using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;
using VEIL.Presentation.ViewModels;

namespace VEIL.UnitTests;

public class AdminViewModelTests
{
    private static readonly SuperAdminIdentity Alice = new("Windows", "S-1-5-21-100", "PC\\alice");
    private static readonly SuperAdminIdentity Bob = new("Windows", "S-1-5-21-200", "PC\\bob");

    private sealed class SwitchableAuth(SuperAdminIdentity current, AuthenticationStatus status = AuthenticationStatus.Verified) : IAuthenticationProvider
    {
        public SuperAdminIdentity Current { get; set; } = current;
        public SuperAdminIdentity GetCurrentIdentity() => Current;
        public bool IsOsAdministrator() => false;
        public AuthenticationResult Authenticate() => new(status, status == AuthenticationStatus.Verified ? Current : null, status.ToString());
    }

    private sealed class FakeSession : ISessionInspector
    {
        public SessionInfo Inspect() => new("Windows", "Desktop", SessionSupport.Supported, "notice");
    }

    private static (AdminShellViewModel vm, MemoryStore store, SwitchableAuth auth) Make(
        SuperAdminIdentity current, SuperAdminIdentity? superAdmin, AuthenticationStatus status = AuthenticationStatus.Verified, params ApplicationRule[] rules)
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = superAdmin, Rules = rules } };
        var auth = new SwitchableAuth(current, status);
        var admin = new AdminService(auth, store, new MemoryAudit());
        var vm = new AdminShellViewModel(admin, store, auth, new FakeSession(), () => [], "test");
        return (vm, store, auth);
    }

    private static ApplicationsPageViewModel Apps(AdminShellViewModel vm) => (ApplicationsPageViewModel)vm.CurrentPage!;

    // ---- access (AT-009 / AT-010) -----------------------------------------------------------------------------------

    [Fact]
    public void Super_admin_is_granted_and_lands_on_Applications()
    {
        var (vm, _, _) = Make(Alice, Alice);
        Assert.True(vm.IsGranted);
        Assert.False(vm.IsDenied);
        Assert.IsType<ApplicationsPageViewModel>(vm.CurrentPage);
    }

    [Fact]
    public void Another_account_is_denied_and_no_settings_page_exists()
    {
        var (vm, _, _) = Make(Bob, Alice);
        Assert.True(vm.IsDenied);
        Assert.Null(vm.CurrentPage);
        Assert.Equal("Access denied", vm.AccessTitle);
    }

    [Fact]
    public void Selecting_a_section_while_denied_does_not_create_a_page()
    {
        var (vm, _, _) = Make(Bob, Alice);
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Hotkeys");
        Assert.Null(vm.CurrentPage);
    }

    [Fact]
    public void Unprovisioned_machine_is_locked_with_its_own_explanation()
    {
        var (vm, _, _) = Make(Alice, null);
        Assert.True(vm.IsDenied);
        Assert.Equal("No Super Admin is set up", vm.AccessTitle);
    }

    [Fact]
    public void An_untrustworthy_identity_is_denied_not_granted()
    {
        var (vm, _, _) = Make(Alice, Alice, AuthenticationStatus.Unavailable);
        Assert.True(vm.IsDenied);
        Assert.Equal("Identity could not be verified", vm.AccessTitle);
    }

    [Fact]
    public void The_denied_message_never_asks_for_a_password()
    {
        var (vm, _, _) = Make(Bob, Alice);
        Assert.DoesNotContain("password", vm.AccessMessage, StringComparison.OrdinalIgnoreCase); // the retry is a command, not a credential form
        Assert.NotNull(vm.SignInCommand);
    }

    [Fact]
    public void Check_again_grants_access_once_the_session_identity_is_right()
    {
        var (vm, _, auth) = Make(Bob, Alice);
        Assert.True(vm.IsDenied);
        auth.Current = Alice;
        vm.SignInCommand.Execute(null);
        Assert.True(vm.IsGranted);
        Assert.NotNull(vm.CurrentPage);
    }

    [Fact]
    public void Identity_text_shows_references_not_secrets()
    {
        var (vm, _, _) = Make(Alice, Alice);
        Assert.Contains("alice", vm.IdentityText);
        Assert.Contains("S-1-5-21-100", vm.IdentityText);
    }

    [Fact]
    public void Every_group_in_the_specification_is_listed_and_unbuilt_ones_say_so()
    {
        var (vm, _, _) = Make(Alice, Alice);
        Assert.Equal(["General", "Displays", "Applications", "Hotkeys", "Appearance", "Workspace Profiles", "Focus Timer", "Security", "Diagnostics", "Backup & Restore"],
            vm.Sections.Select(s => s.Name));
        Assert.All(vm.Sections.Where(s => !s.IsImplemented), s => Assert.IsType<PlaceholderPageViewModel>(s.CreatePage()));
        Assert.All(vm.Sections, s => Assert.Contains(s.Scope, new[] { "Whole machine", "This user" })); // every group states its scope
    }

    // ---- allow-list editor ------------------------------------------------------------------------------------------

    [Fact]
    public void Add_marks_the_page_dirty_but_stores_nothing_until_Save()
    {
        var (vm, store, _) = Make(Alice, Alice);
        var apps = Apps(vm);
        apps.NewApplicationId = "c:\\tools\\app.exe";
        apps.AddCommand.Execute(null);
        Assert.Single(apps.Rules);
        Assert.True(apps.IsDirty);
        Assert.Empty(store.Config.Rules);
        Assert.Equal(string.Empty, apps.NewApplicationId);
    }

    [Fact]
    public void Save_persists_the_rules_and_clears_the_dirty_flag()
    {
        var (vm, store, _) = Make(Alice, Alice);
        var apps = Apps(vm);
        apps.NewApplicationId = "c:\\tools\\app.exe";
        apps.AddCommand.Execute(null);
        apps.SaveCommand.Execute(null);
        Assert.Single(store.Config.Rules);
        Assert.Equal("app", store.Config.Rules[0].DisplayName);
        Assert.False(apps.IsDirty);
        Assert.Equal(StatusKind.Success, apps.StatusKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_is_refused(string text)
    {
        var (vm, _, _) = Make(Alice, Alice);
        var apps = Apps(vm);
        apps.NewApplicationId = text;
        apps.AddCommand.Execute(null);
        Assert.Empty(apps.Rules);
        Assert.Equal(StatusKind.Error, apps.StatusKind);
    }

    [Fact]
    public void Overlong_paths_and_case_insensitive_duplicates_are_refused()
    {
        var (vm, _, _) = Make(Alice, Alice, AuthenticationStatus.Verified, new ApplicationRule("c:\\tools\\app.exe", "app"));
        var apps = Apps(vm);
        apps.NewApplicationId = "C:\\TOOLS\\APP.EXE";
        apps.AddCommand.Execute(null);
        Assert.Single(apps.Rules);
        apps.NewApplicationId = new string('x', 1025);
        apps.AddCommand.Execute(null);
        Assert.Single(apps.Rules);
        Assert.Equal(StatusKind.Error, apps.StatusKind);
    }

    [Fact]
    public void Remove_then_Discard_restores_the_saved_list()
    {
        var (vm, store, _) = Make(Alice, Alice, AuthenticationStatus.Verified, new ApplicationRule("c:\\a.exe", "a"), new ApplicationRule("c:\\b.exe", "b"));
        var apps = Apps(vm);
        apps.RemoveCommand.Execute(apps.Rules[0]);
        Assert.Single(apps.Rules);
        Assert.True(apps.IsDirty);
        apps.RevertCommand.Execute(null);
        Assert.Equal(2, apps.Rules.Count);
        Assert.False(apps.IsDirty);
        Assert.Equal(2, store.Config.Rules.Count);
    }

    [Fact]
    public void Editing_a_row_marks_the_page_dirty()
    {
        var (vm, _, _) = Make(Alice, Alice, AuthenticationStatus.Verified, new ApplicationRule("c:\\a.exe", "a"));
        var apps = Apps(vm);
        apps.Rules[0].Enabled = false;
        Assert.True(apps.IsDirty);
    }

    [Fact] // the save re-authorizes: an account switch after the window opened must not write policy
    public void Save_is_refused_if_the_os_account_changed_after_the_window_opened()
    {
        var (vm, store, auth) = Make(Alice, Alice, AuthenticationStatus.Verified, new ApplicationRule("c:\\a.exe", "a"));
        var apps = Apps(vm);
        apps.Rules[0].Enabled = false;
        auth.Current = Bob;
        apps.SaveCommand.Execute(null);
        Assert.Equal(StatusKind.Error, apps.StatusKind);
        Assert.True(store.Config.Rules[0].Enabled);
        Assert.True(apps.IsDirty); // the edit stays on screen
    }

    [Fact]
    public void Transient_policy_round_trips_through_the_row()
    {
        var (vm, store, _) = Make(Alice, Alice, AuthenticationStatus.Verified, new ApplicationRule("c:\\a.exe", "a", true, TransientWindowPolicy.Hide));
        var apps = Apps(vm);
        apps.Rules[0].FollowParent = true;
        apps.SaveCommand.Execute(null);
        Assert.Equal(TransientWindowPolicy.FollowParent, store.Config.Rules[0].Transient);
    }

    [Fact]
    public void Empty_allow_list_is_reported_so_the_view_can_explain_default_deny()
    {
        var (vm, _, _) = Make(Alice, Alice);
        Assert.True(Apps(vm).IsEmpty);
    }

    // ---- hotkey editor (AT-011) -------------------------------------------------------------------------------------

    private static HotkeysPageViewModel Hotkeys(AdminShellViewModel vm)
    {
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Hotkeys");
        return (HotkeysPageViewModel)vm.CurrentPage!;
    }

    [Fact]
    public void Hotkey_rows_cover_every_command_and_mark_emergency_recovery_required()
    {
        var (vm, _, _) = Make(Alice, Alice);
        var hk = Hotkeys(vm);
        Assert.Equal(Enum.GetValues<HotkeyCommand>().Count(c => c != HotkeyCommand.Selector), hk.Rows.Count); // the retired Selector has no row
        Assert.True(hk.Rows.Single(r => r.Command == HotkeyCommand.EmergencyRecovery).IsLocked);
        Assert.Equal(1, hk.Rows.Count(r => r.IsLocked));
    }

    [Fact]
    public void An_invalid_chord_is_flagged_while_typing()
    {
        var (vm, _, _) = Make(Alice, Alice);
        var row = Hotkeys(vm).Rows[0];
        row.Chord = "V";
        Assert.True(row.HasProblem);
        row.Chord = "Ctrl+Alt+K";
        Assert.False(row.HasProblem);
    }

    [Fact]
    public void A_conflicting_set_is_refused_with_the_chord_named_and_storage_is_untouched()
    {
        var (vm, store, _) = Make(Alice, Alice);
        var before = store.Config.Hotkeys;
        var hk = Hotkeys(vm);
        hk.Rows.Single(r => r.Command == HotkeyCommand.HideAll).Chord = "Ctrl+Alt+V";
        hk.SaveCommand.Execute(null);
        Assert.Equal(StatusKind.Error, hk.StatusKind);
        Assert.Contains("Ctrl+Alt+V", hk.StatusText);
        Assert.Same(before, store.Config.Hotkeys);
    }

    [Fact]
    public void A_valid_change_is_saved()
    {
        var (vm, store, _) = Make(Alice, Alice);
        var hk = Hotkeys(vm);
        hk.Rows.Single(r => r.Command == HotkeyCommand.HideAll).Chord = "Ctrl+Alt+J";
        hk.SaveCommand.Execute(null);
        Assert.Equal(StatusKind.Success, hk.StatusKind);
        Assert.Equal("Ctrl+Alt+J", store.Config.Hotkeys.Single(b => b.Command == HotkeyCommand.HideAll).Chord);
    }

    [Fact]
    public void Disabling_emergency_recovery_is_rejected_even_if_the_ui_were_bypassed()
    {
        var (vm, store, _) = Make(Alice, Alice);
        var hk = Hotkeys(vm);
        hk.Rows.Single(r => r.IsLocked).Enabled = false; // the view disables the box, the rule still holds
        hk.SaveCommand.Execute(null);
        Assert.Equal(StatusKind.Error, hk.StatusKind);
        Assert.True(store.Config.Hotkeys.Single(b => b.Command == HotkeyCommand.EmergencyRecovery).Enabled);
    }

    [Fact]
    public void Restore_defaults_then_Discard_returns_to_the_saved_bindings()
    {
        var (vm, store, _) = Make(Alice, Alice);
        var hk = Hotkeys(vm);
        hk.Rows[0].Chord = "Ctrl+Alt+K";
        hk.ResetToDefaultsCommand.Execute(null);
        Assert.Equal("Ctrl+Alt+V", hk.Rows.Single(r => r.Command == HotkeyCommand.FocusOn).Chord);
        hk.RevertCommand.Execute(null);
        Assert.False(hk.IsDirty);
        Assert.Equal(store.Config.Hotkeys.Select(b => b.Chord), hk.Rows.Select(r => r.Chord));
    }

    // ---- read-only pages --------------------------------------------------------------------------------------------

    [Fact]
    public void Security_page_states_the_limitation_and_shows_references_only()
    {
        var (vm, _, _) = Make(Alice, Alice);
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Security");
        var page = (SecurityPageViewModel)vm.CurrentPage!;
        Assert.Contains("not a secure lock", page.Limitation);
        Assert.Contains("OS administrator", page.Recovery);
        Assert.Equal("Yes", page.Facts.Single(f => f.Label == "This account is the Super Admin").Value);
        Assert.DoesNotContain(page.Facts, f => f.Label.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Diagnostics_page_lists_session_facts_and_displays()
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = Alice } };
        var auth = new SwitchableAuth(Alice);
        var admin = new AdminService(auth, store, new MemoryAudit());
        var vm = new AdminShellViewModel(admin, store, auth, new FakeSession(),
            () => [new DisplayInfo("D1", "DISPLAY1", new WindowBounds(0, 0, 3840, 2160), 2.0, true)], "9.9");
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Diagnostics");
        var page = (DiagnosticsPageViewModel)vm.CurrentPage!;
        Assert.Equal("9.9", page.Session.Single(f => f.Label == "VEIL version").Value);
        Assert.Single(page.Displays);
        Assert.Contains("scale 200%", page.Displays[0].Value);
        Assert.Contains("(primary)", page.Displays[0].Label);
    }
}
