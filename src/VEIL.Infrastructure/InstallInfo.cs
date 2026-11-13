using System.Text.Json;

namespace VEIL.Infrastructure;

public enum InstallMode { Machine, User }

/// <summary>
/// What the installer recorded next to the executables (<c>install.json</c>): the install scope and, for the per-user trial
/// install, where the configuration lives. Machine installs use the protected ProgramData location and need no override.
/// </summary>
public sealed record InstallInfo(InstallMode Mode, string? ConfigDir, string? Version)
{
    public const string FileName = "install.json";
    private const long MaxBytes = 16 * 1024;

    /// <summary>Returns null when the file is absent, oversized or malformed (a missing marker means "not installed by the installer").</summary>
    public static InstallInfo? TryRead(string appDirectory)
    {
        try
        {
            var path = Path.Combine(appDirectory, FileName);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return null;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..];
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            var mode = root.TryGetProperty("Mode", out var m) && Enum.TryParse<InstallMode>(m.GetString(), true, out var parsed) ? parsed : InstallMode.Machine;
            var config = root.TryGetProperty("ConfigDir", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var version = root.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (config is not null && !Path.IsPathFullyQualified(config)) return null; // refuse relative paths
            return new InstallInfo(mode, string.IsNullOrWhiteSpace(config) ? null : config, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Configuration directory precedence: explicit <c>VEIL_CONFIG_DIR</c> (developer override), then the installer's marker,
    /// then the machine default. <see cref="IsProtected"/> is true only for the machine default and a machine install.
    /// </summary>
    public static (string Directory, bool IsProtected) ResolveConfigDir(string? environmentOverride, InstallInfo? install, string machineDefault)
    {
        if (!string.IsNullOrWhiteSpace(environmentOverride)) return (environmentOverride, false);
        if (install is { Mode: InstallMode.User, ConfigDir: { } dir }) return (dir, false);
        return (install?.ConfigDir ?? machineDefault, true);
    }
}
