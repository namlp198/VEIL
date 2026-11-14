using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Application;

/// <summary>
/// The dock at the left edge of the Focus screen: every allowed application as a tile, opened with Tab, plus the workspace
/// chips. It also owns the two display modes of Focus Mode: <b>Panel</b> (full lock: the overlay covers the screen and the
/// list opens on demand) and <b>Tray</b> (no overlay; a slim strip of pinned applications stays on the screen edge and a
/// window dragged onto it is put away there). A hotkey switches the mode; Focus always starts as a full lock.
/// Pins are the user's own choice and live in the user state, not in the protected machine configuration.
///
/// Only allowed windows are ever listed, and showing one still goes through <see cref="FocusService.Reveal"/>, so policy is
/// enforced exactly as for the Window Selector. The view's events arrive on a worker thread; one lock guards the state.
/// </summary>
public sealed class DockController : IDisposable
{
    public const int MaxPinned = 8;

    private readonly FocusService _focus;
    private readonly WindowSelectorService _selector;
    private readonly IDockView _view;
    private readonly Func<SelectorPlacement> _placement;
    private readonly IUserStateStore? _state;
    private readonly ProfileManager? _profiles;
    private readonly object _gate = new();
    private readonly List<string> _pinned = [];
    private DockMode _mode;
    private bool _active, _expanded;
    private string _status = string.Empty;

    public DockController(FocusService focus, WindowSelectorService selector, IDockView view, Func<SelectorPlacement> placement, IUserStateStore? state = null, ProfileManager? profiles = null)
    {
        _focus = focus;
        _selector = selector;
        _view = view;
        _placement = placement;
        _state = state;
        _profiles = profiles;
        try
        {
            var saved = state?.LoadDock();
            if (saved is not null)
            {
                foreach (var id in saved.PinnedApps ?? []) if (!Has(id) && _pinned.Count < MaxPinned) _pinned.Add(id);
            }
        }
        catch { /* a damaged state file only loses the layout */ }
        _view.TileClicked += OnTile;
        _view.WindowDropped += OnDrop;
        _view.RemoveRequested += OnRemove;
        _view.LockRequested += OnLock;
        _view.WorkspaceRequested += OnWorkspace;
        _view.PinRequested += OnPin;
        _view.UnpinRequested += OnUnpin;
        _view.ExpandToggleRequested += OnToggle;
    }

    /// <summary>Raised after the dock's state or the shown windows changed. Handlers must not block (they run under the dock's lock).</summary>
    public event Action? Changed;

    /// <summary>Diagnostics: what was dropped on the tray and what became of it.</summary>
    public Action<string>? Log { get; set; }

    public DockMode Mode { get { lock (_gate) return _mode; } }
    public bool IsExpanded { get { lock (_gate) return _expanded; } }
    public bool IsActive { get { lock (_gate) return _active; } }
    public IReadOnlyList<string> PinnedApps { get { lock (_gate) return _pinned.ToList(); } }

    /// <summary>The dock is on screen: expanded in either mode, or the collapsed strip in Tray mode.</summary>
    public bool IsOnScreen { get { lock (_gate) return _active && (_expanded || _mode == DockMode.Tray); } }

    /// <summary>Focus Mode started. In Tray mode the strip appears at once.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_active) return;
            _active = true;
            _mode = DockMode.Panel; // Focus always begins as the full lock
            _expanded = false;
            _status = string.Empty;
            Sync();
        }
    }

    /// <summary>Focus Mode ended (also on Emergency Recovery): the dock goes away without waiting for anything for long.</summary>
    public void Stop()
    {
        var locked = Monitor.TryEnter(_gate, TimeSpan.FromSeconds(1));
        try
        {
            if (locked) { _active = false; _expanded = false; _mode = DockMode.Panel; }
            _view.Hide();
        }
        finally { if (locked) Monitor.Exit(_gate); }
    }

    /// <summary>Tab (or the dock hotkey): open the full list, or fold it away again.</summary>
    public void Toggle() { lock (_gate) { if (!_active) return; _expanded = !_expanded; _status = string.Empty; Sync(); } }

    public void Collapse() { lock (_gate) { if (!_active || !_expanded) return; _expanded = false; Sync(); } }

    /// <summary>Picks up windows that opened or closed since the last look. Cheap when nothing is on screen.</summary>
    public void Refresh() { lock (_gate) { if (_active && (_expanded || _mode == DockMode.Tray)) Sync(); } }

    /// <summary>Full lock <-> tray. The overlay is taken away or put back first; the dock follows only if that worked.</summary>
    public ActionResult ToggleMode() => SetMode(_mode == DockMode.Panel ? DockMode.Tray : DockMode.Panel);

    public ActionResult SetMode(DockMode mode)
    {
        lock (_gate)
        {
            if (!_active) return new(false, "Focus Mode is not active.");
            if (_mode == mode) return new(true, mode == DockMode.Tray ? "Already in tray mode." : "Already in full lock.");
            var r = _focus.SetTrayMode(mode == DockMode.Tray);
            if (!r.Success) return Say(false, r.Message);
            _mode = mode;
            _expanded = false;
            Say(true, r.Message);
            Sync();
            return r;
        }
    }

    /// <summary>
    /// Tray mode: a window was dragged onto the tray. An allowed application is added to the tray (its window stays where it is,
    /// to be used in the full lock); anything else
    /// is refused with the policy's reason, exactly as the selector would (AT-005/AT-006).
    /// </summary>
    public ActionResult Drop(string runtimeId)
    {
        lock (_gate)
        {
            if (!_active || _mode != DockMode.Tray) return new(false, "Dropping windows works in tray mode.");
            var entry = _selector.BuildEntries().FirstOrDefault(e => e.Window.RuntimeId == runtimeId);
            if (entry is null) return Refuse("That window is no longer available.");
            if (!entry.IsAllowed) return Refuse($"Not allowed: {entry.Decision.Reason}");
            var app = entry.Window.ApplicationId;
            var pin = Has(app) ? new ActionResult(true, string.Empty) : Pin(app);
            if (!pin.Success) return Refuse(pin.Message);
            Log?.Invoke($"[dock] {AppName(app)} added to the tray.");
            Say(true, $"{AppName(app)} added to the tray.");
            Sync();
            return new ActionResult(true, _status);
        }
    }

    private ActionResult Refuse(string message) { Log?.Invoke("[dock] drop refused: " + message); var r = Say(false, message); Sync(); return r; }

    public ActionResult SelectWorkspace(string? name)
    {
        if (_profiles is null) return new(false, "Workspaces are not available.");
        var r = _profiles.Select(name);
        lock (_gate) { _status = r.Message; Sync(); }
        return r;
    }

    public ActionResult Pin(string applicationId)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(applicationId) || applicationId.StartsWith("unknown:", StringComparison.Ordinal))
                return Say(false, "That application cannot be pinned.");
            if (Has(applicationId)) return Say(true, "Already in the tray.");
            if (_pinned.Count >= MaxPinned) return Say(false, $"The tray holds at most {MaxPinned} applications.");
            // Only applications the policy allows are offered as tiles, but check again: the list may have changed.
            var entry = _selector.BuildEntries(onlyAllowed: true).FirstOrDefault(e => Same(e.Window.ApplicationId, applicationId));
            if (entry is null) return Say(false, "Only allowed applications can be pinned.");
            _pinned.Add(entry.Window.ApplicationId);
            Save();
            return Say(true, $"{AppName(entry.Window.ApplicationId)} pinned to the tray.");
        }
    }

    public ActionResult Unpin(string applicationId)
    {
        lock (_gate)
        {
            var i = _pinned.FindIndex(p => Same(p, applicationId));
            if (i < 0) return Say(true, "Not in the tray.");
            _pinned.RemoveAt(i);
            Save();
            return Say(true, $"{AppName(applicationId)} removed from the tray.");
        }
    }

    /// <summary>Shows the window if it is hidden, hides it if it is shown. A refused reveal reports the policy message.</summary>
    public ActionResult Activate(string runtimeId)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(runtimeId)) return Say(false, "That application has no window open.");
            if (_focus.RevealedWindowIds.Contains(runtimeId))
            {
                var hidden = _focus.Hide(runtimeId);
                _status = hidden.Message;
                Sync();
                return hidden;
            }
            var r = _focus.Reveal(runtimeId);
            if (r.Success && _mode == DockMode.Panel) _expanded = false; // done choosing; Tray stays for the next switch
            _status = r.Message;
            Sync();
            return r;
        }
    }

    // ---- view events ---------------------------------------------------------------------------------------------------

    private void OnTile(string id) => Activate(id);
    private void OnDrop(string id) => Drop(id);
    private void OnLock() => SetMode(DockMode.Panel);
    private void OnRemove(string app) { lock (_gate) { Unpin(app); Sync(); } } // the X on a tile takes it out of the tray
    private void OnWorkspace(string? name) => SelectWorkspace(name);
    private void OnPin(string app) { lock (_gate) { Pin(app); Sync(); } }
    private void OnUnpin(string app) { lock (_gate) { Unpin(app); Sync(); } }
    private void OnToggle() => Toggle();

    // ---- state ---------------------------------------------------------------------------------------------------------

    private ActionResult Say(bool ok, string message) { _status = message; return new(ok, message); }
    private bool Has(string id) => _pinned.Any(p => Same(p, id));
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string AppName(string applicationId) =>
        applicationId.StartsWith("unknown:", StringComparison.Ordinal) ? "unknown application" : Path.GetFileNameWithoutExtension(applicationId);

    private void Save()
    {
        try { _state?.SaveDock(new DockSettings(DockMode.Panel, _pinned.ToList())); } catch { /* the layout is a convenience */ }
    }

    /// <summary>Builds what the view should show and shows, updates or hides it. Caller holds the lock.</summary>
    private void Sync()
    {
        try
        {
            if (!_active || !(_expanded || _mode == DockMode.Tray)) { _view.Hide(); return; }
            var state = Build();
            if (_view.IsVisible) _view.Update(state);
            else _view.Show(state, _placement());
        }
        finally { Changed?.Invoke(); }
    }

    private DockViewState Build()
    {
        var shown = _focus.RevealedWindowIds.ToHashSet();
        var entries = _selector.BuildEntries(onlyAllowed: true);
        DockTile Tile(SelectorEntry e) => new(e.Window.RuntimeId, e.Window.ApplicationId, AppName(e.Window.ApplicationId), e.Window.Title,
            shown.Contains(e.Window.RuntimeId), Has(e.Window.ApplicationId));

        // The list holds the applications in the tray: the ones the user chose to work with in this lock.
        var tiles = entries.Where(e => Has(e.Window.ApplicationId)).Select(Tile).ToList();
        var pinned = _pinned.Select(app =>
        {
            var windows = entries.Where(e => Same(e.Window.ApplicationId, app)).ToList();
            var first = windows.FirstOrDefault(e => shown.Contains(e.Window.RuntimeId)) ?? windows.FirstOrDefault();
            return new DockTile(first?.Window.RuntimeId ?? string.Empty, app, AppName(app), first?.Window.Title ?? "Not running",
                windows.Any(e => shown.Contains(e.Window.RuntimeId)), true);
        }).ToList();
        return new DockViewState(_mode, _expanded, tiles, pinned, _status, _profiles?.Names, _profiles?.ActiveName);
    }

    public void Dispose()
    {
        _view.TileClicked -= OnTile;
        _view.WindowDropped -= OnDrop;
        _view.RemoveRequested -= OnRemove;
        _view.LockRequested -= OnLock;
        _view.WorkspaceRequested -= OnWorkspace;
        _view.PinRequested -= OnPin;
        _view.UnpinRequested -= OnUnpin;
        _view.ExpandToggleRequested -= OnToggle;
        try { _view.Hide(); } catch { /* shutting down */ }
    }
}
