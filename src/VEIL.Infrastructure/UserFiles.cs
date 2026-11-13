using System.Text.Json;
using VEIL.Application;

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

/// <summary>The user's remembered workspace, in their own profile (not the protected machine configuration).</summary>
public sealed class JsonUserStateStore(string path) : IUserStateStore
{
    private sealed record State(string? ActiveWorkspace);

    public string? LoadActiveWorkspace()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            var name = JsonSerializer.Deserialize<State>(File.ReadAllBytes(path))?.ActiveWorkspace;
            return string.IsNullOrWhiteSpace(name) || name.Length > 200 ? null : name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void SaveActiveWorkspace(string? name)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new State(name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
