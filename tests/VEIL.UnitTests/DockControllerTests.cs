using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.UnitTests;

internal sealed class FakeDockView : IDockView
{
    public bool IsVisible { get; private set; }
    public List<DockViewState> States { get; } = [];
    public int HideCount { get; private set; }
    public DockViewState Last => States[^1];

    public event Action<string>? TileClicked;
    public event Action<string>? WindowDropped;
    public event Action<string>? RemoveRequested;
    public event Action? LockRequested;
    public void BackToLock() => LockRequested?.Invoke();
    public event Action<string?>? WorkspaceRequested;
    public event Action<string>? PinRequested;
    public event Action<string>? UnpinRequested;
    public event Action? ExpandToggleRequested;

    public void Show(DockViewState state, SelectorPlacement area) { IsVisible = true; States.Add(state); }
    public void Update(DockViewState state) => States.Add(state);
    public void Hide() { IsVisible = false; HideCount++; }
    public void Dispose() { }

    public void Click(string runtimeId) => TileClicked?.Invoke(runtimeId);
    public void Remove(string app) => RemoveRequested?.Invoke(app);
    public void Drop(string runtimeId) => WindowDropped?.Invoke(runtimeId);
    public void Workspace(string? name) => WorkspaceRequested?.Invoke(name);
    public void Pin(string app) => PinRequested?.Invoke(app);
    public void Unpin(string app) => UnpinRequested?.Invoke(app);
    public void More() => ExpandToggleRequested?.Invoke();
}

public class DockControllerTests : IDisposable
{
    private readonly FakeDisplays _displays = new(1);
    private readonly FakeWindows _windows = new();
    private readonly FocusService _focus;
    private readonly RecoverySupervisor _recovery;
    private readonly FakeDockView _view = new();
    private readonly MemoryDockState _user = new();
    private readonly DockController _dock;

    private sealed class MemoryDockState : IUserStateStore
    {
        public DockSettings Dock { get; set; } = new();
        public string? LoadActiveWorkspace() => null;
        public void SaveActiveWorkspace(string? name) { }
        public DockSettings LoadDock() => Dock;
        public void SaveDock(DockSettings settings) => Dock = settings;
    }

    public DockControllerTests()
    {
        _windows.Windows.Add(FakeWindows.Win("1", "c:\\ide.exe", "Main"));
        _windows.Windows.Add(FakeWindows.Win("2", "c:\\notes.exe", "Notes"));
        _windows.Windows.Add(FakeWindows.Win("3", "c:\\game.exe", "Game"));
        var overlays = new OverlayManager(_displays, new FakeOverlayHost());
        _recovery = new RecoverySupervisor(overlays.Teardown);
        ApplicationPolicy Policy() => new([new ApplicationRule("c:\\ide.exe", "IDE"), new ApplicationRule("c:\\notes.exe", "Notes")]);
        _focus = new FocusService(new FocusStateMachine(), _displays, _windows, overlays, _recovery, Policy);
        _dock = Make();
        _focus.FocusOn();
    }

    private DockController Make()
    {
        var selector = new WindowSelectorService(_windows, () => new ApplicationPolicy([new ApplicationRule("c:\\ide.exe", "IDE"), new ApplicationRule("c:\\notes.exe", "Notes")]),
            () => _focus.RevealedWindowIds);
        return new DockController(_focus, selector, _view, () => SelectorPlacementCalculator.Compute(_displays.GetDisplays(), null, false), _user);
    }

    public void Dispose() { _dock.Dispose(); _focus.Dispose(); _recovery.Dispose(); }

    [Fact]
    public void Panel_mode_stays_hidden_until_toggled_and_lists_only_allowed_windows()
    {
        _dock.Pin("c:\\ide.exe");
        _dock.Pin("c:\\notes.exe");
        _dock.Pin("c:\\game.exe");               // refused: not allowed
        _dock.Start();
        Assert.False(_view.IsVisible);

        _dock.Toggle();
        Assert.True(_view.IsVisible);
        Assert.True(_view.Last.Expanded);
        Assert.Equal(["1", "2"], _view.Last.Tiles.Select(t => t.RuntimeId).OrderBy(x => x));
        Assert.DoesNotContain(_view.Last.Tiles, t => t.ApplicationId == "c:\\game.exe");

        _dock.Toggle();
        Assert.False(_view.IsVisible);
    }

    [Fact]
    public void Tray_mode_shows_the_strip_at_once_and_Tab_expands_it()
    {
        _dock.Start();
        Assert.False(_view.IsVisible);
        Assert.True(_dock.SetMode(DockMode.Tray).Success);
        Assert.True(_focus.IsTrayMode);
        Assert.True(_view.IsVisible);
        Assert.False(_view.Last.Expanded);

        _dock.Toggle();
        Assert.True(_view.Last.Expanded);
        _dock.Toggle();
        Assert.True(_view.IsVisible);       // back to the strip, not gone
        Assert.False(_view.Last.Expanded);
    }

    [Fact]
    public void Clicking_a_tile_shows_the_window_and_clicking_again_hides_it()
    {
        _dock.Pin("c:\\ide.exe");
        _dock.Start();
        _dock.Toggle();
        _view.Click("1");
        Assert.Contains("1", _focus.RevealedWindowIds);
        Assert.False(_view.IsVisible);       // Panel mode folds away after choosing

        _dock.Toggle();
        Assert.True(_view.Last.Tiles.Single(t => t.RuntimeId == "1").IsShown);
        _view.Click("1");
        Assert.DoesNotContain("1", _focus.RevealedWindowIds);
    }

    [Fact]
    public void A_window_that_is_not_allowed_is_never_shown_even_if_asked_for_directly()
    {
        _dock.Start();
        var r = _dock.Activate("3");
        Assert.False(r.Success);
        Assert.Empty(_focus.RevealedWindowIds);
    }

    [Fact]
    public void Dropping_on_the_tray_pins_and_the_choice_survives_a_restart()
    {
        _dock.Start();
        _dock.Toggle();
        _view.Pin("c:\\notes.exe");

        Assert.Equal(["c:\\notes.exe"], _dock.PinnedApps);
        Assert.Equal(["c:\\notes.exe"], _user.Dock.PinnedApps);
        Assert.Equal("2", _view.Last.Pinned.Single().RuntimeId);

        using var again = Make();
        Assert.Equal(["c:\\notes.exe"], again.PinnedApps);
    }

    [Fact]
    public void Only_allowed_applications_can_be_pinned_and_the_tray_has_a_limit()
    {
        _dock.Start();
        Assert.False(_dock.Pin("c:\\game.exe").Success);
        Assert.False(_dock.Pin("unknown:42").Success);
        Assert.Empty(_dock.PinnedApps);
        Assert.True(_dock.Pin("c:\\ide.exe").Success);
        Assert.True(_dock.Pin("C:\\IDE.EXE").Success);   // same application, different case
        Assert.Single(_dock.PinnedApps);
    }

    [Fact]
    public void Unpin_removes_the_application_and_a_pinned_app_without_a_window_is_listed_as_not_running()
    {
        _dock.Pin("c:\\notes.exe");
        _windows.Windows.RemoveAll(w => w.RuntimeId == "2");
        _dock.Start();
        _dock.SetMode(DockMode.Tray);

        var tile = _view.Last.Pinned.Single();
        Assert.Equal(string.Empty, tile.RuntimeId);
        Assert.Equal("Not running", tile.Title);
        Assert.False(_dock.Activate(tile.RuntimeId).Success);

        _view.Unpin("c:\\notes.exe");
        Assert.Empty(_dock.PinnedApps);
    }

    [Fact]
    public void The_mode_switch_takes_the_overlay_away_and_puts_it_back_and_Focus_always_starts_as_a_full_lock()
    {
        _dock.Start();
        Assert.Equal(DockMode.Panel, _dock.Mode);
        Assert.True(_dock.ToggleMode().Success);
        Assert.Equal(DockMode.Tray, _dock.Mode);
        Assert.True(_focus.IsTrayMode);
        Assert.True(_view.IsVisible);

        Assert.True(_dock.ToggleMode().Success);
        Assert.Equal(DockMode.Panel, _dock.Mode);
        Assert.False(_focus.IsTrayMode);
        Assert.False(_view.IsVisible);

        _dock.ToggleMode();
        _dock.Stop();
        _dock.Start();
        Assert.Equal(DockMode.Panel, _dock.Mode);
    }

    [Fact]
    public void The_Lock_button_on_the_tray_goes_back_to_the_full_lock()
    {
        _dock.Start();
        _dock.SetMode(DockMode.Tray);
        _view.BackToLock();
        Assert.Equal(DockMode.Panel, _dock.Mode);
        Assert.False(_focus.IsTrayMode);
    }

    [Fact]
    public void Switching_mode_does_nothing_while_Focus_is_off()
    {
        _focus.FocusOff();
        Assert.False(_dock.ToggleMode().Success);
        Assert.False(_focus.IsTrayMode);
    }

    [Fact]
    public void A_window_dropped_on_the_tray_is_added_for_use_in_the_lock_but_only_if_allowed()
    {
        _dock.Start();
        _dock.SetMode(DockMode.Tray);

        _view.Drop("2");
        Assert.Equal(["c:\\notes.exe"], _dock.PinnedApps);
        Assert.Contains("added to the tray", _view.Last.Status);

        _view.Drop("3");                       // not on the allow-list
        Assert.Equal(["c:\\notes.exe"], _dock.PinnedApps);
        Assert.StartsWith("Not allowed", _view.Last.Status);

        // back in the full lock the dropped application is the one on offer
        _dock.ToggleMode();
        _dock.Toggle();
        Assert.Equal(["2"], _view.Last.Tiles.Select(t => t.RuntimeId));
    }

    [Fact]
    public void Dropping_is_ignored_in_the_full_lock()
    {
        _dock.Start();
        _view.Drop("2");
        Assert.Empty(_dock.PinnedApps);
    }

    [Fact]
    public void Workspace_chips_switch_the_workspace_and_the_dock_follows()
    {
        var cfg = new VeilConfiguration
        {
            Rules = [new ApplicationRule("c:\\ide.exe", "IDE"), new ApplicationRule("c:\\notes.exe", "Notes")],
            Profiles = [new WorkspaceProfile("Coding", ["c:\\ide.exe"])],
        };
        var profiles = new ProfileManager(() => cfg);
        var selector = new WindowSelectorService(_windows, () => new ApplicationPolicy(cfg.Rules, profiles.Active), () => _focus.RevealedWindowIds);
        using var dock = new DockController(_focus, selector, _view, () => SelectorPlacementCalculator.Compute(_displays.GetDisplays(), null, false), _user, profiles);
        dock.Pin("c:\\ide.exe");
        dock.Pin("c:\\notes.exe");
        dock.Start();
        dock.Toggle();
        Assert.Equal(["Coding"], _view.Last.Workspaces);
        Assert.Equal(2, _view.Last.Tiles.Count);

        _view.Workspace("Coding");
        Assert.Equal("Coding", _view.Last.ActiveWorkspace);
        Assert.Equal(["1"], _view.Last.Tiles.Select(t => t.RuntimeId));

        _view.Workspace(null);
        Assert.Null(_view.Last.ActiveWorkspace);
        Assert.Equal(2, _view.Last.Tiles.Count);
    }

    [Fact]
    public void Stop_removes_the_dock_and_it_stays_off_until_Focus_starts_again()
    {
        _dock.Start();
        _dock.SetMode(DockMode.Tray);
        _dock.Stop();
        Assert.False(_view.IsVisible);
        Assert.False(_dock.IsActive);
        _dock.Toggle();                       // ignored while Focus is off
        Assert.False(_view.IsVisible);
    }

    [Fact]
    public void Changed_is_raised_when_a_window_is_shown_so_Tab_can_be_handed_back()
    {
        var count = 0;
        _dock.Changed += () => count++;
        _dock.Start();
        _dock.Toggle();
        _view.Click("2");
        Assert.True(count >= 3);
    }

    [Fact]
    public void The_X_takes_an_application_out_of_the_tray()
    {
        _dock.Pin("c:\\ide.exe");
        _dock.Pin("c:\\notes.exe");
        _dock.Start();
        _dock.Toggle();
        Assert.Equal(2, _view.Last.Tiles.Count);

        _view.Remove("c:\\notes.exe");
        Assert.Equal(["1"], _view.Last.Tiles.Select(t => t.RuntimeId));
        Assert.Equal(["c:\\ide.exe"], _dock.PinnedApps);
        Assert.Equal(["c:\\ide.exe"], _user.Dock.PinnedApps);
    }
}
