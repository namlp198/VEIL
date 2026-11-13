#pragma warning disable CS0618 // Bitmap.Save(string, int?) is the simplest PNG writer for a throw-away harness
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using VEIL.Application;
using VEIL.Domain;
using VEIL.Infrastructure;
using VEIL.Platform.Abstractions;
using VEIL.Presentation;
using VEIL.Presentation.ViewModels;
using VEIL.Presentation.Views;

// Renders the Admin Settings window with the VEIL theme in headless Avalonia (Skia) and checks its behaviour.
// Usage: dotnet run --project tools/AdminShots [output-dir]
var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "veil-admin-shots");
Directory.CreateDirectory(outDir);

AppBuilder.Configure<VeilApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var fails = 0;
void Check(string what, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}"); if (!ok) fails++; }

var alice = new SuperAdminIdentity("Windows", "S-1-5-21-1000-2000-3000-1001", "WORKSTATION\\alice");
var bob = new SuperAdminIdentity("Windows", "S-1-5-21-1000-2000-3000-1002", "WORKSTATION\\bob");

Dispatcher.UIThread.Invoke(() =>
{
    string Shot(AdminShellViewModel vm, string name, double? width = null, double? height = null)
    {
        var w = new AdminWindow(vm);
        if (width is { } ww) { w.MinWidth = 0; w.MinHeight = 0; w.Width = ww; w.Height = height ?? w.Height; }
        w.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Path.Combine(outDir, name + ".png");
        using (var frame = w.CaptureRenderedFrame()) frame?.Save(path);
        w.Close();
        return path;
    }

    (AdminShellViewModel vm, JsonConfigurationStore store, Fake auth) Make(SuperAdminIdentity current, SuperAdminIdentity? superAdmin, bool osAdmin = false, AuthenticationStatus status = AuthenticationStatus.Verified, IEnumerable<ApplicationRule>? rules = null, IEnumerable<WorkspaceProfile>? profiles = null, Func<IReadOnlyList<WindowInfo>>? open = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "veil-admin-" + Guid.NewGuid().ToString("N"));
        var store = new JsonConfigurationStore(dir);
        store.Save(new VeilConfiguration { SuperAdmin = superAdmin, Rules = (rules ?? []).ToList(), Profiles = (profiles ?? []).ToList() });
        var auth = new Fake(current, osAdmin, status);
        var admin = new AdminService(auth, store, new FileAuditLog(Path.Combine(dir, "logs")));
        var displays = () => (IReadOnlyList<DisplayInfo>)
            [new("DISPLAY2", "DISPLAY2", new WindowBounds(0, 0, 1920, 1080), 1.0, true), new("DISPLAY1", "DISPLAY1", new WindowBounds(1920, 0, 1920, 1080), 1.0, false)];
        var vm = new AdminShellViewModel(admin, store, auth, new Session(), displays, "1.0.0-dev", null, open, new SampleSessions());
        return (vm, store, auth);
    }

    var rules = new[]
    {
        new ApplicationRule("c:\\program files\\microsoft vs code\\code.exe", "Visual Studio Code", true, TransientWindowPolicy.FollowParent),
        new ApplicationRule("c:\\program files\\google\\chrome\\application\\chrome.exe", "Google Chrome"),
        new ApplicationRule("c:\\st\\stm32cubeide\\stm32cubeide.exe", "STM32CubeIDE", false),
    };

    // 1. Not provisioned
    var s1 = Make(bob, null, osAdmin: true);
    Check("not provisioned: access denied with an explanatory title", s1.vm.IsDenied && s1.vm.AccessTitle == "No Super Admin is set up" && s1.vm.CurrentPage is null);
    Console.WriteLine("      " + Shot(s1.vm, "admin-1-not-provisioned"));

    // 2. Wrong identity (AT-009)
    var s2 = Make(bob, alice);
    Check("wrong account: access denied, no page is created", s2.vm.IsDenied && s2.vm.AccessTitle == "Access denied" && s2.vm.CurrentPage is null);
    Check("wrong account: the account is shown and no password is requested", s2.vm.IdentityText.Contains("bob") && !s2.vm.AccessMessage.Contains("password", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine("      " + Shot(s2.vm, "admin-2-access-denied"));

    // 2b. Identity not trustworthy
    var s2b = Make(alice, alice, status: AuthenticationStatus.Unavailable);
    Check("untrustworthy identity: denied, not silently granted", s2b.vm.IsDenied && s2b.vm.AccessTitle == "Identity could not be verified");

    // 3. Granted: Applications (AT-010)
    var s3 = Make(alice, alice, rules: rules);
    Check("super admin: granted and Applications is selected", s3.vm.IsGranted && s3.vm.SelectedSection?.Name == "Applications" && s3.vm.CurrentPage is ApplicationsPageViewModel);
    var apps = (ApplicationsPageViewModel)s3.vm.CurrentPage!;
    Check("applications: 3 rules listed, none dirty", apps.Rules.Count == 3 && !apps.IsDirty);
    Console.WriteLine("      " + Shot(s3.vm, "admin-3-applications"));

    // 3b. Edit and save, then verify the stored configuration
    apps.NewApplicationId = "c:\\tools\\notepad++\\notepad++.exe";
    apps.AddCommand.Execute(null);
    Check("add: row appears, page is dirty, nothing saved yet", apps.Rules.Count == 4 && apps.IsDirty && s3.store.Load().Rules.Count == 3);
    apps.SaveCommand.Execute(null);
    Check("save: stored list has 4 rules and the page is clean", s3.store.Load().Rules.Count == 4 && !apps.IsDirty && apps.StatusKind == StatusKind.Success);
    apps.NewApplicationId = "C:\\Tools\\Notepad++\\Notepad++.exe";
    apps.AddCommand.Execute(null);
    Check("add duplicate (case-insensitive): refused with a message", apps.Rules.Count == 4 && apps.StatusKind == StatusKind.Error);
    apps.RemoveCommand.Execute(apps.Rules[3]);
    apps.RevertCommand.Execute(null);
    Check("remove then discard restores the saved list", apps.Rules.Count == 4);

    // 3c. Session changes to a different account before saving: the save must be refused (re-authorized every time)
    apps.Rules[0].Enabled = false;
    s3.auth.Current = bob;
    apps.SaveCommand.Execute(null);
    Check("save after the OS account changed: refused, stored list unchanged", apps.StatusKind == StatusKind.Error && s3.store.Load().Rules[0].Enabled);
    s3.auth.Current = alice;

    // 4. Hotkeys: conflict and invalid chord (AT-011)
    var s4 = Make(alice, alice);
    s4.vm.SelectedSection = s4.vm.Sections.First(s => s.Name == "Hotkeys");
    var hk = (HotkeysPageViewModel)s4.vm.CurrentPage!;
    hk.Rows.First(r => r.Command == HotkeyCommand.HideAll).Chord = "Ctrl+Alt+V";
    hk.Rows.First(r => r.Command == HotkeyCommand.Selector).Chord = "V";
    Check("invalid chord is flagged while typing", hk.Rows.First(r => r.Command == HotkeyCommand.Selector).HasProblem);
    var before = s4.store.Load().Hotkeys.ToList();
    hk.SaveCommand.Execute(null);
    Check("conflicting/invalid set refused, message names the chord", hk.StatusKind == StatusKind.Error && hk.StatusText.Contains("Ctrl+Alt+V"));
    Check("stored bindings are unchanged after the refusal", s4.store.Load().Hotkeys.SequenceEqual(before));
    Console.WriteLine("      " + Shot(s4.vm, "admin-4-hotkeys-conflict"));
    var er = hk.Rows.First(r => r.IsLocked);
    Check("emergency recovery is marked required and cannot be disabled in the UI", er.Command == HotkeyCommand.EmergencyRecovery && er.IsLocked);

    // 5. Security
    var s5 = Make(alice, alice, osAdmin: false);
    s5.vm.SelectedSection = s5.vm.Sections.First(s => s.Name == "Security");
    Check("security page states the limitation and exposes no password", s5.vm.CurrentPage is SecurityPageViewModel sp && sp.Limitation.Contains("not a secure lock") && sp.Facts.All(f => !f.Label.Contains("password", StringComparison.OrdinalIgnoreCase)));
    Console.WriteLine("      " + Shot(s5.vm, "admin-5-security"));

    // 6. Diagnostics
    var s6 = Make(alice, alice);
    s6.vm.SelectedSection = s6.vm.Sections.First(s => s.Name == "Diagnostics");
    Check("diagnostics lists both displays with scale", s6.vm.CurrentPage is DiagnosticsPageViewModel dp && dp.Displays.Count == 2 && dp.Displays[0].Value.Contains("scale 100%"));
    Console.WriteLine("      " + Shot(s6.vm, "admin-6-diagnostics"));

    // 7. A group that is specified but not built
    var s7 = Make(alice, alice);
    s7.vm.SelectedSection = s7.vm.Sections.First(s => s.Name == "Backup & Restore");
    Check("unbuilt group shows a planned placeholder, not a fake form", s7.vm.CurrentPage is PlaceholderPageViewModel && !s7.vm.SelectedSection!.IsImplemented);
    Console.WriteLine("      " + Shot(s7.vm, "admin-7-planned-group"));

    // 10. Drag and drop, open applications, workspaces, appearance and timer
    WindowInfo Open(string id, string exe) => new(id, "1", exe, "t", "D0", new WindowBounds(0, 0, 1, 1), true, false, false);
    var s10 = Make(alice, alice, rules: rules,
        profiles: [new WorkspaceProfile("Coding", [rules[0].ApplicationId, rules[1].ApplicationId]), new WorkspaceProfile("Research", [rules[1].ApplicationId])],
        open: () => [Open("1", "c:\\program files\\windowsapps\\windowsterminal.exe"), Open("2", rules[0].ApplicationId), Open("3", "c:\\tools\\notes.exe")]);
    var apps10 = (ApplicationsPageViewModel)s10.vm.CurrentPage!;
    Check("open applications: only the ones not yet allowed are offered", apps10.OpenApps.Count == 2 && apps10.OpenApps.All(a => a.ApplicationId != rules[0].ApplicationId));
    Console.WriteLine("      " + Shot(s10.vm, "admin-10-applications-open-now"));
    s10.vm.IsDragActive = true;
    Console.WriteLine("      " + Shot(s10.vm, "admin-10b-drop-hint"));
    s10.vm.IsDragActive = false;
    s10.vm.SelectedSection = s10.vm.Sections.First(s => s.Name == "Workspace Profiles");
    var prof10 = (ProfilesPageViewModel)s10.vm.CurrentPage!;
    Check("workspaces: two listed, the first selected", prof10.Profiles.Count == 2 && prof10.Selected?.Name == "Coding");
    Console.WriteLine("      " + Shot(s10.vm, "admin-11-workspaces"));
    s10.vm.SelectedSection = s10.vm.Sections.First(s => s.Name == "Appearance");
    Console.WriteLine("      " + Shot(s10.vm, "admin-12-appearance"));
    s10.vm.SelectedSection = s10.vm.Sections.First(s => s.Name == "Focus Timer");
    Check("timer page shows the recorded sessions", ((FocusTimerPageViewModel)s10.vm.CurrentPage!).Recent.Count == 2);
    Console.WriteLine("      " + Shot(s10.vm, "admin-13-focus-timer"));
    var empty10 = Make(alice, alice, rules: rules);
    empty10.vm.SelectedSection = empty10.vm.Sections.First(s => s.Name == "Workspace Profiles");
    Console.WriteLine("      " + Shot(empty10.vm, "admin-11b-workspaces-empty"));

    // 9. Small effective window (what a 1080p display at 200% leaves: 864 x 486 dip) must not overlap or lose navigation
    var s9 = Make(alice, alice, rules: rules);
    Console.WriteLine("      " + Shot(s9.vm, "admin-9-small-864x486", 864, 486));
    s9.vm.SelectedSection = s9.vm.Sections.First(s => s.Name == "Hotkeys");
    Console.WriteLine("      " + Shot(s9.vm, "admin-9b-small-hotkeys-864x486", 864, 486));

    // 8. Empty allow-list
    var s8 = Make(alice, alice);
    Check("empty allow-list shows the empty state", ((ApplicationsPageViewModel)s8.vm.CurrentPage!).IsEmpty);
    Console.WriteLine("      " + Shot(s8.vm, "admin-8-applications-empty"));
});

Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
return fails;

sealed class Fake(SuperAdminIdentity current, bool osAdmin, AuthenticationStatus status) : IAuthenticationProvider
{
    public SuperAdminIdentity Current { get; set; } = current;
    public SuperAdminIdentity GetCurrentIdentity() => Current;
    public bool IsOsAdministrator() => osAdmin;
    public AuthenticationResult Authenticate() => new(status, status == AuthenticationStatus.Verified ? Current : null, status.ToString());
}

sealed class Session : ISessionInspector
{
    public SessionInfo Inspect() => new("Windows", "Desktop", SessionSupport.Supported,
        "Overlay Focus is a visibility feature, not a secure lock. UAC prompts, the secure desktop and elevated windows are outside VEIL's control.");
}

sealed class SampleSessions : IFocusSessionLog
{
    public void Append(FocusSessionRecord record) { }
    public IReadOnlyList<FocusSessionRecord> ReadRecent(int max)
    {
        var now = DateTimeOffset.UtcNow;
        return [new(now.AddHours(-2), now.AddHours(-1.5), 50, 1800, false, "Coding"), new(now.AddDays(-1), now.AddDays(-1).AddMinutes(25), 25, 1500, true, null)];
    }
}
