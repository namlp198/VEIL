using System.Text.Json;
using VEIL.Application;
using VEIL.Platform.Abstractions;

namespace VEIL.Infrastructure;

/// <summary>
/// Focus sessions as one JSON object per line in the user's own profile. Append-only and tolerant: a damaged line is
/// skipped, a write error is swallowed (a full disk must never interrupt a focus session).
/// </summary>
public sealed class FileFocusSessionLog(string path, int keepLines = 2000) : IFocusSessionLog
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly object _gate = new();

    public void Append(FocusSessionRecord record)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.AppendAllText(path, JsonSerializer.Serialize(record, Options) + Environment.NewLine);
                Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }

    public IReadOnlyList<FocusSessionRecord> ReadRecent(int max)
    {
        lock (_gate)
        {
            var result = new List<FocusSessionRecord>();
            try
            {
                if (!File.Exists(path)) return result;
                foreach (var line in File.ReadLines(path).Reverse().Take(Math.Max(max, 0) * 2 + 20))
                {
                    if (result.Count >= max) break;
                    try { if (JsonSerializer.Deserialize<FocusSessionRecord>(line, Options) is { } r) result.Add(r); }
                    catch (JsonException) { /* skip a damaged line */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable: show nothing */ }
            return result;
        }
    }

    private void Trim()
    {
        var info = new FileInfo(path);
        if (info.Length < 512 * 1024) return;
        var lines = File.ReadAllLines(path);
        if (lines.Length > keepLines) File.WriteAllLines(path, lines[^keepLines..]);
    }
}

/// <summary>The user's remembered workspace and dock layout, in their own profile (not the protected machine configuration).</summary>
public sealed class JsonUserStateStore(string path) : IUserStateStore
{
    private const int MaxBytes = 64 * 1024, MaxPinned = 24;
    private sealed record State(string? ActiveWorkspace, string? DockMode = null, List<string>? PinnedApps = null);
    private readonly object _gate = new();

    private State Read()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return new State(null);
            return JsonSerializer.Deserialize<State>(File.ReadAllBytes(path)) ?? new State(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new State(null); }
    }

    private void Write(State state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }

    public string? LoadActiveWorkspace()
    {
        lock (_gate)
        {
            var name = Read().ActiveWorkspace;
            return string.IsNullOrWhiteSpace(name) || name.Length > 200 ? null : name;
        }
    }

    public void SaveActiveWorkspace(string? name)
    {
        lock (_gate) Write(Read() with { ActiveWorkspace = name });
    }

    public DockSettings LoadDock()
    {
        lock (_gate)
        {
            var s = Read();
            var mode = Enum.TryParse<DockMode>(s.DockMode, out var m) ? m : DockMode.Panel;
            return new DockSettings(mode, (s.PinnedApps ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 1024).Take(MaxPinned).ToList());
        }
    }

    public void SaveDock(DockSettings settings)
    {
        lock (_gate) Write(Read() with { DockMode = settings.Mode.ToString(), PinnedApps = (settings.PinnedApps ?? []).Take(MaxPinned).ToList() });
    }
}
