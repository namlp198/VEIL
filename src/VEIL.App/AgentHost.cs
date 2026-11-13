using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using VEIL.Application;
using VEIL.Domain;
using VEIL.Presentation;
using VEIL.Presentation.Views;

namespace VEIL.App;

/// <summary>
/// The resident agent (<c>veil run</c>), started at logon by the installer's registry entry. It registers the global
/// hotkeys and shows a tray icon, but is deliberately idle: it never enters Focus Mode by itself, after boot or after a
/// crash (FR-LIFE-005). One instance per user session; launching it again opens the settings window instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AgentHost
{
    private const string MutexName = @"Local\VEIL.Agent.v1";
    private const string ShowSettingsEvent = @"Local\VEIL.Agent.ShowSettings.v1";

    public static string LogDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VEIL", "logs");

    public static int Run(Program.Context ctx, string[] args)
    {
        var log = new AgentLog(Path.Combine(LogDirectory, "agent.log"));
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var first);
        if (!first)
        {
            // Already running: ask the running agent to show its settings window, then leave.
            try { using var ev = EventWaitHandle.OpenExisting(ShowSettingsEvent); ev.Set(); } catch { /* the other agent is starting or exiting */ }
            log.Write("Agent already running; asked it to open settings.");
            Console.WriteLine("VEIL is already running; opened its settings.");
            return 0;
        }

        var native = ctx.Native ?? throw new InvalidOperationException("The Windows platform is not available.");
        double? scaleOverride = double.TryParse(Environment.GetEnvironmentVariable("VEIL_SCALE_OVERRIDE"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var so) ? so : null;

        log.Write($"VEIL agent starting (version {typeof(AgentHost).Assembly.GetName().Version}, config {ctx.Store.FilePath}).");
        using var runtime = new FocusRuntime(native, ctx.Store.Load, log.Write, scaleOverride, ctx.Sessions, ctx.UserState);
        var startError = runtime.Start(includeFocusOn: true);
        if (startError is not null) log.Write(startError);
        log.Write("Hotkeys: " + string.Join(", ", runtime.Registered.Select(h => $"{h.Command}={h.Chord}")));
        foreach (var w in runtime.Warnings) log.Write("Hotkey unavailable - " + w);
        log.Write("Agent ready. Focus Mode is off until a hotkey or the tray menu turns it on.");

        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
        AdminWindow? settingsWindow = null;
        TrayIcon? tray = null;
        IClassicDesktopStyleApplicationLifetime? lifetime = null;

        void OpenSettings()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (settingsWindow is { IsVisible: true }) { settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate(); return; }
                    settingsWindow = Program.CreateSettingsWindow(ctx, onPointer: true);
                    settingsWindow.Closed += (_, _) => settingsWindow = null;
                    settingsWindow.Show();
                    settingsWindow.Activate();
                }
                catch (Exception ex) { log.Write("Settings window failed: " + ex.Message); }
            });
        }

        VeilApp.Started = app =>
        {
            lifetime = app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            tray = BuildTray(runtime, log, OpenSettings, () => { runtime.Focus.EmergencyRecover(); lifetime?.Shutdown(); });
            TrayIcon.SetIcons(app, new TrayIcons { tray });
        };

        // A second launch (for example the Start Menu shortcut) signals this event to open the settings window.
        var listener = new Thread(() => { while (true) { showSettings.WaitOne(); OpenSettings(); } }) { IsBackground = true, Name = "VEIL.ShowSettings" };
        listener.Start();

        try
        {
            return AppBuilder.Configure<VeilApp>().UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            log.Write("Agent crashed: " + ex);
            return 1;
        }
        finally
        {
            // Whatever happened, no overlay may outlive the agent (FR-LIFE-003/004).
            runtime.Focus.EmergencyRecover();
            tray?.Dispose();
            log.Write("Agent stopped.");
        }
    }

    private static TrayIcon BuildTray(FocusRuntime runtime, AgentLog log, Action openSettings, Action exit)
    {
        string Chord(HotkeyCommand c) => runtime.Registered.FirstOrDefault(b => b.Command == c)?.Chord is { } x ? $"   ({x})" : string.Empty;

        var status = new NativeMenuItem { IsEnabled = false };
        var toggle = new NativeMenuItem();
        var timerMenu = new NativeMenuItem("Focus timer") { Menu = new NativeMenu() };
        var workspaceMenu = new NativeMenuItem("Workspace") { Menu = new NativeMenu() };
        var settings = new NativeMenuItem("Open settings...");
        var logs = new NativeMenuItem("Open log folder");
        var quit = new NativeMenuItem("Exit VEIL");
        var menu = new NativeMenu();
        menu.Add(status);
        if (runtime.Warnings.Count > 0 || !runtime.EmergencyReady)
            menu.Add(new NativeMenuItem(runtime.EmergencyReady ? "Some shortcuts are unavailable (see log)" : "Emergency Recovery shortcut unavailable") { IsEnabled = false });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(toggle);
        menu.Add(timerMenu);
        menu.Add(workspaceMenu);
        menu.Add(settings);
        menu.Add(logs);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quit);

        var icon = new TrayIcon { Menu = menu, IsVisible = true };
        using (var s = AssetLoader.Open(new Uri("avares://VEIL.Presentation/icons/veil-tray.png"))) icon.Icon = new WindowIcon(new Bitmap(s));

        void Refresh()
        {
            var active = runtime.Machine.State is FocusState.Focused or FocusState.SelectorOpen;
            status.Header = active ? "VEIL - Focus Mode is ON" : "VEIL - idle (Focus Mode off)";
            toggle.Header = active ? "Turn Focus Off" + Chord(HotkeyCommand.FocusOff) : "Turn Focus On" + Chord(HotkeyCommand.FocusOn);
            toggle.IsEnabled = active || runtime.EmergencyReady;
            icon.ToolTipText = active ? "VEIL - Focus Mode is on" : "VEIL - ready (idle)";
        }
        // Focus timer: presets start a session (entering Focus Mode if needed); everything runs off the UI thread.
        var stopTimer = new NativeMenuItem("Stop timer");
        foreach (var minutes in FocusTimer.Presets)
        {
            var item = new NativeMenuItem($"{minutes} minutes");
            var m = minutes;
            item.Click += (_, _) => Task.Run(() => log.Write(runtime.StartTimer(m).Message));
            timerMenu.Menu!.Add(item);
        }
        timerMenu.Menu!.Add(new NativeMenuItemSeparator());
        timerMenu.Menu!.Add(stopTimer);
        stopTimer.Click += (_, _) => Task.Run(() => log.Write(runtime.SessionTimer.Stop().Message));

        void RebuildWorkspaces()
        {
            workspaceMenu.Menu!.Items.Clear();
            IReadOnlyList<string> names;
            try { names = runtime.Profiles.Names; }
            catch (Exception) { names = []; } // an unreadable configuration must not break the tray menu
            var active = runtime.Profiles.ActiveName;
            NativeMenuItem Choice(string header, string? name)
            {
                var item = new NativeMenuItem(header)
                {
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = string.Equals(active, name, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) => Task.Run(() => log.Write(runtime.Profiles.Select(name).Message));
                return item;
            }
            workspaceMenu.Menu.Add(Choice("All allowed applications", null));
            foreach (var n in names) workspaceMenu.Menu.Add(Choice(n, n));
            workspaceMenu.IsEnabled = names.Count > 0;
            workspaceMenu.Header = active is null ? "Workspace" : $"Workspace: {active}";
        }

        void RefreshTimer()
        {
            var snap = runtime.SessionTimer.Snapshot();
            var running = snap.Phase == TimerPhase.Running;
            stopTimer.IsEnabled = running;
            timerMenu.Header = running ? $"Focus timer ({(int)Math.Ceiling(snap.Remaining.TotalMinutes)} min left)" : "Focus timer";
            timerMenu.IsEnabled = runtime.EmergencyReady;
        }

        void RefreshAll() { Refresh(); RefreshTimer(); RebuildWorkspaces(); }
        RefreshAll();
        runtime.Machine.StateChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
        runtime.SessionTimer.Changed += () => Dispatcher.UIThread.Post(RefreshAll);
        runtime.Profiles.Changed += () => Dispatcher.UIThread.Post(RefreshAll);
        menu.NeedsUpdate += (_, _) => RefreshAll(); // opened: bring the countdown and workspace list up to date

        toggle.Click += (_, _) => Task.Run(() => log.Write(runtime.ToggleFocus().Message));
        settings.Click += (_, _) => openSettings();
        logs.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(LogDirectory); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { log.Write("Could not open the log folder: " + ex.Message); }
        };
        quit.Click += (_, _) => exit();
        icon.Clicked += (_, _) => openSettings();   // left click opens settings; the menu is on right click
        return icon;
    }
}

/// <summary>Small rolling text log for the windowless agent (there is no console to write to).</summary>
internal sealed class AgentLog(string path, long maxBytes = 256 * 1024)
{
    private readonly object _gate = new();

    public void Write(string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > maxBytes) File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* logging must never break the agent */ }
        }
    }
}
