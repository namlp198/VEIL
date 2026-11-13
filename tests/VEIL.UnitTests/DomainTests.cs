using VEIL.Domain;

namespace VEIL.UnitTests;

public class FocusStateMachineTests
{
    [Fact]
    public void Happy_path_follows_SRS_model()
    {
        var m = new FocusStateMachine();
        m.Transition(FocusState.Preparing);
        m.Transition(FocusState.Focused);
        m.Transition(FocusState.SelectorOpen);
        m.Transition(FocusState.Focused);
        m.Transition(FocusState.Inactive);
        Assert.Equal(FocusState.Inactive, m.State);
    }

    [Theory]
    [InlineData(FocusState.Inactive, FocusState.Focused)]
    [InlineData(FocusState.Inactive, FocusState.SelectorOpen)]
    [InlineData(FocusState.Preparing, FocusState.Inactive)]
    [InlineData(FocusState.Recovering, FocusState.Focused)]
    [InlineData(FocusState.ErrorSafe, FocusState.Focused)]
    public void Illegal_transitions_are_rejected(FocusState from, FocusState to) =>
        Assert.False(FocusStateMachine.IsAllowed(from, to));

    [Theory]
    [InlineData(FocusState.Inactive)]
    [InlineData(FocusState.Preparing)]
    [InlineData(FocusState.Focused)]
    [InlineData(FocusState.SelectorOpen)]
    [InlineData(FocusState.ErrorSafe)]
    public void Any_runtime_state_may_enter_Recovering(FocusState from) =>
        Assert.True(FocusStateMachine.IsAllowed(from, FocusState.Recovering));

    [Fact]
    public void Transition_throws_and_TryTransition_does_not_on_illegal_move()
    {
        var m = new FocusStateMachine();
        Assert.False(m.TryTransition(FocusState.Focused));
        Assert.Throws<InvalidOperationException>(() => m.Transition(FocusState.Focused));
        Assert.Equal(FocusState.Inactive, m.State);
    }

    [Fact]
    public void Raises_StateChanged()
    {
        var m = new FocusStateMachine();
        FocusStateChangedEventArgs? seen = null;
        m.StateChanged += (_, e) => seen = e;
        m.Transition(FocusState.Preparing);
        Assert.Equal((FocusState.Inactive, FocusState.Preparing), (seen!.Previous, seen.Current));
    }
}

public class ApplicationPolicyTests
{
    private static ApplicationPolicy Policy(params ApplicationRule[] r) => new(r);

    [Fact]
    public void Unlisted_application_is_denied_by_default() =>
        Assert.False(Policy().Evaluate(FakeWindows.Win("1", "c:\\a.exe")).IsAllowed);

    [Fact]
    public void Listed_normal_window_is_allowed_case_insensitively() =>
        Assert.True(Policy(new ApplicationRule("C:\\A.exe", "A")).Evaluate(FakeWindows.Win("1", "c:\\a.exe")).IsAllowed);

    [Fact]
    public void Disabled_rule_denies() =>
        Assert.False(Policy(new ApplicationRule("c:\\a.exe", "A", Enabled: false)).Evaluate(FakeWindows.Win("1", "c:\\a.exe")).IsAllowed);

    [Theory]
    [InlineData(WindowKind.Dialog)]
    [InlineData(WindowKind.Modal)]
    [InlineData(WindowKind.Splash)]
    [InlineData(WindowKind.Child)]
    public void Transient_windows_default_to_hidden(WindowKind kind) =>
        Assert.False(Policy(new ApplicationRule("c:\\a.exe", "A")).Evaluate(FakeWindows.Win("1", "c:\\a.exe", kind: kind)).IsAllowed);

    [Fact]
    public void Transient_windows_follow_parent_when_rule_says_so() =>
        Assert.True(Policy(new ApplicationRule("c:\\a.exe", "A", Transient: TransientWindowPolicy.FollowParent))
            .Evaluate(FakeWindows.Win("1", "c:\\a.exe", kind: WindowKind.Dialog)).IsAllowed);

    [Fact]
    public void Unknown_window_kind_is_always_hidden() =>
        Assert.False(Policy(new ApplicationRule("c:\\a.exe", "A", Transient: TransientWindowPolicy.FollowParent))
            .Evaluate(FakeWindows.Win("1", "c:\\a.exe", kind: WindowKind.Unknown)).IsAllowed);

    [Fact]
    public void Unresolvable_process_identity_can_never_match() =>
        Assert.False(Policy(new ApplicationRule("c:\\a.exe", "A")).Evaluate(FakeWindows.Win("1", "unknown:4242")).IsAllowed);
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+V", KeyModifiers.Ctrl | KeyModifiers.Alt, "V")]
    [InlineData("ctrl + alt + tab", KeyModifiers.Ctrl | KeyModifiers.Alt, "Tab")]
    [InlineData("Win+F12", KeyModifiers.Win, "F12")]
    [InlineData("Ctrl+Alt+Esc", KeyModifiers.Ctrl | KeyModifiers.Alt, "Esc")]
    public void Parses_valid_chords(string text, KeyModifiers mods, string key)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(mods, key), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("V")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Banana")]
    public void Rejects_invalid_chords(string text) => Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void Default_bindings_are_valid() => Assert.Empty(DefaultHotkeys.Validate(DefaultHotkeys.Create()));

    [Fact]
    public void Duplicate_chords_are_reported()
    {
        var set = DefaultHotkeys.Create().Select(b => b.Command == HotkeyCommand.HideAll ? b with { Chord = "Ctrl+Alt+V" } : b);
        Assert.Contains(DefaultHotkeys.Validate(set), p => p.Contains("Ctrl+Alt+V"));
    }

    [Fact]
    public void Emergency_recovery_cannot_be_removed_or_disabled()
    {
        var set = DefaultHotkeys.Create().Select(b => b.Command == HotkeyCommand.EmergencyRecovery ? b with { Enabled = false } : b);
        Assert.Contains(DefaultHotkeys.Validate(set), p => p.Contains("Emergency Recovery"));
    }

    [Fact]
    public void Chord_round_trips()
    {
        Assert.True(KeyChord.TryParse("alt+ctrl+v", out var c));
        Assert.Equal("Ctrl+Alt+V", c.ToString());
    }
}

public class SuperAdminIdentityTests
{
    [Fact]
    public void Authority_follows_principal_not_display_name() =>
        Assert.True(new SuperAdminIdentity("Windows", "S-1-5-21-1", "old").IsSamePrincipal(new("windows", "s-1-5-21-1", "renamed")));

    [Fact]
    public void Same_display_name_with_other_principal_is_a_different_identity() =>
        Assert.False(new SuperAdminIdentity("Windows", "S-1-5-21-1", "admin").IsSamePrincipal(new("Windows", "S-1-5-21-2", "admin")));
}
