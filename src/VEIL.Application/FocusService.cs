using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Application;

public sealed record ActionResult(bool Success, string Message);

/// <summary>
/// Orchestrates Focus Mode (FR-LIFE-002, FR-WIN-004/005/006, FR-APP-002). All platform access goes through
/// abstractions (NFR-MAINT-001).
/// </summary>
public sealed class FocusService : IDisposable
{
    private readonly FocusStateMachine _machine;
    private readonly IDisplayService _displays;
    private readonly IWindowService _windows;
    private readonly OverlayManager _overlays;
    private readonly RecoverySupervisor _recovery;
    private readonly Func<ApplicationPolicy> _policy;
    private readonly IDiagnosticSink _diag;
    private readonly object _gate = new();
    // Separate from _gate on purpose: Emergency Recovery must be able to read and clear this even while FocusOn/FocusOff
    // is blocked inside the platform (ADR-009).
    private readonly object _revealedGate = new();
    private readonly HashSet<string> _revealed = new();
    private volatile bool _tray;

    public FocusService(
        FocusStateMachine machine,
        IDisplayService displays,
        IWindowService windows,
        OverlayManager overlays,
        RecoverySupervisor recovery,
        Func<ApplicationPolicy> policy,
        IDiagnosticSink? diagnostics = null)
    {
        _machine = machine;
        _displays = displays;
        _windows = windows;
        _overlays = overlays;
        _recovery = recovery;
        _policy = policy;
        _diag = diagnostics ?? NullDiagnosticSink.Instance;
        _displays.TopologyChanged += OnTopologyChanged;
    }

    public FocusState State => _machine.State;
    public string? LastError { get; private set; }

    /// <summary>
    /// Tray mode: Focus Mode is on, but no overlay covers the screen. Windows are put away by minimizing them (there is no
    /// overlay to hide behind) and the dock's tray strip is the way back to them. See <see cref="SetTrayMode"/>.
    /// </summary>
    public bool IsTrayMode => _tray;
    public IReadOnlyCollection<string> RevealedWindowIds { get { lock (_revealedGate) return _revealed.ToList(); } }

    /// <summary>Focus On: arm recovery first, then create overlays. FR-LIFE-001, architecture 10.</summary>
    public ActionResult FocusOn()
    {
        lock (_gate)
        {
            if (_machine.State is FocusState.Focused or FocusState.SelectorOpen)
                return new(true, "Focus Mode is already active.");
            if (!_machine.TryTransition(FocusState.Preparing))
                return new(false, $"Cannot enter Focus Mode from state {_machine.State}.");
            try
            {
                _tray = false;
                _recovery.Arm();
                _overlays.Show();
                _machine.Transition(FocusState.Focused);
                LastError = null;
                return new(true, $"Focus Mode active on {_overlays.Count} display(s).");
            }
            catch (Exception ex)
            {
                _overlays.Teardown();
                LastError = ex.Message;
                _diag.Report($"Focus On failed: {ex.Message}");
                _machine.TryTransition(FocusState.ErrorSafe);
                return new(false, $"Focus Mode could not start: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Switches between the full lock (overlays on every display) and tray mode (no overlays). Recovery stays armed in both,
    /// and Focus Off / Emergency Recovery work the same. Windows shown above the overlay go back to normal layering first.
    /// </summary>
    public ActionResult SetTrayMode(bool tray)
    {
        lock (_gate)
        {
            if (_machine.State is not (FocusState.Focused or FocusState.SelectorOpen))
                return new(false, "Focus Mode is not active.");
            if (_tray == tray) return new(true, tray ? "Already in tray mode." : "Already in full lock.");
            if (tray)
            {
                _tray = true;
                ReleaseRevealed(TakeRevealed());
                _overlays.Teardown();
                return new(true, "Tray mode: the screen is free; drag a window onto the tray to put it away.");
            }
            try
            {
                _overlays.Show();
                _tray = false;
                return new(true, "Full lock: the overlay covers every display again.");
            }
            catch (Exception ex)
            {
                _overlays.Teardown();
                _diag.Report($"Returning to full lock failed: {ex.Message}");
                return new(false, $"Could not cover the screen again: {ex.Message}");
            }
        }
    }

    public ActionResult FocusOff()
    {
        lock (_gate)
        {
            if (_machine.State is FocusState.Inactive) return new(true, "Focus Mode is not active.");
            if (_machine.State is FocusState.ErrorSafe)
            {
                _machine.TryTransition(FocusState.Inactive);
                return new(true, "Cleared error state.");
            }
            if (_machine.State is not (FocusState.Focused or FocusState.SelectorOpen))
                return new(false, $"Cannot leave Focus Mode from state {_machine.State}.");
            var revealed = TakeRevealed();
            _overlays.Teardown();
            _machine.TryTransition(FocusState.Inactive);
            ReleaseRevealed(revealed);
            return new(true, "Focus Mode off. Desktop restored.");
        }
    }

    /// <summary>
    /// Emergency Recovery: unconditional, idempotent, never depends on selector UI (FR-HK-003). Every step is isolated, so
    /// one failing step (a throwing subscriber, a dead window) can never stop the next one, and it takes no lock that a
    /// stuck FocusOn/FocusOff could be holding. The overlays go first: that is what the user is waiting for.
    /// </summary>
    public ActionResult EmergencyRecover()
    {
        Isolated(() => _machine.TryTransition(FocusState.Recovering), "enter recovery");
        Isolated(_recovery.EmergencyTeardown, "recovery teardown");
        Isolated(_overlays.Teardown, "overlay teardown");
        var revealed = TakeRevealed();
        Isolated(() =>
        {
            if (_machine.State is FocusState.Recovering or FocusState.ErrorSafe) _machine.TryTransition(FocusState.Inactive);
        }, "leave recovery");
        ReleaseRevealed(revealed);
        return new(true, "Emergency Recovery complete. Desktop restored.");
    }

    private void Isolated(Action step, string what)
    {
        try { step(); }
        catch (Exception ex) { _diag.Report($"Emergency Recovery step '{what}' failed: {ex.Message}"); }
    }

    private List<string> TakeRevealed()
    {
        lock (_revealedGate)
        {
            var ids = _revealed.ToList();
            _revealed.Clear();
            return ids;
        }
    }

    /// <summary>
    /// Windows shown above the overlay were made TOPMOST. Once the overlay is gone they would stay always-on-top, so they
    /// go back to normal layering. Never blocks: the platform call is asynchronous towards the other process.
    /// </summary>
    private void ReleaseRevealed(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            try { _windows.Hide(id); }
            catch (Exception ex) { _diag.Report($"Could not restore window layering: {ex.Message}"); }
        }
    }

    /// <summary>
    /// The active workspace or the allow-list changed: windows that are no longer permitted go back behind the overlay.
    /// Newly permitted windows are not revealed by themselves; the user still chooses.
    /// </summary>
    public ActionResult RefreshPolicy()
    {
        if (_machine.State is not (FocusState.Focused or FocusState.SelectorOpen)) return new(true, "Focus Mode is not active.");
        var ids = RevealedWindowIds;
        if (ids.Count == 0) return new(true, "Nothing to update.");
        var policy = _policy();
        var windows = _windows.GetWindows();
        var hidden = 0;
        foreach (var id in ids)
        {
            var window = windows.FirstOrDefault(w => w.RuntimeId == id);
            if (window is not null && policy.Evaluate(window).IsAllowed) continue;
            Hide(id);
            hidden++;
        }
        return new(true, hidden == 0 ? "All shown windows are still allowed." : $"{hidden} window(s) returned behind the overlay.");
    }

    /// <summary>Reveals a window above the overlay only if policy permits it (AT-005/AT-006).</summary>
    public ActionResult Reveal(string runtimeId)
    {
        if (_machine.State is not (FocusState.Focused or FocusState.SelectorOpen))
            return new(false, "Focus Mode is not active.");
        var window = _windows.GetWindows().FirstOrDefault(w => w.RuntimeId == runtimeId);
        if (window is null) return new(false, "Window is no longer available.");
        var decision = _policy().Evaluate(window);
        if (!decision.IsAllowed) return new(false, $"Not revealed: {decision.Reason}");
        if (!_windows.Reveal(runtimeId)) return new(false, "The window could not be revealed; the overlay remains in place.");
        lock (_revealedGate) _revealed.Add(runtimeId);
        return new(true, $"Revealed '{window.Title}'.");
    }

    /// <summary>Hides one window behind the overlay without terminating it (FR-WIN-005, FR-APP-006).</summary>
    public ActionResult Hide(string runtimeId)
    {
        var ok = _tray ? _windows.Minimize(runtimeId) : _windows.Hide(runtimeId);
        lock (_revealedGate) _revealed.Remove(runtimeId);
        if (!ok) return new(false, "Window is no longer available.");
        return new(true, _tray ? "Window put away in the tray." : "Window hidden behind the overlay.");
    }

    /// <summary>Hide All: places overlays above every work window; nothing is closed or minimized.</summary>
    public ActionResult HideAll()
    {
        var ids = TakeRevealed();
        foreach (var id in ids) { if (_tray) _windows.Minimize(id); else _windows.Hide(id); }
        return new(true, $"Hid {ids.Count} window(s).");
    }

    private void OnTopologyChanged(object? sender, DisplayTopologyChangedEventArgs e)
    {
        if (_machine.State is not (FocusState.Focused or FocusState.SelectorOpen) || _tray) return;
        try { _overlays.Reconcile(); }
        catch (Exception ex) { _diag.Report($"Display reconciliation failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        _displays.TopologyChanged -= OnTopologyChanged;
        EmergencyRecover();
    }
}
