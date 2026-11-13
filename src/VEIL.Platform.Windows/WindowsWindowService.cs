using System.Globalization;
using System.Text;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>
/// Top-level window enumeration and reveal/hide using TOPMOST layering. Handles are validated before every use and
/// identity comes from the executable path, never the title (architecture 5.3). FR-WIN-001..007.
///
/// These windows belong to other programs, and any of them may be frozen. Every call that could wait for such a program is
/// therefore either bounded (titles are read with a timeout) or asynchronous (layering changes are posted, not awaited),
/// and frozen windows are skipped. A frozen application must never be able to stall VEIL (ADR-009).
/// </summary>
public sealed class WindowsWindowService : IWindowService
{
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    /// <summary>Upper bound for one enumeration, so a desktop full of slow windows still answers.</summary>
    private static readonly TimeSpan EnumerationBudget = TimeSpan.FromSeconds(3);
    private const uint TitleTimeoutMs = 250;
    private const uint TitleFlags = Native.SMTO_ABORTIFHUNG | Native.SMTO_BLOCK;

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        var result = new List<WindowInfo>();
        var processPaths = new Dictionary<uint, string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Native.EnumWindows((hWnd, _) =>
        {
            if (clock.Elapsed > EnumerationBudget) return false;
            try
            {
                var info = TryDescribe(hWnd, processPaths);
                if (info is not null) result.Add(info);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* one odd window must not hide all the others */ }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Window text with a timeout; null when the window does not answer or has no title.</summary>
    private static string? ReadTitle(IntPtr hWnd)
    {
        if (Native.SendMessageTimeout(hWnd, Native.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, TitleFlags, TitleTimeoutMs, out var length) == IntPtr.Zero)
            return null;
        var n = Math.Min((int)length, 1024);
        if (n <= 0) return null;
        var sb = new StringBuilder(n + 1);
        if (Native.SendMessageTimeoutText(hWnd, Native.WM_GETTEXT, new IntPtr(n + 1), sb, TitleFlags, TitleTimeoutMs, out _) == IntPtr.Zero)
            return null;
        return sb.Length == 0 ? null : sb.ToString();
    }

    private WindowInfo? TryDescribe(IntPtr hWnd, Dictionary<uint, string> processPaths)
    {
        var minimized = Native.IsIconic(hWnd);
        if (!Native.IsWindowVisible(hWnd) && !minimized) return null;
        if (Native.GetWindow(hWnd, Native.GW_OWNER) != IntPtr.Zero) return null; // owned dialogs/popups are transient
        var ex = (long)Native.GetWindowLongPtr(hWnd, Native.GWL_EXSTYLE);
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return null;
        if (Native.DwmGetWindowAttribute(hWnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return null;

        var tid = Native.GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == _ownPid) return null;
        if (Native.IsHungAppWindow(hWnd)) return null; // cannot be shown or hidden reliably, and asking it for a title could stall

        var title = ReadTitle(hWnd);
        if (title is null) return null;

        if (!processPaths.TryGetValue(pid, out var path))
            processPaths[pid] = path = ResolveExecutable(pid);

        Native.GetWindowRect(hWnd, out var r);
        var monitor = Native.MonitorFromWindow(hWnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
        var displayId = Native.GetMonitorInfo(monitor, ref mi) ? mi.szDevice : string.Empty;

        return new WindowInfo(
            RuntimeId: Format(hWnd, pid, tid),
            ProcessId: pid.ToString(CultureInfo.InvariantCulture),
            ApplicationId: path,
            Title: title,
            DisplayId: displayId,
            Bounds: new WindowBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
            IsVisible: !minimized,
            IsMinimized: minimized,
            IsAllowed: false,
            Kind: WindowKind.Normal);
    }

    /// <summary>Unresolvable (typically elevated) processes get an "unknown:" identity that no rule can match: default deny.</summary>
    private static string ResolveExecutable(uint pid)
    {
        var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return $"unknown:{pid}";
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString().ToLowerInvariant() : $"unknown:{pid}";
        }
        finally { Native.CloseHandle(h); }
    }

    private static string Format(IntPtr hWnd, uint pid, uint tid) =>
        string.Create(CultureInfo.InvariantCulture, $"{hWnd.ToInt64():X}-{pid}-{tid}");

    /// <summary>Runtime id for a live window handle (null if the handle is not a window).</summary>
    public static string? RuntimeIdFor(IntPtr hWnd)
    {
        if (!Native.IsWindow(hWnd)) return null;
        var tid = Native.GetWindowThreadProcessId(hWnd, out var pid);
        return Format(hWnd, pid, tid);
    }

    /// <summary>
    /// Window handles are recycled by the OS (observed: same HWND value reissued after ~33k create/destroy cycles).
    /// The id therefore carries the owning process and thread, and both must still match before any action.
    /// Reuse by a window of the same thread cannot be detected with handles alone.
    /// </summary>
    private static bool TryParse(string runtimeId, out IntPtr hWnd)
    {
        hWnd = IntPtr.Zero;
        var parts = runtimeId.Split('-');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            || !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var tid)) return false;
        var h = new IntPtr(v);
        if (!Native.IsWindow(h)) return false; // stale handles are rejected
        var nowTid = Native.GetWindowThreadProcessId(h, out var nowPid);
        if (nowPid != pid || nowTid != tid) return false; // handle now belongs to a different window
        hWnd = h;
        return true;
    }

    public bool Reveal(string runtimeId)
    {
        if (!TryParse(runtimeId, out var hWnd)) return false;
        if (Native.IsHungAppWindow(hWnd)) return false; // never lift a frozen window above the overlay
        if (Native.IsIconic(hWnd)) Native.ShowWindowAsync(hWnd, Native.SW_RESTORE);
        var ok = Native.SetWindowPos(hWnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_SHOWWINDOW | Native.SWP_ASYNCWINDOWPOS);
        Native.SetForegroundWindow(hWnd); // may be refused by the OS; the window is still layered above the overlay
        return ok;
    }

    public bool Hide(string runtimeId)
    {
        if (!TryParse(runtimeId, out var hWnd)) return false;
        // Asynchronous: lowering a window of a frozen program must return at once, like everything on the recovery path.
        return Native.SetWindowPos(hWnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
    }
}
