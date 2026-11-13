using VEIL.Application;
using VEIL.Domain;
using VEIL.Infrastructure;
using VEIL.Platform.Abstractions;
using VEIL.Presentation.ViewModels;

namespace VEIL.UnitTests;

public class FocusTimerTests
{
    private static (FocusTimer Timer, ManualTime Time, MemorySessionLog Log) Make(string? workspace = "Coding")
    {
        var time = new ManualTime();
        var log = new MemorySessionLog();
        return (new FocusTimer(log, () => workspace, time, tick: false), time, log);
    }

    [Fact]
    public void Counts_down_and_reports_progress()
    {
        var (timer, time, _) = Make();
        Assert.True(timer.Start(TimeSpan.FromMinutes(25)).Success);
        time.Advance(TimeSpan.FromMinutes(10));
        var s = timer.Snapshot();
        Assert.Equal(TimerPhase.Running, s.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), s.Remaining);
        Assert.Equal(0.4, s.Progress, 3);
    }

    [Fact]
    public void Completion_is_recorded_with_the_planned_time_and_announced_quietly()
    {
        var (timer, time, log) = Make("Research");
        FocusSessionRecord? ended = null;
        timer.SessionEnded += r => ended = r;
        timer.Start(TimeSpan.FromMinutes(25));
        time.Advance(TimeSpan.FromMinutes(25));
        timer.Tick();

        Assert.NotNull(ended);
        Assert.True(ended!.Completed);
        Assert.Equal(1500, ended.ActualSeconds);
        Assert.Equal(25, ended.PlannedMinutes);
        Assert.Equal("Research", ended.Workspace);
        Assert.Single(log.Records);

        var s = timer.Snapshot();
        Assert.Equal(TimerPhase.Completed, s.Phase);
        Assert.Contains("25 min", s.Notice);

        time.Advance(TimeSpan.FromSeconds(21));
        Assert.Equal(TimerPhase.Idle, timer.Snapshot().Phase);   // the notice fades away by itself
    }

    [Fact]
    public void Stopping_early_records_the_time_actually_spent()
    {
        var (timer, time, log) = Make();
        timer.Start(TimeSpan.FromMinutes(50));
        time.Advance(TimeSpan.FromSeconds(750));
        var result = timer.Stop();

        Assert.True(result.Success);
        var r = Assert.Single(log.Records);
        Assert.False(r.Completed);
        Assert.Equal(750, r.ActualSeconds);
        Assert.Equal(TimerPhase.Idle, timer.Snapshot().Phase);
    }

    [Fact]
    public void Stop_without_a_session_records_nothing()
    {
        var (timer, _, log) = Make();
        Assert.True(timer.Stop().Success);
        Assert.Empty(log.Records);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(13 * 3600)]
    public void Unreasonable_lengths_are_refused(int seconds)
    {
        var (timer, _, _) = Make();
        Assert.False(timer.Start(TimeSpan.FromSeconds(seconds)).Success);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void A_second_session_cannot_start_while_one_runs()
    {
        var (timer, _, _) = Make();
        timer.Start(TimeSpan.FromMinutes(25));
        Assert.False(timer.Start(TimeSpan.FromMinutes(50)).Success);
    }

    [Fact]
    public void A_failing_log_never_interrupts_the_session()
    {
        var time = new ManualTime();
        var timer = new FocusTimer(new ThrowingLog(), null, time, tick: false);
        timer.Start(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));
        timer.Tick();
        Assert.Equal(TimerPhase.Completed, timer.Snapshot().Phase);
    }

    private sealed class ThrowingLog : IFocusSessionLog
    {
        public void Append(FocusSessionRecord record) => throw new IOException("disk full");
        public IReadOnlyList<FocusSessionRecord> ReadRecent(int max) => [];
    }
}

public class WorkspaceProfileTests
{
    private static VeilConfiguration Cfg() => new()
    {
        Rules = [new("c:\\a.exe", "A"), new("c:\\b.exe", "B")],
        Profiles = [new("Coding", ["c:\\a.exe"]), new("Research", ["c:\\b.exe", "c:\\a.exe"])],
    };

    [Fact]
    public void Next_cycles_through_all_applications_then_each_workspace()
    {
        var state = new MemoryUserState();
        var pm = new ProfileManager(Cfg, state);
        Assert.Null(pm.Active);
        pm.Next(); Assert.Equal("Coding", pm.ActiveName);
        pm.Next(); Assert.Equal("Research", pm.ActiveName);
        pm.Next(); Assert.Null(pm.ActiveName);             // back to "everything allowed"
        pm.Previous(); Assert.Equal("Research", pm.ActiveName);
        Assert.Equal("Research", state.Active);            // remembered for next time
    }

    [Fact]
    public void Select_checks_the_name_and_reports_changes()
    {
        var pm = new ProfileManager(Cfg);
        var changes = 0;
        pm.Changed += () => changes++;
        Assert.False(pm.Select("nope").Success);
        Assert.Equal(0, changes);
        Assert.True(pm.Select("coding").Success);           // names are not case sensitive
        Assert.Equal("Coding", pm.ActiveName);
        pm.Select("Coding");
        Assert.Equal(1, changes);                           // choosing the same one again changes nothing
        Assert.True(pm.Select(null).Success);
        Assert.Null(pm.Active);
    }

    [Fact]
    public void Next_with_no_workspaces_says_so()
    {
        var pm = new ProfileManager(() => new VeilConfiguration());
        var r = pm.Next();
        Assert.False(r.Success);
        Assert.Null(pm.Active);
    }

    [Fact]
    public void A_remembered_workspace_that_no_longer_exists_counts_as_none()
    {
        var pm = new ProfileManager(Cfg, new MemoryUserState { Active = "Deleted" });
        Assert.Null(pm.Active);
    }

    [Fact]
    public void A_workspace_only_narrows_the_allow_list()
    {
        var rules = Cfg().Rules;
        var coding = new WorkspaceProfile("Coding", ["c:\\a.exe", "c:\\sneaky.exe"]);
        var policy = new ApplicationPolicy(rules, coding);

        Assert.True(policy.Evaluate(FakeWindows.Win("1", "c:\\a.exe")).IsAllowed);
        var other = policy.Evaluate(FakeWindows.Win("2", "c:\\b.exe"));
        Assert.False(other.IsAllowed);
        Assert.Contains("Coding", other.Reason);
        // listed in the workspace but not allowed by the Super Admin: still denied
        var sneaky = policy.Evaluate(FakeWindows.Win("3", "c:\\sneaky.exe"));
        Assert.False(sneaky.IsAllowed);
        Assert.Contains("allow-list", sneaky.Reason);
    }

    [Fact]
    public void Validation_rejects_duplicate_blank_and_overlong_names()
    {
        Assert.Null(WorkspaceProfile.Validate([new("A", []), new("B", [])]));
        Assert.NotNull(WorkspaceProfile.Validate([new("A", []), new("a", [])]));
        Assert.NotNull(WorkspaceProfile.Validate([new("  ", [])]));
        Assert.NotNull(WorkspaceProfile.Validate([new(new string('x', 41), [])]));
        Assert.NotNull(WorkspaceProfile.Validate([new("A", [""])]));
    }
}

public class ConfigurationUpgradeTests
{
    [Fact]
    public void Profiles_and_preferences_survive_a_round_trip()
    {
        using var dir = new TempDir();
        var store = new JsonConfigurationStore(dir.Path);
        store.Save(new VeilConfiguration
        {
            Profiles = [new("Coding", ["c:\\a.exe"])],
            Preferences = new FocusPreferences(ReducedMotion: true, DefaultTimerMinutes: 50),
        });
        var loaded = store.Load();
        Assert.Equal("Coding", Assert.Single(loaded.Profiles).Name);
        Assert.True(loaded.Preferences.ReducedMotion);
        Assert.Equal(50, loaded.Preferences.DefaultTimerMinutes);
    }

    [Fact]
    public void Duplicate_workspace_names_are_refused_on_save()
    {
        using var dir = new TempDir();
        var store = new JsonConfigurationStore(dir.Path);
        Assert.Throws<ConfigurationException>(() => store.Save(new VeilConfiguration { Profiles = [new("A", []), new("a", [])] }));
        Assert.Throws<ConfigurationException>(() => store.Save(new VeilConfiguration { Preferences = new FocusPreferences(DefaultTimerMinutes: 0) }));
    }

    [Fact]
    public void A_file_written_by_an_earlier_version_loads_and_gains_the_new_shortcuts()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "config.json"),
            "{\"SchemaVersion\":1,\"Rules\":[],\"Hotkeys\":[{\"Command\":\"EmergencyRecovery\",\"Chord\":\"Ctrl+Alt+Esc\",\"Enabled\":true}],\"Preferences\":{}}");
        var cfg = new JsonConfigurationStore(dir.Path).Load();

        Assert.Empty(cfg.Profiles);
        Assert.Contains(cfg.Hotkeys, h => h.Command == HotkeyCommand.FocusTimer && h.Chord == "Ctrl+Alt+T");
        Assert.Contains(cfg.Hotkeys, h => h.Command == HotkeyCommand.NextProfile);
        Assert.Equal("Ctrl+Alt+Esc", cfg.Hotkeys.Single(h => h.Command == HotkeyCommand.EmergencyRecovery).Chord);
    }

    [Fact]
    public void An_upgrade_never_steals_a_chord_the_user_already_uses()
    {
        var upgraded = DefaultHotkeys.WithMissingDefaults(
        [
            new(HotkeyCommand.EmergencyRecovery, "Ctrl+Alt+Esc"),
            new(HotkeyCommand.Selector, "Ctrl+Alt+T"),       // the user's own choice
        ]);
        Assert.DoesNotContain(upgraded, h => h.Command == HotkeyCommand.FocusTimer);
        Assert.Contains(upgraded, h => h.Command == HotkeyCommand.NextProfile);
        Assert.Empty(DefaultHotkeys.Validate(upgraded));
    }

    [Fact]
    public void The_default_shortcuts_do_not_conflict()
    {
        Assert.Empty(DefaultHotkeys.Validate(DefaultHotkeys.Create()));
    }
}

public class UserFilesTests
{
    [Fact]
    public void Sessions_are_read_back_newest_first_and_damaged_lines_are_skipped()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sessions.jsonl");
        var log = new FileFocusSessionLog(path);
        var t = new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
        log.Append(new FocusSessionRecord(t, t.AddMinutes(25), 25, 1500, true, "Coding"));
        File.AppendAllText(path, "this is not json" + Environment.NewLine);
        log.Append(new FocusSessionRecord(t.AddHours(1), t.AddHours(1).AddMinutes(10), 50, 600, false, null));

        var recent = log.ReadRecent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal(600, recent[0].ActualSeconds);
        Assert.Equal("Coding", recent[1].Workspace);
    }

    [Fact]
    public void A_missing_or_unwritable_log_is_harmless()
    {
        using var dir = new TempDir();
        Assert.Empty(new FileFocusSessionLog(Path.Combine(dir.Path, "none.jsonl")).ReadRecent(5));
        // a directory where the file should be: appending fails quietly instead of throwing into a focus session
        var blocked = Path.Combine(dir.Path, "blocked.jsonl");
        Directory.CreateDirectory(blocked);
        new FileFocusSessionLog(blocked).Append(new FocusSessionRecord(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, true, null));
    }

    [Fact]
    public void The_active_workspace_is_remembered_and_a_corrupt_file_is_ignored()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "state.json");
        var store = new JsonUserStateStore(path);
        Assert.Null(store.LoadActiveWorkspace());
        store.SaveActiveWorkspace("Coding");
        Assert.Equal("Coding", store.LoadActiveWorkspace());
        File.WriteAllText(path, "{ not json");
        Assert.Null(store.LoadActiveWorkspace());
    }
}

public class PathResolverTests
{
    [Fact]
    public void Folders_and_missing_files_are_refused_with_a_reason()
    {
        using var dir = new TempDir();
        var resolver = new PathApplicationResolver();
        Assert.Contains("folder", resolver.Resolve(dir.Path).Error);
        Assert.Contains("does not exist", resolver.Resolve(Path.Combine(dir.Path, "nope.exe")).Error);
        Assert.NotNull(resolver.Resolve("").Error);
    }

    [Fact]
    public void A_program_gets_a_lower_case_identity_and_a_readable_name()
    {
        using var dir = new TempDir();
        var exe = Path.Combine(dir.Path, "MyTool.exe");
        File.WriteAllBytes(exe, [0]);
        var app = new PathApplicationResolver().Resolve("\"" + exe + "\"").Application;
        Assert.NotNull(app);
        Assert.Equal(exe.ToLowerInvariant(), app!.ApplicationId);
        Assert.Equal("MyTool", app.DisplayName);
    }

    [Fact]
    public void Other_file_types_are_not_applications()
    {
        using var dir = new TempDir();
        var doc = Path.Combine(dir.Path, "notes.txt");
        File.WriteAllText(doc, "x");
        Assert.Null(new PathApplicationResolver().Resolve(doc).Application);
    }
}

public class DragDropAndPagesTests
{
    private static readonly SuperAdminIdentity Alice = new("Windows", "S-1-5-21-100", "PC\\alice");
    private static readonly SuperAdminIdentity Bob = new("Windows", "S-1-5-21-200", "PC\\bob");

    private static (AdminShellViewModel Vm, MemoryStore Store) Shell(IApplicationResolver? resolver = null,
        Func<IReadOnlyList<WindowInfo>>? open = null, IFocusSessionLog? log = null, params ApplicationRule[] rules)
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = Alice, Rules = rules } };
        var auth = new FakeAuth(Alice, false);
        var admin = new AdminService(auth, store, new MemoryAudit());
        return (new AdminShellViewModel(admin, store, auth, new FakeSession(), () => [], "test", resolver, open, log), store);
    }

    private sealed class FakeSession : ISessionInspector
    {
        public SessionInfo Inspect() => new("Windows", "Desktop", SessionSupport.Supported, "notice");
    }

    private static FakeResolver Resolver() => new(new()
    {
        ["C:\\drop\\code.lnk"] = FakeResolver.App("c:\\apps\\code.exe", "Visual Studio Code"),
        ["C:\\drop\\chrome.exe"] = FakeResolver.App("c:\\apps\\chrome.exe", "Google Chrome"),
        ["C:\\drop\\readme.txt"] = ResolveResult.Fail("Only programs (.exe) and shortcuts to programs can be added."),
    });

    private static ApplicationsPageViewModel Apps(AdminShellViewModel vm) => (ApplicationsPageViewModel)vm.CurrentPage!;

    [Fact]
    public void Dropping_programs_adds_and_saves_them_in_one_step()
    {
        var (vm, store) = Shell(Resolver());
        Apps(vm).AddDropped(["C:\\drop\\code.lnk", "C:\\drop\\chrome.exe"]);

        Assert.Equal(["c:\\apps\\code.exe", "c:\\apps\\chrome.exe"], store.Config.Rules.Select(r => r.ApplicationId).ToArray());
        Assert.Equal("Visual Studio Code", store.Config.Rules[0].DisplayName);
        Assert.False(Apps(vm).IsDirty);
        Assert.Equal(StatusKind.Success, Apps(vm).StatusKind);
        Assert.Contains("Visual Studio Code", Apps(vm).StatusText);
    }

    [Fact]
    public void Things_that_are_not_applications_are_skipped_with_a_reason_and_the_rest_still_work()
    {
        var (vm, store) = Shell(Resolver());
        Apps(vm).AddDropped(["C:\\drop\\readme.txt", "C:\\drop\\chrome.exe"]);

        Assert.Single(store.Config.Rules);
        Assert.Contains("readme.txt", Apps(vm).StatusText);
        Assert.Contains("Skipped", Apps(vm).StatusText);
        Assert.Contains("Added Google Chrome", Apps(vm).StatusText);
    }

    [Fact]
    public void Only_non_applications_dropped_is_an_error_and_changes_nothing()
    {
        var (vm, store) = Shell(Resolver());
        Apps(vm).AddDropped(["C:\\drop\\readme.txt"]);
        Assert.Empty(store.Config.Rules);
        Assert.Equal(StatusKind.Error, Apps(vm).StatusKind);
    }

    [Fact]
    public void Dropping_an_application_twice_does_not_duplicate_it()
    {
        var (vm, store) = Shell(Resolver(), rules: new ApplicationRule("c:\\apps\\chrome.exe", "Chrome"));
        Apps(vm).AddDropped(["C:\\drop\\chrome.exe"]);
        Assert.Single(store.Config.Rules);
        Assert.Contains("already", Apps(vm).StatusText);
    }

    [Fact]
    public void When_saving_is_refused_nothing_is_stored_and_the_rows_stay_visible()
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = Alice } };
        var admin = new AdminService(new FakeAuth(Bob, false), store, new MemoryAudit());   // not the Super Admin
        var page = new ApplicationsPageViewModel(admin, store, Resolver());

        page.AddDropped(["C:\\drop\\chrome.exe"]);

        Assert.Empty(store.Config.Rules);
        Assert.Equal(StatusKind.Error, page.StatusKind);
        Assert.Single(page.Rules);
        Assert.True(page.IsDirty);
    }

    [Fact]
    public void A_drop_on_any_page_goes_to_Applications()
    {
        var (vm, store) = Shell(Resolver());
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Hotkeys");
        Assert.IsType<HotkeysPageViewModel>(vm.CurrentPage);

        Assert.True(vm.HandleDrop(["C:\\drop\\chrome.exe"]));

        Assert.IsType<ApplicationsPageViewModel>(vm.CurrentPage);
        Assert.Single(store.Config.Rules);
    }

    [Fact]
    public void Nothing_is_accepted_unless_the_super_admin_is_signed_in()
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = Alice } };
        var auth = new FakeAuth(Bob, false);
        var vm = new AdminShellViewModel(new AdminService(auth, store, new MemoryAudit()), store, auth, new FakeSession(), () => [], "test", Resolver());
        Assert.False(vm.HandleDrop(["C:\\drop\\chrome.exe"]));
        Assert.Empty(store.Config.Rules);
    }

    [Fact]
    public void Open_applications_can_be_allowed_with_one_click_and_leave_the_list()
    {
        WindowInfo W(string id, string app) => FakeWindows.Win(id, app, "title");
        var (vm, store) = Shell(Resolver(), () => [W("1", "c:\\apps\\chrome.exe"), W("2", "c:\\apps\\chrome.exe"), W("3", "c:\\apps\\known.exe"), W("4", "unknown:99")],
            rules: new ApplicationRule("c:\\apps\\known.exe", "Known"));
        var page = Apps(vm);

        var item = Assert.Single(page.OpenApps);                // listed once, already-allowed and unidentifiable ones left out
        Assert.Equal("c:\\apps\\chrome.exe", item.ApplicationId);
        page.AllowOpenAppCommand.Execute(item);

        Assert.Contains(store.Config.Rules, r => r.ApplicationId == "c:\\apps\\chrome.exe");
        Assert.Empty(page.OpenApps);
    }

    [Fact]
    public void A_broken_window_list_never_breaks_the_page()
    {
        var (vm, _) = Shell(Resolver(), () => throw new InvalidOperationException("enumeration failed"));
        Assert.Empty(Apps(vm).OpenApps);
    }

    // ---- workspace profiles page -------------------------------------------------------------------------------------

    private static ProfilesPageViewModel Profiles(AdminShellViewModel vm)
    {
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Workspace Profiles");
        return (ProfilesPageViewModel)vm.CurrentPage!;
    }

    [Fact]
    public void A_workspace_is_created_filled_and_saved()
    {
        var (vm, store) = Shell(rules: [new ApplicationRule("c:\\a.exe", "A"), new ApplicationRule("c:\\b.exe", "B")]);
        var page = Profiles(vm);
        Assert.True(page.HasNoProfiles);

        page.AddNamedCommand.Execute("Coding");
        page.AddNamedCommand.Execute("Coding");                  // same name again is made unique, not refused
        Assert.Equal(["Coding", "Coding 2"], page.Profiles.Select(p => p.Name).ToArray());

        page.Profiles[0].Apps.Single(a => a.ApplicationId == "c:\\a.exe").Included = true;
        Assert.True(page.IsDirty);
        Assert.Empty(store.Config.Profiles);                     // nothing stored until Save
        page.SaveCommand.Execute(null);

        Assert.False(page.IsDirty);
        Assert.Equal(["c:\\a.exe"], store.Config.Profiles[0].ApplicationIds.ToArray());
        Assert.Empty(store.Config.Profiles[1].ApplicationIds);
    }

    [Fact]
    public void An_application_that_left_the_allow_list_stays_in_the_workspace_until_the_workspace_is_edited()
    {
        var store = new MemoryStore
        {
            Config = new VeilConfiguration
            {
                SuperAdmin = Alice,
                Rules = [new ApplicationRule("c:\\a.exe", "A")],
                Profiles = [new("Coding", ["c:\\gone.exe", "c:\\a.exe"])],
            },
        };
        var auth = new FakeAuth(Alice, false);
        var page = new ProfilesPageViewModel(new AdminService(auth, store, new MemoryAudit()), store);

        Assert.Equal(["c:\\a.exe", "c:\\gone.exe"], page.Profiles[0].ToProfile().ApplicationIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void A_workspace_can_be_removed_and_saving_needs_the_super_admin()
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = Alice, Profiles = [new("Coding", [])] } };
        var page = new ProfilesPageViewModel(new AdminService(new FakeAuth(Bob, false), store, new MemoryAudit()), store);
        page.RemoveCommand.Execute(page.Profiles[0]);
        page.SaveCommand.Execute(null);

        Assert.Equal(StatusKind.Error, page.StatusKind);
        Assert.Single(store.Config.Profiles);                    // refused: still stored
    }

    // ---- appearance and timer pages ----------------------------------------------------------------------------------

    [Fact]
    public void Appearance_toggles_are_saved_and_reduced_motion_is_a_real_setting()
    {
        var (vm, store) = Shell();
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Appearance");
        var page = (AppearancePageViewModel)vm.CurrentPage!;
        Assert.False(page.ReducedMotion);

        page.ReducedMotion = true;
        page.ShowSystemStatus = false;
        page.SaveCommand.Execute(null);

        Assert.True(store.Config.Preferences.ReducedMotion);
        Assert.False(store.Config.Preferences.ShowSystemStatus);
        Assert.True(store.Config.Preferences.ShowClock);          // untouched preferences are kept
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("721")]
    [InlineData("")]
    public void The_default_session_length_must_be_a_sensible_number(string text)
    {
        var (vm, store) = Shell();
        vm.SelectedSection = vm.Sections.First(s => s.Name == "Focus Timer");
        var page = (FocusTimerPageViewModel)vm.CurrentPage!;
        page.DefaultMinutesText = text;
        page.SaveCommand.Execute(null);
        Assert.Equal(StatusKind.Error, page.StatusKind);
        Assert.Equal(25, store.Config.Preferences.DefaultTimerMinutes);
    }

    [Fact]
    public void A_valid_default_length_is_saved_and_the_totals_add_up()
    {
        var time = new ManualTime();
        var log = new MemorySessionLog();
        var now = time.GetUtcNow();
        log.Append(new FocusSessionRecord(now.AddDays(-10), now.AddDays(-10).AddMinutes(25), 25, 1500, true, null));
        log.Append(new FocusSessionRecord(now.AddDays(-3), now.AddDays(-3).AddMinutes(50), 50, 3000, true, "Research"));
        log.Append(new FocusSessionRecord(now, now.AddMinutes(20), 25, 1200, false, "Coding"));

        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = Alice } };
        var admin = new AdminService(new FakeAuth(Alice, false), store, new MemoryAudit());
        var page = new FocusTimerPageViewModel(admin, store, log, time);

        Assert.Equal("20 min", page.Today);
        Assert.Equal("1 h 10 min", page.ThisWeek);               // 20 + 50 minutes; the 10-day-old session is outside the week
        Assert.Equal(3, page.Recent.Count);
        Assert.Equal("Stopped early", page.Recent[0].Outcome);   // newest first

        page.DefaultMinutesText = "45";
        page.SaveCommand.Execute(null);
        Assert.Equal(45, store.Config.Preferences.DefaultTimerMinutes);
        Assert.Contains("Ctrl+Alt+T", page.HowToStart);
    }

    [Fact]
    public void Profiles_and_preferences_need_the_super_admin_in_the_service_too()
    {
        var store = new MemoryStore { Config = new VeilConfiguration { SuperAdmin = Alice } };
        var admin = new AdminService(new FakeAuth(Bob, false), store, new MemoryAudit());
        Assert.False(admin.UpdateProfiles([new("A", [])]).Success);
        Assert.False(admin.UpdatePreferences(new FocusPreferences(ReducedMotion: true)).Success);
        Assert.Empty(store.Config.Profiles);
        Assert.False(store.Config.Preferences.ReducedMotion);

        var granted = new AdminService(new FakeAuth(Alice, false), store, new MemoryAudit());
        Assert.False(granted.UpdateProfiles([new("A", []), new("a", [])]).Success);   // validated as well
        Assert.True(granted.UpdateProfiles([new("A", [])]).Success);
    }
}
