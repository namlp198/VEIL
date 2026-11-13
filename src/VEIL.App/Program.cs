using VEIL.Application;
using VEIL.Domain;
using VEIL.Infrastructure;
using VEIL.Platform.Abstractions;
using VEIL.Platform.Linux.X11;
using VEIL.Platform.Windows;
using VEIL.Presentation;
using VEIL.Presentation.ViewModels;
using VEIL.Presentation.Views;
using Avalonia;

using System.Runtime.Versioning;

namespace VEIL.App;

/// <summary>
/// Phase 1/2 spike host (console). Composition root only: wires adapters into the shared application layer.
/// The Avalonia presentation layer is scheduled for Phase 6; the console host keeps the native core testable now.
/// </summary>
internal static class Program
{
    private const string Usage = """
        VEIL - Controlled Desktop Environment (spike host)

        Usage:
          veil diag                         Session, displays, hotkey set, admin status
          veil windows                      Eligible windows and their allow-list decision
          veil focus [--seconds N] [--reveal text]... [--timer MIN] [--workspace NAME]
                                            Enter Focus Mode (--reveal may repeat; --timer starts a focus session)
                                            (Ctrl+Alt+U off, Ctrl+Alt+Esc emergency recovery)
          veil run                          Start the resident agent (tray icon + global hotkeys). Never enters Focus Mode by itself.
          veil autostart status|enable|disable [--machine]   Start-with-Windows registry entry (HKCU, or HKLM with --machine)
          veil settings [--display primary|pointer]   Open the Admin Settings window (Super Admin only);
                                            default: the display under the pointer
          veil admin status                 Show Super Admin state
          veil admin provision [--account <name|SID>]   Designate the Super Admin (needs OS admin; default: the current account)
          veil admin allow <exe-path>       Add an application to the allow-list (Super Admin only)
          veil admin list                   Show the allow-list
          veil config export <file>         Write a configuration backup
          veil config import <file>         Validate and apply a configuration backup

        Overlay Focus is a visibility feature, not a secure lock or kiosk. A local OS administrator can always override it.
        Environment: VEIL_CONFIG_DIR overrides the protected configuration directory.
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
        try
        {
            var ctx = Context.Create();
            return args[0].ToLowerInvariant() switch
            {
                "diag" => Diag(ctx),
                "windows" => OperatingSystem.IsWindows() ? WindowsList(ctx) : Fail("Window control is not available on this platform yet."),
                "focus" => OperatingSystem.IsWindows() ? FocusWindows(ctx, args) : Fail($"Overlay Focus is not supported in this session: {ctx.Session.Inspect().Notice}"),
                "admin" => Admin(ctx, args),
                "config" => ConfigCommand(ctx, args),
                "settings" => Settings(ctx, args),
                "run" or "agent" => OperatingSystem.IsWindows() ? AgentHost.Run(ctx, args) : Fail("The resident agent is only available on Windows for now."),
                "autostart" => AutoStartCommand(args),
                _ => Fail($"Unknown command '{args[0]}'.\n\n{Usage}"),
            };
        }
        catch (ConfigurationException ex) { return Fail($"Configuration error: {ex.Message}"); }
        catch (PlatformNotSupportedException ex) { return Fail(ex.Message); }
    }

    internal static int Fail(string message) { Console.Error.WriteLine(message); return 1; }

    // ---- diagnostics (FR-DISP-008, UI/UX 3.5) -------------------------------------------------------------------

    private static int Diag(Context ctx)
    {
        var s = ctx.Session.Inspect();
        Console.WriteLine($"VEIL {typeof(Program).Assembly.GetName().Version}");
        Console.WriteLine($"OS/session : {s.Os} / {s.SessionType} ({s.Support})");
        Console.WriteLine($"Notice     : {s.Notice}");
        Console.WriteLine($"Config     : {ctx.Store.FilePath}");
        var cfg = ctx.Store.Load();
        Console.WriteLine($"Super Admin: {(cfg.SuperAdmin is null ? "not provisioned" : $"{cfg.SuperAdmin.Platform} {cfg.SuperAdmin.Principal} ({cfg.SuperAdmin.DisplayName})")}");
        Console.WriteLine($"Allow-list : {cfg.Rules.Count} rule(s)");
        Console.WriteLine("Hotkeys    : " + string.Join(", ", cfg.Hotkeys.Where(h => h.Enabled).Select(h => $"{h.Command}={h.Chord}")));
        if (!OperatingSystem.IsWindows() || ctx.Native is null) return 0;
        foreach (var d in ctx.Native.Displays.GetDisplays())
            Console.WriteLine($"Display    : {d.Name}{(d.IsPrimary ? " [primary]" : "")} {d.Bounds.Width}x{d.Bounds.Height} at ({d.Bounds.X},{d.Bounds.Y}) scale {d.ScaleFactor:0.##}");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static int WindowsList(Context ctx)
    {
        if (ctx.Native is null) return Fail("Window control is not available on this platform yet.");
        var cfg = ctx.Store.Load();
        var selector = new WindowSelectorService(ctx.Native.Windows, () => new ApplicationPolicy(cfg.Rules), () => []);
        var entries = selector.BuildEntries();
        if (entries.Count == 0) { Console.WriteLine("No eligible windows detected."); return 0; }
        foreach (var e in entries)
            Console.WriteLine($"{e.Number,2}. [{(e.IsAllowed ? "Allowed    " : "Not allowed")}] [{e.State,-9}] {Path.GetFileName(e.Window.ApplicationId),-24} {e.Window.Title}");
        return 0;
    }

    // ---- focus ---------------------------------------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static int FocusWindows(Context ctx, string[] args)
    {
        if (ctx.Native is null)
            return Fail($"Overlay Focus is not supported in this session: {ctx.Session.Inspect().Notice}");

        var seconds = 0;
        var timerMinutes = 0;
        string? workspace = null;
        var revealFilters = new List<string>();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--seconds" && !int.TryParse(args[i + 1], out seconds)) return Fail("--seconds needs a number.");
            if (args[i] == "--timer" && !int.TryParse(args[i + 1], out timerMinutes)) return Fail("--timer needs a number of minutes.");
            if (args[i] == "--workspace") workspace = args[i + 1];
            if (args[i] == "--reveal") revealFilters.Add(args[i + 1]);
        }

        var native = ctx.Native;
        double? scaleOverride = double.TryParse(Environment.GetEnvironmentVariable("VEIL_SCALE_OVERRIDE"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var so) ? so : null;

        using var done = new ManualResetEventSlim();
        using var runtime = new FocusRuntime(native, ctx.Store.Load, Console.WriteLine, scaleOverride, ctx.Sessions, ctx.UserState) { EndOnOff = true };
        runtime.EndRequested += done.Set;

        // Recovery path must exist before any overlay is created (architecture 5.2). FocusOn is started by this command itself.
        var startError = runtime.Start(includeFocusOn: false);
        foreach (var w in runtime.Warnings) Console.Error.WriteLine("Hotkey " + w);
        if (startError is not null) return Fail(startError);

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; runtime.Focus.EmergencyRecover(); done.Set(); };

        if (workspace is not null)
        {
            var chosen = runtime.Profiles.Select(workspace);
            Console.WriteLine(chosen.Message);
            if (!chosen.Success) return Fail(chosen.Message);
        }
        var on = runtime.Focus.FocusOn();
        Console.WriteLine(on.Message);
        if (!on.Success) return 1;
        Console.WriteLine("Hotkeys: " + string.Join(", ", runtime.Registered.Select(h => $"{h.Command}={h.Chord}")));
        foreach (var filter in revealFilters) Console.WriteLine(runtime.RevealFirst(filter).Message);
        if (timerMinutes > 0) Console.WriteLine(runtime.StartTimer(timerMinutes).Message);
        if (seconds > 0) Console.WriteLine($"Auto-exit in {seconds}s.");

        done.Wait(seconds > 0 ? TimeSpan.FromSeconds(seconds) : Timeout.InfiniteTimeSpan);
        runtime.Focus.FocusOff();
        runtime.Focus.EmergencyRecover(); // idempotent final guarantee
        Console.WriteLine("Desktop restored.");
        return 0;
    }

    // ---- settings window ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Opens the Avalonia Admin Settings window. Access is decided by the OS identity through <see cref="AdminService"/>
    /// (the window shows an access-denied card for anyone but the Super Admin); no password is requested.
    /// </summary>
    private static int Settings(Context ctx, string[] args)
    {
        var onPointer = true;
        for (var i = 1; i < args.Length - 1; i++)
            if (args[i] == "--display") onPointer = !args[i + 1].Equals("primary", StringComparison.OrdinalIgnoreCase);

        VeilApp.MainWindowFactory = () => CreateSettingsWindow(ctx, onPointer);
        try
        {
            return AppBuilder.Configure<VeilApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            return Fail($"The settings window could not be opened: {ex.Message}");
        }
    }

    /// <summary>Builds the Admin Settings window on the display under the pointer (or the primary display).</summary>
    internal static AdminWindow CreateSettingsWindow(Context ctx, bool onPointer)
    {
        var admin = new AdminService(ctx.Auth, ctx.Store, ctx.Audit);
        Func<IReadOnlyList<DisplayInfo>> displays = () => OperatingSystem.IsWindows() ? WindowsDisplays(ctx) : [];
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev";
        IApplicationResolver resolver = OperatingSystem.IsWindows() && ctx.Native is not null ? ctx.Native.Resolver : new PathApplicationResolver();
        Func<IReadOnlyList<WindowInfo>> openWindows = () => OperatingSystem.IsWindows() ? WindowsOpenWindows(ctx) : [];
        var window = new AdminWindow(new AdminShellViewModel(admin, ctx.Store, ctx.Auth, ctx.Session, displays, version,
            resolver, openWindows, ctx.Sessions));
        var list = displays();
        if (list.Count > 0)
        {
            var pointer = OperatingSystem.IsWindows() ? WindowsPointer(ctx) : null;
            var display = SelectorPlacementCalculator.PickDisplay(list, pointer, onPointer);
            // Diagnostic: VEIL_SCALE_OVERRIDE (see focus) also applies here; pair it with AVALONIA_GLOBAL_SCALE_FACTOR.
            if (double.TryParse(Environment.GetEnvironmentVariable("VEIL_SCALE_OVERRIDE"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var forced) && forced > 0)
                display = display with { ScaleFactor = forced };
            var (w, h, x, y) = SelectorPlacementCalculator.CenterWindow(display, 1180, 780);
            window.PlaceOn(w, h, x, y);
            Console.WriteLine($"Settings on {display.Name} (scale {display.ScaleFactor:0.##}): {w:0}x{h:0} dip at ({x},{y})");
        }
        return window;
    }

    [SupportedOSPlatform("windows")]
    private static (int X, int Y)? WindowsPointer(Context ctx) => ctx.Native?.Displays.GetPointerPosition();

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<WindowInfo> WindowsOpenWindows(Context ctx) => ctx.Native?.Windows.GetWindows() ?? [];

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<DisplayInfo> WindowsDisplays(Context ctx) => ctx.Native?.Displays.GetDisplays() ?? [];

    [SupportedOSPlatform("windows")]
    private static SuperAdminIdentity? ResolveAccount(Context ctx, string account) => ctx.Native?.Authentication.ResolveIdentity(account);

    // ---- start with Windows ------------------------------------------------------------------------------------------

    private static int AutoStartCommand(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return Fail("Start-with-Windows is only available on Windows.");
        return AutoStartWindows(args);
    }

    [SupportedOSPlatform("windows")]
    private static int AutoStartWindows(string[] args)
    {
        var scope = args.Contains("--machine") ? AutoStartScope.AllUsers : AutoStartScope.CurrentUser;
        var action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        try
        {
            switch (action)
            {
                case "status":
                    foreach (var sc in new[] { AutoStartScope.CurrentUser, AutoStartScope.AllUsers })
                        Console.WriteLine($"{(sc == AutoStartScope.AllUsers ? "HKLM (all users)" : "HKCU (this user)")}: {WindowsAutoStart.Get(sc) ?? "not set"}");
                    return 0;
                case "enable":
                    var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
                    // The windowless agent is the one to start at logon when it sits next to this executable.
                    var agent = Path.Combine(Path.GetDirectoryName(exe)!, "veil-agent.exe");
                    WindowsAutoStart.Enable(scope, File.Exists(agent) ? agent : exe, "run");
                    Console.WriteLine($"Enabled: {WindowsAutoStart.Get(scope)}");
                    return 0;
                case "disable":
                    WindowsAutoStart.Disable(scope);
                    Console.WriteLine("Disabled.");
                    return 0;
                default: return Fail(Usage);
            }
        }
        catch (UnauthorizedAccessException) { return Fail("Writing the machine-wide entry needs administrator rights."); }
    }

    // ---- administration -----------------------------------------------------------------------------------------

    private static int Admin(Context ctx, string[] args)
    {
        var svc = new AdminService(ctx.Auth, ctx.Store, ctx.Audit);
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        switch (sub)
        {
            case "status":
                var a = svc.Authorize();
                Console.WriteLine($"{a.Decision}: {a.Message}");
                return a.IsGranted ? 0 : 3;
            case "provision":
                var target = ctx.Auth.GetCurrentIdentity();
                var accountIdx = Array.IndexOf(args, "--account");
                if (accountIdx >= 0)
                {
                    if (accountIdx + 1 >= args.Length) return Fail("--account needs a user name or SID.");
                    var resolved = OperatingSystem.IsWindows() ? ResolveAccount(ctx, args[accountIdx + 1]) : null;
                    if (resolved is null) return Fail($"Account '{args[accountIdx + 1]}' could not be resolved.");
                    target = resolved;
                }
                var p = svc.Provision(target);
                Console.WriteLine(p.Message);
                return p.Success ? 0 : 3;
            case "allow" when args.Length > 2:
                var exe = Path.GetFullPath(args[2]).ToLowerInvariant();
                var rules = ctx.Store.Load().Rules.Where(r => !r.ApplicationId.Equals(exe, StringComparison.OrdinalIgnoreCase)).ToList();
                rules.Add(new ApplicationRule(exe, Path.GetFileNameWithoutExtension(exe)));
                var u = svc.UpdateAllowList(rules);
                Console.WriteLine(u.Message);
                return u.Success ? 0 : 3;
            case "list":
                foreach (var r in ctx.Store.Load().Rules) Console.WriteLine($"{(r.Enabled ? "on " : "off")} {r.ApplicationId}");
                return 0;
            default: return Fail(Usage);
        }
    }

    private static int ConfigCommand(Context ctx, string[] args)
    {
        if (args.Length < 3) return Fail(Usage);
        var svc = new AdminService(ctx.Auth, ctx.Store, ctx.Audit);
        var authz = svc.Authorize();
        if (!authz.IsGranted) return Fail($"Backup and restore need Super Admin: {authz.Message}");
        if (args[1] == "export") { ctx.Store.Export(args[2]); Console.WriteLine("Exported."); return 0; }
        if (args[1] == "import") { ctx.Store.Import(args[2]); Console.WriteLine("Imported."); return 0; }
        return Fail(Usage);
    }

    private sealed class ConsoleDiagnostics : IDiagnosticSink
    {
        public void Report(string message) => Console.Error.WriteLine($"[diag] {message}");
    }

    /// <summary>Per-platform wiring. <see cref="Native"/> is null where window control is not implemented or unsupported.</summary>
    internal sealed class Context
    {
        public required JsonConfigurationStore Store { get; init; }
        public required FileAuditLog Audit { get; init; }
        public required IAuthenticationProvider Auth { get; init; }
        public required ISessionInspector Session { get; init; }
        public WindowsPlatform? Native { get; init; }
        /// <summary>The user's own focus-session record and remembered workspace (not the protected machine configuration).</summary>
        public required FileFocusSessionLog Sessions { get; init; }
        public required JsonUserStateStore UserState { get; init; }

        public static Context Create()
        {
            var (dir, isProtected) = InstallInfo.ResolveConfigDir(
                Environment.GetEnvironmentVariable("VEIL_CONFIG_DIR"),
                InstallInfo.TryRead(AppContext.BaseDirectory),
                OperatingSystem.IsWindows()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VEIL")
                    : "/etc/veil");

            if (OperatingSystem.IsWindows())
            {
                WindowsPlatform.EnableDpiAwareness();
                var native = new WindowsPlatform();
                return new Context
                {
                    // Only the machine location is ACL-locked; a developer override or a per-user trial install is not.
                    Store = new JsonConfigurationStore(dir, isProtected ? native.Storage : null),
                    Audit = new FileAuditLog(Path.Combine(dir, "logs"), fallbackDirectory: UserAuditDir()),
                    Auth = native.Authentication,
                    Session = native.Session,
                    Native = native,
                    Sessions = new FileFocusSessionLog(Path.Combine(UserDataDir(), "focus-sessions.jsonl")),
                    UserState = new JsonUserStateStore(Path.Combine(UserDataDir(), "user-state.json")),
                };
            }
            return new Context
            {
                Store = new JsonConfigurationStore(dir, isProtected ? new LinuxProtectedStorage() : null),
                Audit = new FileAuditLog(Path.Combine(dir, "logs"), fallbackDirectory: UserAuditDir()),
                Auth = new LinuxAuthenticationProvider(),
                Session = new LinuxSessionInspector(),
                Sessions = new FileFocusSessionLog(Path.Combine(UserDataDir(), "focus-sessions.jsonl")),
                UserState = new JsonUserStateStore(Path.Combine(UserDataDir(), "user-state.json")),
            };
        }

        /// <summary>Per-user files (focus sessions, remembered workspace). VEIL_USER_DIR overrides it, for tests and trials.</summary>
        private static string UserDataDir() =>
            Environment.GetEnvironmentVariable("VEIL_USER_DIR") is { Length: > 0 } overridden
                ? overridden
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VEIL");

        private static string UserAuditDir() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VEIL", "audit");
    }
}
