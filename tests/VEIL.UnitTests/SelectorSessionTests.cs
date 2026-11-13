using VEIL.Application;
using VEIL.Domain;

namespace VEIL.UnitTests;

public class SelectorSessionTests : IDisposable
{
    private readonly FakeDisplays _displays = new(1);
    private readonly FakeOverlayHost _host = new();
    private readonly FakeWindows _windows = new();
    private readonly FocusStateMachine _machine = new();
    private readonly FocusService _focus;
    private readonly RecoverySupervisor _recovery;
    private readonly SelectorSession _session;

    public SelectorSessionTests()
    {
        // Order produced by the selector: allowed (ide 1, ide 2) then not allowed (game 3).
        _windows.Windows.Add(FakeWindows.Win("1", "c:\\ide.exe", "Main"));
        _windows.Windows.Add(FakeWindows.Win("2", "c:\\ide.exe", "Docs"));
        _windows.Windows.Add(FakeWindows.Win("3", "c:\\game.exe", "Game"));
        var overlays = new OverlayManager(_displays, _host);
        _recovery = new RecoverySupervisor(overlays.Teardown);
        ApplicationPolicy Policy() => new([new ApplicationRule("c:\\ide.exe", "IDE")]);
        _focus = new FocusService(_machine, _displays, _windows, overlays, _recovery, Policy);
        _session = new SelectorSession(_focus, new WindowSelectorService(_windows, Policy, () => _focus.RevealedWindowIds));
    }

    public void Dispose() { _focus.Dispose(); _recovery.Dispose(); }

    [Fact]
    public void Cannot_open_outside_focus_mode()
    {
        Assert.False(_session.Open().Success);
        Assert.False(_session.IsOpen);
    }

    [Fact]
    public void Open_selects_the_first_entry_and_enters_SelectorOpen()
    {
        _focus.FocusOn();
        Assert.True(_session.Open().Success);
        Assert.True(_session.IsOpen);
        Assert.Equal(FocusState.SelectorOpen, _focus.State);
        Assert.Equal(0, _session.SelectedIndex);
        Assert.Equal(["1", "2", "3"], _session.Entries.Select(e => e.Window.RuntimeId));
    }

    [Fact]
    public void Up_and_Down_move_and_wrap()
    {
        _focus.FocusOn(); _session.Open();
        _session.Move(1); Assert.Equal(1, _session.SelectedIndex);
        _session.Move(1); _session.Move(1); Assert.Equal(0, _session.SelectedIndex); // wrapped past the end
        _session.Move(-1); Assert.Equal(2, _session.SelectedIndex);                  // wrapped before the start
    }

    [Fact]
    public void Enter_reveals_the_selected_window_and_closes()
    {
        _focus.FocusOn(); _session.Open();
        _session.Move(1);
        var r = _session.Confirm();
        Assert.True(r.Success);
        Assert.Contains("2", _windows.Raised);
        Assert.False(_session.IsOpen);
        Assert.Equal(FocusState.Focused, _focus.State);
    }

    [Fact] // AT-006: policy message shown, window stays hidden, selector stays open
    public void Enter_on_a_disallowed_window_is_refused_and_keeps_the_selector_open()
    {
        _focus.FocusOn(); _session.Open();
        _session.Move(2);
        var r = _session.Confirm();
        Assert.False(r.Success);
        Assert.Contains("allow-list", r.Message);
        Assert.Empty(_windows.Raised);
        Assert.True(_session.IsOpen);
    }

    [Fact]
    public void Number_keys_reveal_that_entry_directly()
    {
        _focus.FocusOn(); _session.Open();
        Assert.True(_session.PressNumber(2).Success);
        Assert.Contains("2", _windows.Raised);
        Assert.False(_session.IsOpen);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(10)]
    public void Out_of_range_numbers_do_nothing_and_leave_the_selector_open(int n)
    {
        _focus.FocusOn(); _session.Open();
        Assert.False(_session.PressNumber(n).Success);
        Assert.Empty(_windows.Raised);
        Assert.True(_session.IsOpen);
    }

    [Fact]
    public void Number_of_a_disallowed_entry_is_refused()
    {
        _focus.FocusOn(); _session.Open();
        Assert.False(_session.PressNumber(3).Success);
        Assert.Empty(_windows.Raised);
        Assert.True(_session.IsOpen);
    }

    [Fact]
    public void Esc_closes_without_revealing_and_returns_to_Focused()
    {
        _focus.FocusOn(); _session.Open();
        _session.Close();
        Assert.False(_session.IsOpen);
        Assert.Equal(FocusState.Focused, _focus.State);
        Assert.Empty(_windows.Raised);
    }

    [Fact]
    public void Persistent_mode_stays_open_after_a_reveal()
    {
        _focus.FocusOn();
        _session.Persistent = true;
        _session.Open();
        Assert.True(_session.Confirm().Success);
        Assert.True(_session.IsOpen);
    }

    [Fact]
    public void Empty_state_is_open_but_nothing_can_be_selected()
    {
        _windows.Windows.Clear();
        _focus.FocusOn();
        var r = _session.Open();
        Assert.Contains("No eligible", r.Message);
        Assert.Empty(_session.Entries);
        Assert.False(_session.Confirm().Success);
        _session.Move(1); // must not throw
        Assert.False(_session.PressNumber(1).Success);
    }

    [Fact]
    public void Keys_are_ignored_when_the_selector_is_closed()
    {
        _focus.FocusOn();
        Assert.False(_session.Confirm().Success);
        Assert.False(_session.PressNumber(1).Success);
        Assert.Empty(_windows.Raised);
    }

    [Fact] // the window can vanish while the selector is open (FR-WIN-007)
    public void A_window_that_closed_while_open_is_reported_not_thrown()
    {
        _focus.FocusOn(); _session.Open();
        _windows.Windows.RemoveAll(w => w.RuntimeId == "1");
        var r = _session.Confirm();
        Assert.False(r.Success);
        Assert.Contains("no longer", r.Message);
        Assert.True(_session.IsOpen);
    }

    [Fact]
    public void Emergency_recovery_from_an_open_selector_ends_everything()
    {
        _focus.FocusOn(); _session.Open();
        _focus.EmergencyRecover();
        _session.ForceClosed();
        Assert.Equal(FocusState.Inactive, _focus.State);
        Assert.Equal(0, _host.Live);
        Assert.False(_session.IsOpen);
    }

    private void Type(string text) { foreach (var c in text) _session.TypeChar(c); }

    [Fact]
    public void Typing_in_the_list_jumps_to_the_filter_and_narrows_the_entries()
    {
        _focus.FocusOn(); _session.Open();
        Type("do");
        Assert.Equal(SelectorArea.Filter, _session.Area);
        Assert.Equal("do", _session.Filter);
        Assert.Equal(["2"], _session.Entries.Select(e => e.Window.RuntimeId));
    }

    [Fact]
    public void Filter_matches_application_name_and_ignores_case()
    {
        _focus.FocusOn(); _session.Open();
        Type("IDE");
        Assert.Equal(["1", "2"], _session.Entries.Select(e => e.Window.RuntimeId));
    }

    [Fact]
    public void Backspace_widens_the_filter_again_and_is_a_noop_when_empty()
    {
        _focus.FocusOn(); _session.Open();
        Assert.False(_session.Backspace().Success); // list focus, nothing typed
        Type("dox");
        Assert.Empty(_session.Entries);
        _session.Backspace();
        Assert.Equal("do", _session.Filter);
        Assert.Single(_session.Entries);
    }

    [Fact]
    public void Digits_are_text_in_the_filter_but_shortcuts_in_the_list()
    {
        _focus.FocusOn(); _session.Open();
        _session.Tab(reverse: true);                 // List -> Filter
        Assert.Equal(SelectorArea.Filter, _session.Area);
        _session.PressNumber(2);
        Assert.Equal("2", _session.Filter);
        Assert.Empty(_windows.Raised);               // nothing was revealed
        _session.Backspace();
        _session.Tab();                              // Filter -> List
        _session.PressNumber(2);
        Assert.Contains("2", _windows.Raised);
    }

    [Fact]
    public void Tab_cycles_filter_list_toggle_and_wraps_in_both_directions()
    {
        _focus.FocusOn(); _session.Open();           // starts on the list
        Assert.Equal(SelectorArea.OnlyAllowedToggle, _session.Tab());
        Assert.Equal(SelectorArea.Filter, _session.Tab());
        Assert.Equal(SelectorArea.List, _session.Tab());
        Assert.Equal(SelectorArea.Filter, _session.Tab(reverse: true));
        Assert.Equal(SelectorArea.OnlyAllowedToggle, _session.Tab(reverse: true));
    }

    [Fact]
    public void Enter_in_the_filter_moves_to_the_list_and_does_not_reveal()
    {
        _focus.FocusOn(); _session.Open();
        Type("id");
        Assert.True(_session.Confirm().Success);
        Assert.Equal(SelectorArea.List, _session.Area);
        Assert.Empty(_windows.Raised);
        Assert.True(_session.IsOpen);
    }

    [Fact]
    public void Only_allowed_toggle_hides_blocked_windows_and_Enter_on_it_toggles()
    {
        _focus.FocusOn(); _session.Open();
        Assert.Equal(3, _session.Entries.Count);
        _session.Tab();                              // List -> toggle
        _session.Confirm();
        Assert.True(_session.OnlyAllowed);
        Assert.Equal(["1", "2"], _session.Entries.Select(e => e.Window.RuntimeId));
        _session.Confirm();
        Assert.False(_session.OnlyAllowed);
        Assert.Equal(3, _session.Entries.Count);
    }

    [Fact]
    public void Filter_with_no_match_shows_empty_state_and_Enter_reveals_nothing()
    {
        _focus.FocusOn(); _session.Open();
        Type("zzz");
        _session.Confirm();                          // filter -> list
        Assert.Empty(_session.Entries);
        Assert.False(_session.Confirm().Success);
        Assert.Empty(_windows.Raised);
    }

    [Fact]
    public void Selection_follows_the_same_window_when_the_filter_changes()
    {
        _focus.FocusOn(); _session.Open();
        _session.Move(1);                            // "2"
        Type("i");                                   // matches ide windows 1 and 2 (and "Main"/"Docs" no longer matter)
        Assert.Equal("2", _session.Selected!.Window.RuntimeId);
    }

    [Fact]
    public void Control_characters_and_overlong_filters_are_rejected()
    {
        _focus.FocusOn(); _session.Open();
        Assert.False(_session.TypeChar((char)10).Success);
        for (var i = 0; i < 80; i++) _session.TypeChar('a');
        Assert.Equal(64, _session.Filter.Length);
    }

    [Fact]
    public void Filter_state_resets_on_each_open()
    {
        _focus.FocusOn(); _session.Open();
        Type("do");
        _session.Close();
        _session.Open();
        Assert.Equal(string.Empty, _session.Filter);
        Assert.Equal(SelectorArea.List, _session.Area);
        Assert.Equal(3, _session.Entries.Count);
    }
}
