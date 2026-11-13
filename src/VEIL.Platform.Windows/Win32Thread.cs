using System.Collections.Concurrent;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// One dedicated message-pump thread owning every window VEIL creates (overlays, selector, hidden notification window).
///
/// The rule that keeps the desktop safe (ADR-009): <b>this thread never runs application logic and never waits on another
/// process.</b> Hotkeys, display changes and selector input only enqueue work for a separate command thread; Emergency
/// Recovery gets its own thread so it cannot queue behind anything. If this thread were blocked, the overlay would stop
/// painting and the recovery hotkey would never be dispatched, which is exactly the "frozen screen, no key works" failure.
/// A heartbeat monitor watches the thread as a last line of defence.
/// </summary>
internal sealed class Win32Thread : IDisposable
{
    private const string HiddenClass = "VEIL.Notify";
    private static readonly uint WmInvoke = Native.WM_APP + 1;
    private static readonly uint WmPing = Native.WM_APP + 2;

    /// <summary>How long a caller waits for the window thread before giving up (it normally answers in microseconds).</summary>
    public static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(4);
    /// <summary>A window thread silent for this long is considered wedged.</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(5);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly BlockingCollection<Action> _commands = new();
    private readonly Thread _commandThread;
    private readonly Native.WndProc _proc;
    private HeartbeatMonitor? _monitor;
    private uint _threadId;
    private volatile bool _disposed;

    public IntPtr NotifyWindow { get; private set; }

    /// <summary>Window that gets dialog-style keyboard handling (Tab, Enter, Esc). Zero when none.</summary>
    public volatile IntPtr DialogWindow;

    /// <summary>Raised ON the window thread: subscribers must only enqueue work (see <see cref="Post"/>), never do it.</summary>
    public event Action<IntPtr, IntPtr>? Hotkey;
    public event Action? DisplayChanged;

    /// <summary>An unexpected exception was contained instead of taking the process down.</summary>
    public event Action<string>? Fault;

    /// <summary>The window thread stopped answering. Raised once per stall, on a monitor thread.</summary>
    public event Action<TimeSpan>? Stalled;

    public Win32Thread()
    {
        _proc = WindowProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "VEIL.Win32" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Win32 message thread failed to start.");

        _commandThread = new Thread(CommandLoop) { IsBackground = true, Name = "VEIL.Commands" };
        _commandThread.Start();

        _monitor = new HeartbeatMonitor(
            ping: () => Native.PostMessage(NotifyWindow, WmPing, IntPtr.Zero, IntPtr.Zero),
            onStall: silent => Stalled?.Invoke(silent),
            interval: TimeSpan.FromSeconds(1),
            stallAfter: StallAfter);
    }

    public bool IsPumpThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    public IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == Native.WM_HOTKEY) Hotkey?.Invoke(wParam, lParam);
            else if (msg == Native.WM_DISPLAYCHANGE) DisplayChanged?.Invoke();
            else if (msg == WmInvoke) Drain();
            else if (msg == WmPing) _monitor?.Beat();
        }
        catch (Exception ex)
        {
            // An exception escaping a window procedure would terminate the process; contain it and report.
            RaiseFault("window thread: " + ex.Message);
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public Native.WndProc ProcDelegate => _proc;

    private void Run()
    {
        var hInstance = Native.GetModuleHandle(null);
        var cls = new Native.WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance,
            lpszClassName = HiddenClass,
        };
        Native.RegisterClassEx(ref cls);
        NotifyWindow = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, HiddenClass, "VEIL", Native.WS_POPUP, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        _threadId = Native.GetCurrentThreadId();
        _ready.Set();

        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            try
            {
                var dlg = DialogWindow;
                if (dlg != IntPtr.Zero && Native.IsDialogMessage(dlg, ref msg)) continue;
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            catch (Exception ex) { RaiseFault("message loop: " + ex.Message); }
        }
        Drain();
        if (NotifyWindow != IntPtr.Zero) Native.DestroyWindow(NotifyWindow);
        Native.UnregisterClass(HiddenClass, hInstance);
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var a))
        {
            try { a(); } catch (Exception ex) { RaiseFault("queued window work: " + ex.Message); }
        }
    }

    // ---- work that must not run on the window thread ------------------------------------------------------------------

    /// <summary>Runs <paramref name="work"/> on the single, ordered command thread. Returns immediately.</summary>
    public void Post(Action work)
    {
        try { _commands.Add(work); }
        catch (InvalidOperationException) { /* shutting down */ }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a thread of its own, ahead of anything queued by <see cref="Post"/>. For Emergency
    /// Recovery only: it must never wait behind a command that is itself stuck.
    /// </summary>
    public void PostUrgent(Action work)
    {
        var t = new Thread(() => Guarded(work)) { IsBackground = true, Name = "VEIL.Urgent" };
        t.Start();
    }

    private void CommandLoop()
    {
        foreach (var work in _commands.GetConsumingEnumerable()) Guarded(work);
    }

    private void Guarded(Action work)
    {
        try { work(); }
        catch (Exception ex) { RaiseFault("command: " + ex.Message); }
    }

    private void RaiseFault(string message)
    {
        try { Fault?.Invoke(message); } catch { /* reporting must not fail */ }
    }

    // ---- marshalling onto the window thread ---------------------------------------------------------------------------

    public T Invoke<T>(Func<T> func)
    {
        if (IsPumpThread) return func();
        if (_disposed) throw new ObjectDisposedException(nameof(Win32Thread));
        T result = default!;
        Exception? error = null;
        // Not disposed on purpose: after a timeout the queued action may still run and signal it.
        var done = new ManualResetEventSlim();
        _queue.Enqueue(() =>
        {
            try { result = func(); } catch (Exception ex) { error = ex; } finally { done.Set(); }
        });
        Native.PostMessage(NotifyWindow, WmInvoke, IntPtr.Zero, IntPtr.Zero);
        if (!done.Wait(InvokeTimeout)) throw new TimeoutException("Win32 message thread did not respond.");
        if (error is not null) throw new InvalidOperationException(error.Message, error);
        return result;
    }

    public void Invoke(Action action) => Invoke<object?>(() => { action(); return null; });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor?.Dispose();
        _commands.CompleteAdding();
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(3));
    }
}
