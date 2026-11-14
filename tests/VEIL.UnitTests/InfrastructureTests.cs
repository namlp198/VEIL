using System.Text.Json;
using VEIL.Domain;
using VEIL.Infrastructure;
using VEIL.Platform.Abstractions;
using VEIL.Platform.Linux.X11;

namespace VEIL.UnitTests;

public class JsonConfigurationStoreTests
{
    [Fact]
    public void Missing_file_yields_safe_defaults()
    {
        using var d = new TempDir();
        var cfg = new JsonConfigurationStore(d.Path).Load();
        Assert.Null(cfg.SuperAdmin);
        Assert.Empty(cfg.Rules);
        Assert.Empty(DefaultHotkeys.Validate(cfg.Hotkeys));
    }

    [Fact]
    public void Round_trips_and_leaves_no_temp_files()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        store.Save(new VeilConfiguration
        {
            SuperAdmin = new SuperAdminIdentity("Windows", "S-1-5-21-1", "alice"),
            Rules = [new ApplicationRule("c:\\ide.exe", "IDE", Transient: TransientWindowPolicy.FollowParent)],
        });
        store.Save(store.Load() with { Preferences = new FocusPreferences(ReducedMotion: true) });
        var loaded = store.Load();
        Assert.Equal("S-1-5-21-1", loaded.SuperAdmin!.Principal);
        Assert.Equal(TransientWindowPolicy.FollowParent, loaded.Rules.Single().Transient);
        Assert.True(loaded.Preferences.ReducedMotion);
        Assert.Empty(Directory.GetFiles(d.Path, "*.tmp"));
    }

    [Fact]
    public void Corrupt_file_is_reported_and_not_overwritten()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        File.WriteAllText(store.FilePath, "{ not json");
        Assert.Throws<ConfigurationException>(store.Load);
        Assert.Equal("{ not json", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void An_old_config_that_still_binds_the_removed_Selector_loads_and_drops_it()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        File.WriteAllText(store.FilePath, "{\"SchemaVersion\":1,\"Hotkeys\":[{\"Command\":\"Selector\",\"Chord\":\"Ctrl+Alt+L\",\"Enabled\":true},{\"Command\":\"EmergencyRecovery\",\"Chord\":\"Ctrl+Alt+Esc\",\"Enabled\":true}]}");
        var cfg = store.Load();
        Assert.DoesNotContain(cfg.Hotkeys, h => h.Command == HotkeyCommand.Selector);
        Assert.Contains(cfg.Hotkeys, h => h.Command == HotkeyCommand.EmergencyRecovery);
    }

    [Fact]
    public void Config_never_contains_credential_fields()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        store.Save(new VeilConfiguration { SuperAdmin = new SuperAdminIdentity("Windows", "S-1", "a"), LockPasswordHash = LockPassword.Hash("hunter2-clear") });
        var text = File.ReadAllText(store.FilePath);
        // The only credential-like field is the salted lock-password hash; the clear password is never written.
        Assert.DoesNotContain("hunter2-clear", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text.Replace("lockPasswordHash", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // malicious config import: valid config untouched on rejection
    public void Rejected_import_does_not_overwrite_valid_configuration()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        store.Save(new VeilConfiguration { Rules = [new ApplicationRule("c:\\keep.exe", "keep")] });
        var bad = Path.Combine(d.Path, "bad.json");

        foreach (var content in new[]
        {
            "garbage",
            "{\"SchemaVersion\":99}",
            "{\"SchemaVersion\":1,\"Unexpected\":true}",
            "{\"SchemaVersion\":1,\"Rules\":[{\"ApplicationId\":\"\",\"DisplayName\":\"x\"}]}",
            "{\"SchemaVersion\":1,\"Hotkeys\":[{\"Command\":\"FocusOn\",\"Chord\":\"V\"}]}",
        })
        {
            File.WriteAllText(bad, content);
            Assert.Throws<ConfigurationException>(() => store.Import(bad));
            Assert.Equal("c:\\keep.exe", store.Load().Rules.Single().ApplicationId);
        }
    }

    [Fact]
    public void Utf8_bom_is_tolerated()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        File.WriteAllText(store.FilePath, "{\"SchemaVersion\":1,\"Rules\":[{\"ApplicationId\":\"c:/a.exe\",\"DisplayName\":\"a\"}]}", new System.Text.UTF8Encoding(true));
        Assert.Single(store.Load().Rules);
    }

    [Fact]
    public void Oversized_import_is_rejected()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        var big = Path.Combine(d.Path, "big.json");
        File.WriteAllBytes(big, new byte[JsonConfigurationStore.MaxFileBytes + 1]);
        Assert.Throws<ConfigurationException>(() => store.Import(big));
    }

    [Fact]
    public void Export_then_import_restores_configuration()
    {
        using var d = new TempDir();
        var store = new JsonConfigurationStore(d.Path);
        store.Save(new VeilConfiguration { Rules = [new ApplicationRule("c:\\a.exe", "a")] });
        var backup = Path.Combine(d.Path, "backup.json");
        store.Export(backup);
        store.Save(new VeilConfiguration());
        store.Import(backup);
        Assert.Single(store.Load().Rules);
    }

    [Fact]
    public void Protected_storage_hook_is_invoked()
    {
        using var d = new TempDir();
        var hook = new RecordingStorage();
        _ = new JsonConfigurationStore(Path.Combine(d.Path, "sub"), hook);
        Assert.Single(hook.Paths);
    }

    private sealed class RecordingStorage : IProtectedStorage
    {
        public List<string> Paths { get; } = [];
        public void EnsureProtectedDirectory(string path) => Paths.Add(path);
    }
}

public class FileAuditLogTests
{
    [Fact]
    public void Appends_and_reads_events()
    {
        using var d = new TempDir();
        var log = new FileAuditLog(d.Path);
        log.Write(new AuditEvent(DateTimeOffset.UtcNow, "S-1", "admin.authorize", "Granted"));
        log.Write(new AuditEvent(DateTimeOffset.UtcNow, "S-1", "policy.update", "Denied"));
        Assert.Equal(["admin.authorize", "policy.update"], log.ReadCurrent().Select(e => e.Action));
    }

    [Fact]
    public void Rotates_with_bounded_retention()
    {
        using var d = new TempDir();
        var log = new FileAuditLog(d.Path, maxBytes: 200, retainedFiles: 2);
        for (var i = 0; i < 60; i++) log.Write(new AuditEvent(DateTimeOffset.UtcNow, "S-1", "action" + i, "ok"));
        var files = Directory.GetFiles(d.Path, "audit*.jsonl");
        Assert.True(files.Length <= 3, $"expected at most 3 files, found {files.Length}");
    }
}

public class FileAuditLogFallbackTests
{
    [Fact]
    public void Unwritable_primary_falls_back_and_never_throws()
    {
        using var d = new TempDir();
        var blocker = Path.Combine(d.Path, "protected");
        File.WriteAllText(blocker, "a file where the directory should be"); // CreateDirectory fails with IOException
        var fallback = Path.Combine(d.Path, "user-audit");
        var log = new FileAuditLog(blocker, fallbackDirectory: fallback);
        log.Write(new AuditEvent(DateTimeOffset.UtcNow, "S-1", "admin.authorize", "DeniedWrongIdentity"));
        Assert.Single(File.ReadAllLines(Path.Combine(fallback, "audit.jsonl")));
    }

    [Fact]
    public void Unwritable_everywhere_is_swallowed_not_thrown()
    {
        using var d = new TempDir();
        var blocker = Path.Combine(d.Path, "x");
        File.WriteAllText(blocker, "");
        new FileAuditLog(blocker, fallbackDirectory: blocker).Write(new AuditEvent(DateTimeOffset.UtcNow, "S-1", "a", "b"));
    }
}

public class LinuxSessionInspectorTests
{
    private static SessionInfo Inspect(params (string k, string v)[] env)
    {
        var map = env.ToDictionary(e => e.k, e => e.v);
        return new LinuxSessionInspector(k => map.GetValueOrDefault(k)).Inspect();
    }

    [Fact] // AT-012
    public void Wayland_is_reported_as_unsupported_not_pretended()
    {
        var s = Inspect(("XDG_SESSION_TYPE", "wayland"), ("WAYLAND_DISPLAY", "wayland-0"));
        Assert.Equal(SessionSupport.Unsupported, s.Support);
        Assert.Contains("X11", s.Notice);
    }

    [Fact]
    public void Wayland_with_xwayland_is_restricted()
    {
        var s = Inspect(("XDG_SESSION_TYPE", "wayland"), ("DISPLAY", ":0"));
        Assert.Equal(SessionSupport.Restricted, s.Support);
    }

    [Fact]
    public void X11_is_supported()
    {
        Assert.Equal(SessionSupport.Supported, Inspect(("XDG_SESSION_TYPE", "x11"), ("DISPLAY", ":0")).Support);
        Assert.Equal(SessionSupport.Supported, Inspect(("DISPLAY", ":0")).Support);
    }

    [Fact]
    public void No_graphical_session_is_unsupported() =>
        Assert.Equal(SessionSupport.Unsupported, Inspect().Support);
}

public class InstallInfoTests
{
    private static InstallInfo? Read(string json, bool bom = false)
    {
        using var d = new TempDir();
        File.WriteAllText(Path.Combine(d.Path, InstallInfo.FileName), json, new System.Text.UTF8Encoding(bom));
        return InstallInfo.TryRead(d.Path);
    }

    [Fact]
    public void Missing_marker_means_not_installed_by_the_installer()
    {
        using var d = new TempDir();
        Assert.Null(InstallInfo.TryRead(d.Path));
    }

    [Fact]
    public void Reads_a_user_trial_install_including_a_bom()
    {
        var info = Read("{\"Mode\":\"User\",\"ConfigDir\":\"C:/Users/x/AppData/Local/VEIL/config\",\"Version\":\"1.0.0\"}", bom: true);
        Assert.Equal(InstallMode.User, info!.Mode);
        Assert.Equal("1.0.0", info.Version);
        Assert.Equal("C:/Users/x/AppData/Local/VEIL/config", info.ConfigDir);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Mode\":\"User\",\"ConfigDir\":\"relative/path\"}")]
    [InlineData("")]
    public void Malformed_or_relative_markers_are_ignored(string json) => Assert.Null(Read(json));

    [Fact]
    public void Unknown_mode_falls_back_to_machine()
    {
        Assert.Equal(InstallMode.Machine, Read("{\"Mode\":\"Weird\"}")!.Mode);
    }

    [Fact]
    public void Oversized_marker_is_ignored()
    {
        using var d = new TempDir();
        File.WriteAllText(Path.Combine(d.Path, InstallInfo.FileName), new string(' ', 20_000));
        Assert.Null(InstallInfo.TryRead(d.Path));
    }

    [Fact]
    public void Environment_override_wins_and_is_unprotected()
    {
        var (dir, prot) = InstallInfo.ResolveConfigDir("D:/dev/cfg", new InstallInfo(InstallMode.User, "C:/u/cfg", null), "C:/ProgramData/VEIL");
        Assert.Equal(("D:/dev/cfg", false), (dir, prot));
    }

    [Fact]
    public void User_install_uses_its_own_unprotected_folder()
    {
        var (dir, prot) = InstallInfo.ResolveConfigDir(null, new InstallInfo(InstallMode.User, "C:/u/cfg", null), "C:/ProgramData/VEIL");
        Assert.Equal(("C:/u/cfg", false), (dir, prot));
    }

    [Fact]
    public void Machine_install_and_no_marker_use_the_protected_machine_default()
    {
        Assert.Equal(("C:/ProgramData/VEIL", true), InstallInfo.ResolveConfigDir(null, new InstallInfo(InstallMode.Machine, null, "1"), "C:/ProgramData/VEIL"));
        Assert.Equal(("C:/ProgramData/VEIL", true), InstallInfo.ResolveConfigDir(null, null, "C:/ProgramData/VEIL"));
        Assert.Equal(("C:/ProgramData/VEIL", true), InstallInfo.ResolveConfigDir("  ", null, "C:/ProgramData/VEIL"));
    }
}
