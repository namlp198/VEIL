using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.UnitTests;

internal sealed class FakeSelectorView : ISelectorView
{
    public bool IsVisible { get; private set; }
    public List<SelectorViewState> States { get; } = [];
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public SelectorViewState Last => States[^1];

    public event Action<string>? FilterChanged;
    public event Action<int>? SelectionChanged;
    public event Action<bool>? OnlyAllowedChanged;
    public event Action? ConfirmRequested;
    public event Action? CancelRequested;
    public event Action<int>? NumberPressed;

    public SelectorPlacement? LastPlacement { get; private set; }
    public void Show(SelectorViewState state, SelectorPlacement placement) { IsVisible = true; ShowCount++; LastPlacement = placement; States.Add(state); }
    public void Update(SelectorViewState state) => States.Add(state);
    public void Hide() { IsVisible = false; HideCount++; }
    public void Dispose() { }

    public void Type(string text) => FilterChanged?.Invoke(text);
    public void Select(int i) => SelectionChanged?.Invoke(i);
    public void SetOnlyAllowed(bool v) => OnlyAllowedChanged?.Invoke(v);
    public void Enter() => ConfirmRequested?.Invoke();
    public void Esc() => CancelRequested?.Invoke();
    public void Digit(int n) => NumberPressed?.Invoke(n);
}

public class SelectorControllerTests : IDisposable
{
    private readonly FakeDisplays _displays = new(1);
    private readonly FakeOverlayHost _host = new();
    private readonly FakeWindows _windows = new();
    private readonly FocusStateMachine _machine = new();
    private readonly FocusService _focus;
    private readonly RecoverySupervisor _recovery;
    private readonly FakeSelectorView _view = new();
    private readonly SelectorController _controller;

    public SelectorControllerTests()
    {
        _windows.Windows.Add(FakeWindows.Win("1", "c:\\ide.exe", "Main"));
        _windows.Windows.Add(FakeWindows.Win("2", "c:\\ide.exe", "Docs"));
        _windows.Windows.Add(FakeWindows.Win("3", "c:\\game.exe", "Game"));
        var overlays = new OverlayManager(_displays, _host);
        _recovery = new RecoverySupervisor(overlays.Teardown);
        ApplicationPolicy Policy() => new([new ApplicationRule("c:\\ide.exe", "IDE")]);
        _focus = new FocusService(_machine, _displays, _windows, overlays, _recovery, Policy);
        var session = new SelectorSession(_focus, new WindowSelectorService(_windows, Policy, () => _focus.RevealedWindowIds));
        _controller = new SelectorController(session, _view, () => SelectorPlacementCalculator.Compute(_displays.GetDisplays(), null, false));
        _focus.FocusOn();
    }

    public void Dispose() { _controller.Dispose(); _focus.Dispose(); _recovery.Dispose(); }

    [Fact]
    public void Open_shows_the_view_with_all_rows_and_the_first_selected()
    {
        Assert.True(_controller.Open().Success);
        Assert.True(_view.IsVisible);
        Assert.Equal(3, _view.Last.Rows.Count);
        Assert.Equal(0, _view.Last.SelectedIndex);
        Assert.Equal(FocusState.SelectorOpen, _focus.State);
    }

    [Fact]
    public void The_placement_is_requested_on_every_open_so_a_moved_pointer_or_new_display_is_honoured()
    {
        _controller.Open();
        Assert.NotNull(_view.LastPlacement);
        Assert.Equal(1.0, _view.LastPlacement!.Scale);
        _controller.Close();
        _displays.Current[0] = new DisplayInfo("D0", "Display 0", new WindowBounds(0, 0, 3840, 2160), 2.0, true);
        _controller.Open();
        Assert.Equal(2.0, _view.LastPlacement!.Scale);
        Assert.Equal(1520, _view.LastPlacement.Bounds.Width);
    }

    [Fact]
    public void Rows_state_allowed_and_visible_in_text_not_by_colour_alone() // UI/UX 8
    {
        _controller.Open();
        Assert.Contains("Allowed", _view.Last.Rows[0].Text);
        Assert.Contains("Not allowed", _view.Last.Rows[2].Text);
        Assert.Contains("Hidden", _view.Last.Rows[0].Text);
    }

    [Fact]
    public void Open_outside_focus_does_not_show_a_view()
    {
        _focus.FocusOff();
        Assert.False(_controller.Open().Success);
        Assert.False(_view.IsVisible);
        Assert.Equal(0, _view.ShowCount);
    }

    [Fact]
    public void Typing_in_the_text_box_filters_the_list()
    {
        _controller.Open();
        _view.Type("doc");
        Assert.Single(_view.Last.Rows);
        Assert.Contains("Docs", _view.Last.Rows[0].Text);
        Assert.Equal("doc", _view.Last.Filter);
    }

    [Fact]
    public void A_filter_that_matches_nothing_gives_an_empty_row_list()
    {
        _controller.Open();
        _view.Type("zzz");
        Assert.Empty(_view.Last.Rows);
    }

    [Fact]
    public void Checkbox_toggles_only_allowed()
    {
        _controller.Open();
        _view.SetOnlyAllowed(true);
        Assert.True(_view.Last.OnlyAllowed);
        Assert.Equal(2, _view.Last.Rows.Count);
        _view.SetOnlyAllowed(false);
        Assert.Equal(3, _view.Last.Rows.Count);
    }

    [Fact]
    public void Enter_reveals_the_highlighted_window_and_hides_the_view()
    {
        _controller.Open();
        _view.Select(1);
        _view.Enter();
        Assert.Contains("2", _windows.Raised);
        Assert.False(_view.IsVisible);
        Assert.Equal(FocusState.Focused, _focus.State);
    }

    [Fact] // AT-006
    public void Enter_on_a_disallowed_window_keeps_the_view_and_shows_the_policy_message()
    {
        _controller.Open();
        _view.Select(2);
        _view.Enter();
        Assert.True(_view.IsVisible);
        Assert.Contains("allow-list", _view.Last.Status);
        Assert.Empty(_windows.Raised);
    }

    [Fact]
    public void Digit_in_the_list_reveals_that_entry()
    {
        _controller.Open();
        _view.Digit(2);
        Assert.Contains("2", _windows.Raised);
        Assert.False(_view.IsVisible);
    }

    [Fact]
    public void Digit_beyond_the_list_reports_and_keeps_the_view()
    {
        _controller.Open();
        _view.Digit(7);
        Assert.True(_view.IsVisible);
        Assert.Contains("no entry 7", _view.Last.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_windows.Raised);
    }

    [Fact]
    public void Digits_work_on_the_filtered_list_numbering()
    {
        _controller.Open();
        _view.Type("doc");     // only "Docs" remains, now entry 1
        _view.Digit(1);
        Assert.Contains("2", _windows.Raised);
    }

    [Fact]
    public void Esc_closes_without_revealing_and_returns_to_Focused()
    {
        _controller.Open();
        _view.Esc();
        Assert.False(_view.IsVisible);
        Assert.Equal(FocusState.Focused, _focus.State);
        Assert.Empty(_windows.Raised);
    }

    [Fact]
    public void Toggle_opens_then_closes()
    {
        _controller.Toggle();
        Assert.True(_view.IsVisible);
        _controller.Toggle();
        Assert.False(_view.IsVisible);
    }

    [Fact] // Emergency Recovery / Focus Off must never leave the selector window behind
    public void Focus_ending_underneath_hides_the_view()
    {
        _controller.Open();
        _focus.EmergencyRecover();
        _controller.OnFocusEnded();
        Assert.False(_view.IsVisible);
        Assert.False(_controller.IsOpen);
        Assert.Equal(0, _host.Live);
    }

    [Fact]
    public void A_window_that_closed_while_the_selector_was_open_is_reported_not_thrown()
    {
        _controller.Open();
        _windows.Windows.RemoveAll(w => w.RuntimeId == "1");
        _view.Enter();
        Assert.True(_view.IsVisible);
        Assert.Contains("no longer", _view.Last.Status);
    }

    [Fact]
    public void Events_after_close_are_ignored()
    {
        _controller.Open();
        _view.Esc();
        var before = _view.States.Count;
        _view.Type("x"); _view.Enter(); _view.Digit(1); _view.SetOnlyAllowed(true);
        Assert.Equal(before, _view.States.Count);
        Assert.Empty(_windows.Raised);
    }
}
