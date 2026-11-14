using System.Text.Json;
using System.Text.Json.Serialization;
using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Infrastructure;

public sealed class ConfigurationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// JSON configuration with strict validation, size limit and atomic writes (architecture 9, security 5).
/// Import never overwrites valid configuration when the incoming file is rejected. FR-ADM-008.
/// </summary>
public sealed class JsonConfigurationStore : IConfigurationStore
{
    public const long MaxFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonConfigurationStore(string directory, IProtectedStorage? protectedStorage = null, string fileName = "config.json")
    {
        protectedStorage?.EnsureProtectedDirectory(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, fileName);
    }

    public string FilePath => _path;

    /// <summary>Missing file yields safe defaults. A corrupt file is NOT silently replaced (architecture 11).</summary>
    public VeilConfiguration Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return new VeilConfiguration();
            return Parse(File.ReadAllBytes(_path), _path);
        }
    }

    public void Save(VeilConfiguration configuration)
    {
        Validate(configuration);
        lock (_gate) AtomicWrite(_path, JsonSerializer.SerializeToUtf8Bytes(configuration, Options));
    }

    /// <summary>Writes a portable backup. Contains no credentials by construction.</summary>
    public void Export(string destination)
    {
        lock (_gate) AtomicWrite(destination, JsonSerializer.SerializeToUtf8Bytes(Load(), Options));
    }

    /// <summary>Validates then applies a backup. On rejection the live configuration is untouched.</summary>
    public void Import(string source)
    {
        var info = new FileInfo(source);
        if (!info.Exists) throw new ConfigurationException("Import file does not exist.");
        if (info.Length > MaxFileBytes) throw new ConfigurationException("Import file exceeds the size limit.");
        var incoming = Parse(File.ReadAllBytes(source), source);
        Save(incoming);
    }

    private static VeilConfiguration Parse(byte[] bytes, string origin)
    {
        if (bytes.LongLength > MaxFileBytes) throw new ConfigurationException($"{origin} exceeds the size limit.");
        // Windows editors commonly save UTF-8 with a BOM, which System.Text.Json rejects.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
        VeilConfiguration? cfg;
        try { cfg = JsonSerializer.Deserialize<VeilConfiguration>(bytes, Options); }
        catch (JsonException ex) { throw new ConfigurationException($"{origin} is not valid VEIL configuration: {ex.Message}", ex); }
        if (cfg is null) throw new ConfigurationException($"{origin} is empty.");
        // Shortcuts added by a newer VEIL appear on upgrade; stored bindings are never altered.
        if (cfg.Hotkeys is not null) cfg = cfg with { Hotkeys = DefaultHotkeys.WithMissingDefaults(cfg.Hotkeys.Where(h => h.Command != HotkeyCommand.Selector).ToList()) };
        Validate(cfg);
        return cfg;
    }

    public static void Validate(VeilConfiguration cfg)
    {
        if (cfg.SchemaVersion != VeilConfiguration.CurrentSchemaVersion)
            throw new ConfigurationException($"Unsupported schema version {cfg.SchemaVersion}.");
        if (cfg.Rules is null || cfg.Hotkeys is null || cfg.Preferences is null)
            throw new ConfigurationException("Configuration is missing required sections.");
        if (cfg.Rules.Count > 1000) throw new ConfigurationException("Too many application rules.");
        foreach (var r in cfg.Rules)
            if (string.IsNullOrWhiteSpace(r.ApplicationId) || r.ApplicationId.Length > 1024)
                throw new ConfigurationException("An application rule has an invalid identifier.");
        var problems = DefaultHotkeys.Validate(cfg.Hotkeys);
        if (problems.Count > 0) throw new ConfigurationException(string.Join(" ", problems));
        if (WorkspaceProfile.Validate(cfg.Profiles) is { } profileProblem) throw new ConfigurationException(profileProblem);
        if (cfg.Preferences.DefaultTimerMinutes is < 1 or > FocusTimer.MaxMinutes)
            throw new ConfigurationException($"The default focus timer must be between 1 and {FocusTimer.MaxMinutes} minutes.");
        if (cfg.SuperAdmin is { } a && (string.IsNullOrWhiteSpace(a.Principal) || string.IsNullOrWhiteSpace(a.Platform)))
            throw new ConfigurationException("Super Admin identity is incomplete.");
    }

    private static void AtomicWrite(string path, byte[] content)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = System.IO.Path.Combine(dir, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tmp, content);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
