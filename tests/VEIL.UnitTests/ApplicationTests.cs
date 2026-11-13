using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.UnitTests;

public class OverlayManagerTests
{
    [Theory] // AT-001, AT-002, FR-DISP-002
    [InlineData(1)]
    [InlineData(3)]
    public void Creates_one_overlay_per_display(int count)
    {
        var host = new FakeOverlayHost();
        var m = new OverlayManager(new FakeDisplays(count), host);
        m.Show();
        Assert.Equal(count, m.Count);
        Assert.Equal(count, host.Live);
    }

    [Fact]
    public void Partial_creation_failure_destroys_everything_already_created()
    {
        var host = new FakeOverlayHost { FailOnDisplay = "D2" };
        var m = new OverlayManager(new FakeDisplays(3), host);
        Assert.Throws<InvalidOperationException>(m.Show);
        Assert.Equal(0, host.Live);
        Assert.Equal(0, m.Count);
    }

    [Fact]
    public void No_displays_is_an_error_and_creates_nothing()
    {
        var host = new FakeOverlayHost();
        Assert.Throws<InvalidOperationException>(new OverlayManager(new FakeDisplays(0), host).Show);
        Assert.Empty(host.Created);
    }

    [Fact] // AT-003
    public void Added_display_gets_an_overlay_on_reconcile()
    {
        var d = new FakeDisplays(1);
        var host = new FakeOverlayHost();
        var m = new OverlayManager(d, host);
        m.Show();
        d.Current.Add(FakeDisplays.Make(1));
        m.Reconcile();
        Assert.Equal(2, m.Count);
    }

    [Fact] // AT-004
    public void Removed_display_releases_its_overlay()
    {
        var d = new FakeDisplays(2);
        var host = new FakeOverlayHost();
        var m = new OverlayManager(d, host);
        m.Show();
        d.Current.RemoveAt(1);
        m.Reconcile();
        Assert.Equal(1, m.Count);
        Assert.True(host.Created.Single(s => s.DisplayId == "D1").Disposed);
    }

    [Fact]
    public void Failed_new_display_does_not_disturb_existing_overlays()
    {
        var d = new FakeDisplays(1);
        var host = new FakeOverlayHost { FailOnDisplay = "D1" };
        var m = new OverlayManager(d, host);
        m.Show();
        d.Current.Add(FakeDisplays.Make(1));
        m.Reconcile();
        Assert.Equal(1, m.Count);
        Assert.Equal(1, host.Live);
    }

    [Fact] // NFR-REL-002, NFR-REL-003
    public void Teardown_is_idempotent_and_survives_a_failing_overlay()
    {
        var host = new FakeOverlayHost();
        var m = new OverlayManager(new FakeDisplays(3), host);
        m.Show();
        host.Created[0].ThrowOnDispose = true;
        m.Teardown();
        m.Teardown();
        Assert.Equal(0, host.Live);
        Assert.All(host.Created, s => Assert.Equal(1, s.DisposeCalls));
    }
}

public class FocusServiceTests
{
    private sealed class Rig : IDisposable
    {
        public FakeDisplays Displays;
        public FakeOverlayHost Host = new();
        public FakeWindows Windows = new();
        public FocusStateMachine Machine = new();
        public OverlayManager Overlays;
        public RecoverySupervisor Recovery;
        public FocusService Focus;

        public Rig(int displays = 1, params ApplicationRule[] rules)
        {
            Displays = new FakeDisplays(displays);
            Overlays = new OverlayManager(Displays, Host);
            Recovery = new RecoverySupervisor(Overlays.Teardown);
            Focus = new FocusService(Machine, Displays, Windows, Overlays, Recovery, () => new ApplicationPolicy(rules));
        }

        public void Dispose() { Focus.Dispose(); Recovery.Dispose(); }
    }

    [Fact]
    public void Focus_on_arms_recovery_before_overlays_exist()
    {
        using var r = new Rig();
        Assert.False(r.Recovery.IsArmed);
        Assert.True(r.Focus.FocusOn().Success);
        Assert.True(r.Recovery.IsArmed);
        Assert.Equal(FocusState.Focused, r.Focus.State);
        Assert.Equal(1, r.Host.Live);
    }

    [Fact]
    public void Focus_on_twice_is_harmless()
    {
        using var r = new Rig(2);
        r.Focus.FocusOn();
        Assert.True(r.Focus.FocusOn().Success);
        Assert.Equal(2, r.Host.Live);
    }

    [Fact]
    public void Failed_start_leaves_no_overlay_and_enters_ErrorSafe()
    {
        using var r = new Rig(3);
        r.Host.FailOnDisplay = "D2";
        var res = r.Focus.FocusOn();
        Assert.False(res.Success);
        Assert.Equal(0, r.Host.Live);
        Assert.Equal(FocusState.ErrorSafe, r.Focus.State);
        Assert.NotNull(r.Focus.LastError);
    }

    [Fact]
    public void Can_retry_after_ErrorSafe()
    {
        using var r = new Rig(2);
        r.Host.FailOnDisplay = "D1";
        r.Focus.FocusOn();
        r.Host.FailOnDisplay = null;
        Assert.True(r.Focus.FocusOn().Success);
        Assert.Equal(2, r.Host.Live);
    }

    [Fact]
    public void Focus_off_removes_overlays_and_returns_to_inactive()
    {
        using var r = new Rig(2);
        r.Focus.FocusOn();
        Assert.True(r.Focus.FocusOff().Success);
        Assert.Equal(0, r.Host.Live);
        Assert.Equal(FocusState.Inactive, r.Focus.State);
    }

    [Fact] // FR-HK-003
    public void Emergency_recovery_works_from_every_state_and_is_repeatable()
    {
        using var r = new Rig(2);
        r.Focus.FocusOn();
        r.Focus.OpenSelector();
        r.Focus.EmergencyRecover();
        r.Focus.EmergencyRecover();
        Assert.Equal(0, r.Host.Live);
        Assert.Equal(FocusState.Inactive, r.Focus.State);
    }

    [Fact]
    public void Emergency_recovery_clears_ErrorSafe()
    {
        using var r = new Rig(2);
        r.Host.FailOnDisplay = "D1";
        r.Focus.FocusOn();
        r.Focus.EmergencyRecover();
        Assert.Equal(FocusState.Inactive, r.Focus.State);
    }

    [Fact] // AT-005
    public void Allowed_window_is_revealed()
    {
        using var r = new Rig(1, new ApplicationRule("c:\\ide.exe", "IDE"));
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe", "main"));
        r.Focus.FocusOn();
        Assert.True(r.Focus.Reveal("A").Success);
        Assert.Contains("A", r.Windows.Raised);
        Assert.Contains("A", r.Focus.RevealedWindowIds);
    }

    [Fact] // AT-006
    public void Disallowed_window_stays_hidden_with_a_policy_message()
    {
        using var r = new Rig(1, new ApplicationRule("c:\\ide.exe", "IDE"));
        r.Windows.Windows.Add(FakeWindows.Win("B", "c:\\game.exe"));
        r.Focus.FocusOn();
        var res = r.Focus.Reveal("B");
        Assert.False(res.Success);
        Assert.Contains("allow-list", res.Message);
        Assert.DoesNotContain("B", r.Windows.Raised);
    }

    [Fact]
    public void Reveal_requires_focus_mode()
    {
        using var r = new Rig(1, new ApplicationRule("c:\\ide.exe", "IDE"));
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        Assert.False(r.Focus.Reveal("A").Success);
    }

    [Fact]
    public void Stale_window_is_reported_not_thrown()
    {
        using var r = new Rig(1, new ApplicationRule("c:\\ide.exe", "IDE"));
        r.Focus.FocusOn();
        Assert.False(r.Focus.Reveal("GONE").Success);
    }

    [Fact] // architecture 11: activation failure keeps overlay visible
    public void Reveal_failure_keeps_the_overlay()
    {
        using var r = new Rig(1, new ApplicationRule("c:\\ide.exe", "IDE"));
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        r.Windows.RevealFails = true;
        r.Focus.FocusOn();
        Assert.False(r.Focus.Reveal("A").Success);
        Assert.Equal(1, r.Host.Live);
        Assert.Empty(r.Focus.RevealedWindowIds);
    }

    [Fact] // AT-007, FR-APP-006
    public void Hide_all_demotes_windows_without_closing_overlays_or_apps()
    {
        using var r = new Rig(1, new ApplicationRule("c:\\ide.exe", "IDE"));
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        r.Focus.FocusOn();
        r.Focus.Reveal("A");
        r.Focus.HideAll();
        Assert.Empty(r.Windows.Raised);
        Assert.Empty(r.Focus.RevealedWindowIds);
        Assert.Single(r.Windows.Windows);
        Assert.Equal(1, r.Host.Live);
    }

    [Fact]
    public void Hide_of_a_vanished_window_reports_and_forgets_it()
    {
        using var r = new Rig(1, new ApplicationRule("c:\\ide.exe", "IDE"));
        r.Windows.Windows.Add(FakeWindows.Win("A", "c:\\ide.exe"));
        r.Focus.FocusOn();
        r.Focus.Reveal("A");
        r.Windows.Windows.Clear();
        r.Windows.Raised.Clear(); // the window is gone
        var res = r.Focus.Hide("A");
        Assert.False(res.Success);
        Assert.Contains("no longer", res.Message);
        Assert.Empty(r.Focus.RevealedWindowIds);
        Assert.Equal(1, r.Host.Live);
        Assert.False(r.Focus.Hide("NEVER-EXISTED").Success);
    }

    [Fact] // AT-003 end to end through the display service event
    public void Topology_change_during_focus_adds_overlay_automatically()
    {
        using var r = new Rig(1);
        r.Focus.FocusOn();
        r.Displays.Current.Add(FakeDisplays.Make(1));
        r.Displays.Raise();
        Assert.Equal(2, r.Host.Live);
    }

    [Fact]
    public void Topology_change_while_inactive_creates_nothing()
    {
        using var r = new Rig(1);
        r.Displays.Current.Add(FakeDisplays.Make(1));
        r.Displays.Raise();
        Assert.Empty(r.Host.Created);
    }

    [Fact]
    public void Selector_is_only_available_in_focus()
    {
        using var r = new Rig();
        Assert.False(r.Focus.OpenSelector().Success);
        r.Focus.FocusOn();
        Assert.True(r.Focus.OpenSelector().Success);
        Assert.True(r.Focus.CloseSelector().Success);
    }

    [Fact] // FR-LIFE-003
    public void Dispose_destroys_overlays()
    {
        var r = new Rig(2);
        r.Focus.FocusOn();
        r.Dispose();
        Assert.Equal(0, r.Host.Live);
    }
}

public class RecoverySupervisorTests
{
    [Fact]
    public void Arm_is_idempotent_and_teardown_never_throws()
    {
        var calls = 0;
        using var s = new RecoverySupervisor(() => { calls++; throw new InvalidOperationException("x"); });
        s.Arm();
        s.Arm();
        s.EmergencyTeardown();
        s.EmergencyTeardown();
        Assert.True(s.IsArmed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Disarm_clears_state()
    {
        var s = new RecoverySupervisor(() => { });
        s.Arm();
        s.Disarm();
        Assert.False(s.IsArmed);
    }
}

public class WindowSelectorTests
{
    private static (WindowSelectorService svc, FakeWindows w) Make(HashSet<string>? revealed = null)
    {
        var w = new FakeWindows();
        w.Windows.Add(FakeWindows.Win("1", "c:\\ide.exe", "Main - Project"));
        w.Windows.Add(FakeWindows.Win("2", "c:\\game.exe", "Game"));
        w.Windows.Add(FakeWindows.Win("3", "c:\\ide.exe", "Docs", minimized: true));
        var svc = new WindowSelectorService(w, () => new ApplicationPolicy([new ApplicationRule("c:\\ide.exe", "IDE")]), () => revealed ?? []);
        return (svc, w);
    }

    [Fact]
    public void Allowed_and_visible_are_independent_dimensions() // UI/UX principle 4
    {
        var (svc, _) = Make(["1"]);
        var e = svc.BuildEntries();
        Assert.Equal((true, WindowDisplayState.Visible), (e[0].IsAllowed, e[0].State));
        Assert.Equal((true, WindowDisplayState.Minimized), (e[1].IsAllowed, e[1].State)); // id 3, allowed entries come first
        Assert.Equal((false, WindowDisplayState.Hidden), (e[2].IsAllowed, e[2].State));
    }

    [Fact]
    public void Order_is_stable_across_enumeration_order_titles_and_reveal_state() // FR-SEL-004
    {
        var revealed = new HashSet<string>();
        var (svc, w) = Make(revealed);
        string Ids() => string.Join(",", svc.BuildEntries().Select(e => e.Window.RuntimeId));
        var baseline = Ids();
        Assert.Equal("1,3,2", baseline); // allowed first, then application, then runtime id

        w.Windows.Reverse();                       // OS enumeration (z-order) changes
        Assert.Equal(baseline, Ids());

        revealed.Add("3");                         // reveal state changes
        Assert.Equal(baseline, Ids());

        var i = w.Windows.FindIndex(x => x.RuntimeId == "1");
        w.Windows[i] = w.Windows[i] with { Title = "A completely different title" }; // title changes
        Assert.Equal(baseline, Ids());
    }

    [Fact]
    public void Allowed_windows_always_precede_disallowed_ones_and_group_by_application()
    {
        var w = new FakeWindows();
        w.Windows.Add(FakeWindows.Win("1", "c:\\zeta.exe"));
        w.Windows.Add(FakeWindows.Win("2", "c:\\alpha.exe"));
        w.Windows.Add(FakeWindows.Win("3", "c:\\zeta.exe"));
        w.Windows.Add(FakeWindows.Win("4", "c:\\blocked.exe"));
        var svc = new WindowSelectorService(w, () => new ApplicationPolicy([new ApplicationRule("c:\\zeta.exe", "z"), new ApplicationRule("c:\\alpha.exe", "a")]), () => []);
        Assert.Equal(["2", "1", "3", "4"], svc.BuildEntries().Select(e => e.Window.RuntimeId));
    }

    [Fact]
    public void Entries_are_numbered_from_one_and_filterable()
    {
        var (svc, _) = Make();
        Assert.Equal([1, 2, 3], svc.BuildEntries().Select(e => e.Number));
        Assert.Single(svc.BuildEntries("docs"));
        Assert.Equal(2, svc.BuildEntries("ide").Count); // matches by application name too
        Assert.Equal(2, svc.BuildEntries(onlyAllowed: true).Count);
    }

    [Fact]
    public void Empty_state_is_an_empty_list()
    {
        var (svc, w) = Make();
        w.Windows.Clear();
        Assert.Empty(svc.BuildEntries());
    }

    [Fact]
    public void Numeric_shortcuts_cover_the_first_nine_entries_only()
    {
        var (svc, _) = Make();
        var e = svc.BuildEntries();
        Assert.Equal("1", WindowSelectorService.GetByNumber(e, 1)!.Window.RuntimeId);
        Assert.Null(WindowSelectorService.GetByNumber(e, 4));
        Assert.Null(WindowSelectorService.GetByNumber(e, 0));
        Assert.Null(WindowSelectorService.GetByNumber(e, 10));
    }

    [Fact]
    public void Cycle_only_visits_allowed_windows_and_wraps()
    {
        var (svc, _) = Make();
        var e = svc.BuildEntries();
        Assert.Equal("1", WindowSelectorService.Cycle(e, null, 1)!.Window.RuntimeId);
        Assert.Equal("3", WindowSelectorService.Cycle(e, "1", 1)!.Window.RuntimeId);
        Assert.Equal("1", WindowSelectorService.Cycle(e, "3", 1)!.Window.RuntimeId);
        Assert.Equal("3", WindowSelectorService.Cycle(e, "1", -1)!.Window.RuntimeId);
        Assert.Equal("3", WindowSelectorService.Cycle(e, null, -1)!.Window.RuntimeId);
    }

    [Fact]
    public void Cycle_with_nothing_allowed_returns_null() =>
        Assert.Null(WindowSelectorService.Cycle([], null, 1));
}

public class AdminServiceTests
{
    private static readonly SuperAdminIdentity Admin = new("Windows", "S-1-5-21-100", "alice");
    private static readonly SuperAdminIdentity Other = new("Windows", "S-1-5-21-200", "bob");

    private static (AdminService svc, MemoryStore store, MemoryAudit audit) Make(SuperAdminIdentity who, bool osAdmin, SuperAdminIdentity? provisioned = null,
        AuthenticationStatus status = AuthenticationStatus.Verified)
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = provisioned } };
        var audit = new MemoryAudit();
        return (new AdminService(new FakeAuth(who, osAdmin, status), store, audit), store, audit);
    }

    [Fact] // AT-010
    public void Designated_super_admin_is_granted()
    {
        var (svc, _, _) = Make(Admin, false, Admin);
        Assert.True(svc.Authorize().IsGranted);
    }

    [Fact] // AT-009
    public void Other_account_is_denied()
    {
        var (svc, _, _) = Make(Other, false, Admin);
        Assert.Equal(AdminDecision.DeniedWrongIdentity, svc.Authorize().Decision);
    }

    [Fact]
    public void Same_name_different_principal_is_denied()
    {
        var (svc, _, _) = Make(new SuperAdminIdentity("Windows", "S-1-5-21-999", "alice"), false, Admin);
        Assert.False(svc.Authorize().IsGranted);
    }

    [Fact]
    public void Unprovisioned_machine_denies_admin_access() =>
        Assert.Equal(AdminDecision.DeniedNotProvisioned, Make(Admin, true).svc.Authorize().Decision);

    [Fact]
    public void Untrustworthy_authentication_denies_rather_than_falling_back()
    {
        var (svc, _, _) = Make(Admin, true, Admin, AuthenticationStatus.Unavailable);
        Assert.Equal(AdminDecision.DeniedAuthUnavailable, svc.Authorize().Decision);
    }

    [Fact]
    public void Provisioning_needs_os_administrator_authority()
    {
        var (svc, store, _) = Make(Admin, osAdmin: false);
        Assert.False(svc.Provision(Admin).Success);
        Assert.Null(store.Config.SuperAdmin);
    }

    [Fact]
    public void Provisioning_records_exactly_one_identity_and_refuses_a_second()
    {
        var (svc, store, _) = Make(Admin, osAdmin: true);
        Assert.True(svc.Provision(Admin).Success);
        Assert.False(svc.Provision(Other).Success);
        Assert.Equal(Admin.Principal, store.Config.SuperAdmin!.Principal);
    }

    [Fact]
    public void Reassignment_needs_os_admin_and_replaces_the_identity()
    {
        var (denied, s1, _) = Make(Other, osAdmin: false, Admin);
        Assert.False(denied.Reassign(Other).Success);
        Assert.Equal(Admin.Principal, s1.Config.SuperAdmin!.Principal);

        var (ok, s2, _) = Make(Other, osAdmin: true, Admin);
        Assert.True(ok.Reassign(Other).Success);
        Assert.Equal(Other.Principal, s2.Config.SuperAdmin!.Principal);
    }

    [Fact]
    public void Non_admin_cannot_change_allow_list()
    {
        var (svc, store, _) = Make(Other, false, Admin);
        Assert.False(svc.UpdateAllowList([new ApplicationRule("c:\\x.exe", "x")]).Success);
        Assert.Empty(store.Config.Rules);
    }

    [Fact]
    public void Super_admin_can_change_allow_list()
    {
        var (svc, store, _) = Make(Admin, false, Admin);
        Assert.True(svc.UpdateAllowList([new ApplicationRule("c:\\x.exe", "x")]).Success);
        Assert.Single(store.Config.Rules);
    }

    [Fact] // AT-011: conflicting set rejected, existing bindings intact
    public void Conflicting_hotkeys_are_rejected_and_previous_bindings_remain()
    {
        var (svc, store, _) = Make(Admin, false, Admin);
        var before = store.Config.Hotkeys;
        var bad = DefaultHotkeys.Create().Select(b => b.Command == HotkeyCommand.HideAll ? b with { Chord = "Ctrl+Alt+V" } : b).ToList();
        var res = svc.UpdateHotkeys(bad);
        Assert.False(res.Success);
        Assert.Contains("Ctrl+Alt+V", res.Message);
        Assert.Same(before, store.Config.Hotkeys);
    }

    [Fact]
    public void Every_decision_is_audited_without_secrets()
    {
        var (svc, _, audit) = Make(Other, false, Admin);
        svc.Authorize();
        svc.UpdateAllowList([]);
        Assert.NotEmpty(audit.Events);
        Assert.All(audit.Events, e =>
        {
            Assert.Equal(Other.Principal, e.Identity);
            Assert.DoesNotContain("password", e.Action + e.Result, StringComparison.OrdinalIgnoreCase);
        });
    }
}

public class AdminServiceWriteFailureTests
{
    private sealed class ThrowingStore(Exception error) : IConfigurationStore
    {
        public VeilConfiguration Config { get; set; } = new();
        public bool Throw { get; set; } = true;
        public VeilConfiguration Load() => Config;
        public void Save(VeilConfiguration configuration) { if (Throw) throw error; Config = configuration; }
    }

    private static readonly SuperAdminIdentity Admin = new("Windows", "S-1-5-21-100", "alice");

    private static (AdminService svc, ThrowingStore store, MemoryAudit audit) Make(Exception error)
    {
        var store = new ThrowingStore(error) { Config = new VeilConfiguration { SuperAdmin = Admin } };
        var audit = new MemoryAudit();
        return (new AdminService(new FakeAuth(Admin, true), store, audit), store, audit);
    }

    [Fact]
    public void A_protected_folder_the_user_cannot_write_is_a_clear_refusal_not_an_exception()
    {
        var (svc, store, audit) = Make(new UnauthorizedAccessException("denied"));
        var result = svc.UpdateAllowList([new ApplicationRule("c:\\a.exe", "a")]);
        Assert.False(result.Success);
        Assert.Contains("administrator rights", result.Message);
        Assert.Empty(store.Config.Rules);
        Assert.Contains(audit.Events, e => e.Action == "policy.update" && e.Result == "Denied");
    }

    [Fact]
    public void Hotkey_saves_fail_the_same_way()
    {
        var (svc, _, _) = Make(new UnauthorizedAccessException("denied"));
        var result = svc.UpdateHotkeys(DefaultHotkeys.Create().ToList());
        Assert.False(result.Success);
        Assert.Contains("Nothing was saved", result.Message);
    }

    [Fact]
    public void Other_io_failures_report_the_reason()
    {
        var (svc, _, _) = Make(new IOException("disk full"));
        var result = svc.UpdateAllowList([]);
        Assert.False(result.Success);
        Assert.Contains("disk full", result.Message);
    }

    [Fact]
    public void Provisioning_and_reassignment_also_refuse_instead_of_throwing()
    {
        var store = new ThrowingStore(new UnauthorizedAccessException()) { Config = new VeilConfiguration() };
        var svc = new AdminService(new FakeAuth(Admin, true), store, new MemoryAudit());
        Assert.False(svc.Provision(Admin).Success);
        Assert.False(svc.Reassign(Admin).Success);
    }

    [Fact]
    public void Once_the_folder_is_writable_the_same_save_succeeds()
    {
        var (svc, store, _) = Make(new UnauthorizedAccessException());
        Assert.False(svc.UpdateAllowList([new ApplicationRule("c:\\a.exe", "a")]).Success);
        store.Throw = false;
        Assert.True(svc.UpdateAllowList([new ApplicationRule("c:\\a.exe", "a")]).Success);
        Assert.Single(store.Config.Rules);
    }
}
