using System.Diagnostics;
using System.Runtime.Versioning;
using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;
using VEIL.Platform.Windows;

namespace VEIL.App;

/// <summary>
/// Everything Focus Mode needs at run time on Windows: the state machine, overlays, recovery, the selector, the focus
/// timer, workspace profiles and the global hotkey handling. Shared by the one-shot <c>veil focus</c> command and the
/// resident agent (<c>veil run</c>). Emergency Recovery must be registered before Focus Mode can start (architecture 5.2).
///
/// Threading (ADR-009): hotkeys arrive on an ordered command thread, never on the thread that owns the overlay windows,
/// so nothing here can stop the overlay from painting or the recovery key from being heard. If the window thread itself
/// ever stops answering while Focus is on, the process ends: Windows then removes every overlay.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class FocusRuntime : IDisposable
{
    private readonly WindowsPlatform _native;
    private readonly Func<VeilConfiguration> _config;
    private readonly Action<string> _log;
    private readonly WindowSelectorService _selector;
    private readonly DockController _dock;
    private readonly UnlockController _unlock;
    private bool _keysBlocked;
    private readonly object _tabGate = new();
    private const string TabKey = "dock-tab";
    private bool _tabRegistered;
    private readonly List<string> _visible = [];   // reveal order; HideCurrent pops the most recent
    private readonly Timer _sampler;
    private string? _current;
    private bool _emergencyReady;
    private volatile HudSnapshot _hud = HudSnapshot.Empty;
    private double? _cpu, _memory;

    public FocusRuntime(WindowsPlatform native, Func<VeilConfiguration> config, Action<string> log, double? scaleOverride,
        IFocusSessionLog? sessionLog = null, IUserStateStore? userState = null)
    {
        _native = native;
        _config = config;
        _log = log;
        var diag = new LogDiagnostics(log);
        Machine = new FocusStateMachine();
        Profiles = new ProfileManager(config, userState);
        SessionTimer = new FocusTimer(sessionLog, () => Profiles.ActiveName);
        var overlays = new OverlayManager(native.Displays, native.Overlays, diag);
        var recovery = new RecoverySupervisor(overlays.Teardown, diag);
        ApplicationPolicy Policy() => new(_config().Rules, Profiles.Active);
        Focus = new FocusService(Machine, native.Displays, native.Windows, overlays, recovery, Policy, diag);
        _selector = new WindowSelectorService(native.Windows, Policy, () => Focus.RevealedWindowIds);

        // The panel and the tray always sit at the top left of display 1 (the primary display), whatever the pointer does.
        SelectorPlacement PrimaryArea()
        {
            var primary = SelectorPlacementCalculator.PickDisplay(native.Displays.GetDisplays(), null, false);
            return new SelectorPlacement(primary.Bounds, scaleOverride is > 0 ? scaleOverride.Value : (primary.ScaleFactor > 0 ? primary.ScaleFactor : 1.0), primary.Id);
        }
        _dock = new DockController(Focus, _selector, native.Dock, PrimaryArea, userState, Profiles) { Log = log };

        // The lock icon (top right of display 1) opens the password form; the right password ends the full lock.
        _unlock = new UnlockController(native.Unlock, pw => LockPassword.Verify(pw, _config().LockPasswordHash),
            () => SelectorPlacementCalculator.Compute(native.Displays.GetDisplays(), null, false, scaleOverride),
            () => { var r = Focus.FocusOff(); ResetVisible(); if (EndOnOff) EndRequested?.Invoke(); return r; },
            () => { var r = _dock.SetMode(DockMode.Tray); _log(r.Message); ResetVisible(); RefreshHud(); SyncTabKey(); return r; });
        native.Overlays.UnlockRequested = () => Guard(_unlock.Open);
        native.Overlays.TrayRequested = () => Guard(_unlock.OpenForTray); // the tray icon asks for the password; the hotkey does not

        _dock.Changed += () => OffThread(SyncTabKey); // entering or leaving tray mode changes who owns Tab
        native.Overlays.ScaleOverride = scaleOverride;
        native.Overlays.HudProvider = BuildHud;

        // Faults that were contained on platform threads, and the last-resort watchdog.
        native.Fault += message => _log("[fault] " + message);
        native.MessageThreadStalled += OnWindowThreadStalled;

        // Whatever happens to Focus Mode, these follow. They run on the thread pool: this event can fire on the recovery
        // path, which must never wait for the selector window or for a file write.
        Machine.StateChanged += (_, e) =>
        {
            var ended = e.Current is FocusState.Inactive or FocusState.Recovering or FocusState.ErrorSafe;
            if (ended) { OffThread(() => SessionTimer.Stop()); OffThread(_unlock.Close); }
            OffThread(SyncDock);
            OffThread(OnStateForSampler);
        };
        SessionTimer.Changed += () => OffThread(RefreshHud);
        SessionTimer.SessionEnded += record => { if (record.Completed) OffThread(_native.SoftChime); };
        Profiles.Changed += () => _native.PostCommand(() =>
        {
            Guard(() => { if (Machine.State is FocusState.Focused or FocusState.SelectorOpen) _log(Focus.RefreshPolicy().Message); });
            RefreshHud();
            _dock.Refresh(); // the panel and tray show the new workspace at once
        });
        native.Hotkeys.HotkeyPressed += (_, e) => Handle(e.Command);
        native.Hotkeys.TransientPressed += (_, e) =>
        {
            if (e.Name != TabKey) return;
            Guard(() => { _dock.Toggle(); SyncTabKey(); });
        };

        _sampler = new Timer(_ => Guard(() => { Sample(); RefreshHud(); _dock.Refresh(); SyncTabKey(); }), null, Timeout.Infinite, Timeout.Infinite);
    }

    public FocusService Focus { get; }
    public FocusStateMachine Machine { get; }
    public FocusTimer SessionTimer { get; }
    public ProfileManager Profiles { get; }

    /// <summary>When true, the Focus Off and Emergency Recovery hotkeys also raise <see cref="EndRequested"/> (one-shot mode).</summary>
    public bool EndOnOff { get; set; }
    public event Action? EndRequested;

    public IReadOnlyList<HotkeyBinding> Registered { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    public bool EmergencyReady => _emergencyReady;

    /// <summary>Registers the configured hotkeys. Returns an error only when Emergency Recovery itself could not be registered.</summary>
    public string? Start(bool includeFocusOn)
    {
        var registered = new List<HotkeyBinding>();
        var warnings = new List<string>();
        foreach (var b in _config().Hotkeys.Where(h => h.Enabled && (includeFocusOn || h.Command != HotkeyCommand.FocusOn)))
        {
            var reg = _native.Hotkeys.Register(b);
            if (reg.Success) { registered.Add(b); if (b.Command == HotkeyCommand.EmergencyRecovery) _emergencyReady = true; }
            else { warnings.Add($"{b.Command}: {reg.Error}"); _log($"Hotkey {b.Command}: {reg.Error}"); }
        }
        Registered = registered;
        Warnings = warnings;
        return _emergencyReady ? null : "Emergency Recovery could not be registered; refusing to enter Focus Mode.";
    }

    /// <summary>Enters Focus Mode unless the recovery shortcut is missing.</summary>
    public ActionResult TryFocusOn()
    {
        if (!_emergencyReady) return new(false, "Emergency Recovery shortcut is unavailable, so Focus Mode is disabled.");
        _native.ReducedMotion = _config().Preferences.ReducedMotion;
        Sample(); // primes the CPU counter so the first reading on screen is real
        RefreshHud();
        return Focus.FocusOn();
    }

    public ActionResult ToggleFocus() =>
        Machine.State is FocusState.Focused or FocusState.SelectorOpen ? Focus.FocusOff() : TryFocusOn();

    /// <summary>Starts a focus session of <paramref name="minutes"/> (default from preferences), entering Focus Mode if needed.</summary>
    public ActionResult StartTimer(int? minutes = null)
    {
        var m = minutes ?? _config().Preferences.DefaultTimerMinutes;
        if (Machine.State is not (FocusState.Focused or FocusState.SelectorOpen))
        {
            var on = TryFocusOn();
            if (!on.Success) return on;
        }
        var started = SessionTimer.Start(TimeSpan.FromMinutes(m));
        RefreshHud();
        return started;
    }

    public ActionResult RevealFirst(string filter)
    {
        var all = _selector.BuildEntries(filter);
        var match = all.FirstOrDefault(e => e.IsAllowed && !_visible.Contains(e.Window.RuntimeId)) ?? all.FirstOrDefault();
        if (match is null) return new(false, $"No window matches '{filter}'.");
        var r = Focus.Reveal(match.Window.RuntimeId);
        if (r.Success) { _current = match.Window.RuntimeId; _visible.Add(_current); }
        return r;
    }

    /// <summary>Runs on the ordered command thread (or the dedicated recovery thread). Never lets an exception escape.</summary>
    private void Handle(HotkeyCommand command)
    {
        try
        {
            HandleCore(command);
        }
        catch (Exception ex)
        {
            _log($"[hotkey] {command} failed: {ex.Message}");
            // A failed command must never leave the user behind an overlay they cannot dismiss.
            if (command == HotkeyCommand.EmergencyRecovery) Guard(() => Focus.EmergencyRecover());
        }
    }

    private void HandleCore(HotkeyCommand command)
    {
        _log($"[hotkey] {command}");
        if (command == HotkeyCommand.EmergencyRecovery) Guard(() => { _native.KeyGuard.BlockWindowsKeys(false); _keysBlocked = false; }); // keys back first
        switch (command)
        {
            case HotkeyCommand.FocusOn:
                // In tray mode the Focus On key brings the full lock back, like the Lock button.
                if (Focus.IsTrayMode) { _log(_dock.SetMode(DockMode.Panel).Message); ResetVisible(); RefreshHud(); SyncTabKey(); }
                else _log(TryFocusOn().Message);
                break;
            case HotkeyCommand.FocusOff: _log(Focus.FocusOff().Message); ResetVisible(); if (EndOnOff) EndRequested?.Invoke(); break;
            case HotkeyCommand.EmergencyRecovery: _log(Focus.EmergencyRecover().Message); ResetVisible(); if (EndOnOff) EndRequested?.Invoke(); break;
            case HotkeyCommand.HideAll: _log(Focus.HideAll().Message); ResetVisible(); RefreshHud(); break;
            case HotkeyCommand.HideCurrent:
                if (_visible.Count == 0) { _log("Nothing to hide."); break; }
                var target = _visible[^1];
                _visible.RemoveAt(_visible.Count - 1);
                _log(Focus.Hide(target).Message);
                if (_current == target) _current = _visible.Count > 0 ? _visible[^1] : null;
                RefreshHud();
                break;
            case HotkeyCommand.Dock: _dock.Toggle(); SyncTabKey(); break;
            case HotkeyCommand.TrayMode: _log(_dock.ToggleMode().Message); ResetVisible(); RefreshHud(); SyncTabKey(); break;
            case HotkeyCommand.NextWindow or HotkeyCommand.PreviousWindow:
                var pick = WindowSelectorService.Cycle(_selector.BuildEntries(), _current, command == HotkeyCommand.NextWindow ? 1 : -1);
                if (pick is null) { _log("No allowed windows to reveal."); break; }
                if (_current is not null && _current != pick.Window.RuntimeId) { Focus.Hide(_current); _visible.Remove(_current); }
                var r = Focus.Reveal(pick.Window.RuntimeId);
                _log(r.Message);
                if (r.Success) { _current = pick.Window.RuntimeId; _visible.Remove(_current); _visible.Add(_current); }
                RefreshHud();
                break;
            case HotkeyCommand.FocusTimer:
                _log(SessionTimer.IsRunning ? SessionTimer.Stop().Message : StartTimer().Message);
                break;
            case HotkeyCommand.NextProfile:
                _log(Profiles.Next().Message);
                break;
        }
    }

    private void ResetVisible() { _current = null; _visible.Clear(); }

    // ---- what the Focus screen shows -----------------------------------------------------------------------------------

    /// <summary>Called by the overlay while painting, on the window thread: cached values and one tiny lock, nothing else.</summary>
    private HudSnapshot BuildHud()
    {
        var timer = SessionTimer.Snapshot();
        return _hud with
        {
            Now = DateTime.Now,
            TimerRemaining = timer.Phase == TimerPhase.Running ? timer.Remaining : null,
            TimerProgress = timer.Progress,
            Notice = timer.Notice,
        };
    }

    private void Sample()
    {
        var (cpu, memory) = _native.SystemStats.Sample();
        _cpu = cpu ?? _cpu;
        _memory = memory ?? _memory;
    }

    /// <summary>Shows or hides the dock with Focus Mode, and keeps the Tab key in step. Runs off the recovery path.</summary>
    private void SyncDock()
    {
        var on = Machine.State is FocusState.Focused or FocusState.SelectorOpen;
        if (on) _dock.Start(); else _dock.Stop();
        SyncTabKey();
    }

    /// <summary>
    /// Tab opens the allowed-apps panel and closes it again. It is a bare key, so while it is registered no other program
    /// receives it: VEIL therefore owns it for the whole full lock, including while apps are shown, so the panel can always be
    /// reached to bring up another app (several can be shown at once). It is given back while the Window Selector is open
    /// (the selector uses Tab between its controls) and in tray mode, where the real desktop is in use and the panel opens
    /// with its hotkey or the tray's "..." button instead.
    /// </summary>
    private void SyncTabKey()
    {
        lock (_tabGate)
        {
            var state = Machine.State;
            // The Windows keys do nothing during the full lock (and only then).
            var blockKeys = state is FocusState.Focused or FocusState.SelectorOpen && !Focus.IsTrayMode;
            if (blockKeys != _keysBlocked) { _native.KeyGuard.BlockWindowsKeys(blockKeys); _keysBlocked = blockKeys; }
            var want = state == FocusState.Focused && !Focus.IsTrayMode;
            if (want == _tabRegistered) return;
            if (want)
            {
                var r = _native.Hotkeys.RegisterTransient(TabKey, KeyModifiers.None, "Tab");
                _tabRegistered = r.Success;
                if (!r.Success) _log($"[dock] Tab could not be reserved: {r.Error}");
            }
            else
            {
                _native.Hotkeys.UnregisterTransient(TabKey);
                _tabRegistered = false;
            }
        }
    }

    private void RefreshHud()
    {
        var prefs = _config().Preferences;
        _hud = new HudSnapshot(DateTime.Now, prefs.ShowClock, prefs.ShowSystemStatus, Profiles.ActiveName,
            Focus.RevealedWindowIds.Count, CpuPercent: _cpu, MemoryPercent: _memory);
    }

    private void OnStateForSampler()
    {
        var on = Machine.State is FocusState.Focused or FocusState.SelectorOpen;
        _sampler.Change(on ? TimeSpan.FromSeconds(2) : Timeout.InfiniteTimeSpan, on ? TimeSpan.FromSeconds(2) : Timeout.InfiniteTimeSpan);
        if (on) RefreshHud();
    }

    // ---- safety net ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The thread that owns the overlay windows has not answered for several seconds. With Focus on, the screen is covered by
    /// windows nobody can remove and the recovery key can no longer be heard, so the one thing left is to end the process:
    /// Windows destroys every window it owns. The agent can then simply be started again.
    /// </summary>
    private void OnWindowThreadStalled(TimeSpan silent)
    {
        _log($"[watchdog] The window thread has not responded for {silent.TotalSeconds:0} s (Focus state: {Machine.State}).");
        if (Machine.State == FocusState.Inactive) return; // no overlay to get stuck behind; the agent merely needs a restart
        _log("[watchdog] Ending VEIL so Windows removes the overlays. Start VEIL again to continue.");
        try { Process.GetCurrentProcess().Kill(); }
        catch { Environment.FailFast("VEIL window thread stalled while Focus Mode was active."); }
    }

    private static void OffThread(Action work) => ThreadPool.UnsafeQueueUserWorkItem(_ => Guard(work), null);

    private static void Guard(Action work)
    {
        try { work(); } catch { /* background follow-up work must never take the process down */ }
    }

    public void Dispose()
    {
        _sampler.Dispose();
        SessionTimer.Dispose();
        _dock.Dispose();
        _unlock.Dispose();
        Guard(() => _native.KeyGuard.BlockWindowsKeys(false));
        lock (_tabGate) { if (_tabRegistered) { _native.Hotkeys.UnregisterTransient(TabKey); _tabRegistered = false; } }
        Focus.Dispose(); // tears every overlay down
    }

    private sealed class LogDiagnostics(Action<string> log) : IDiagnosticSink
    {
        public void Report(string message) => log("[diag] " + message);
    }
}
