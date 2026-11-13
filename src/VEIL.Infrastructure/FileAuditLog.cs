using System.Text.Json;
using VEIL.Application;
using VEIL.Domain;

namespace VEIL.Infrastructure;

/// <summary>
/// Append-only JSONL audit log with size-based rotation and bounded retention (security design 7).
/// Records only action and outcome; never secrets, window titles or content. FR-ADM-007.
/// </summary>
public sealed class FileAuditLog(string directory, long maxBytes = 512 * 1024, int retainedFiles = 5, string? fallbackDirectory = null) : IAuditLog
{
    private readonly object _gate = new();
    private string LogPath => Path.Combine(directory, "audit.jsonl");

    /// <summary>
    /// Auditing must never change an authorization outcome. A standard user cannot write to the protected directory
    /// (that is the point of the ACL), so denied attempts fall back to a per-user location; if that also fails the
    /// event is dropped rather than crashing the caller.
    /// </summary>
    public void Write(AuditEvent auditEvent)
    {
        lock (_gate)
        {
            if (TryAppend(directory, auditEvent)) return;
            if (fallbackDirectory is not null) TryAppend(fallbackDirectory, auditEvent);
        }
    }

    private bool TryAppend(string dir, AuditEvent auditEvent)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Rotate(dir);
            File.AppendAllText(Path.Combine(dir, "audit.jsonl"), JsonSerializer.Serialize(auditEvent) + Environment.NewLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<AuditEvent> ReadCurrent()
    {
        lock (_gate)
        {
            if (!File.Exists(LogPath)) return [];
            return File.ReadLines(LogPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonSerializer.Deserialize<AuditEvent>(l)!)
                .ToList();
        }
    }

    private void Rotate(string directory)
    {
        var LogPath = Path.Combine(directory, "audit.jsonl");
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < maxBytes) return;
        var oldest = Path.Combine(directory, $"audit.{retainedFiles}.jsonl");
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var i = retainedFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(directory, $"audit.{i}.jsonl");
            if (File.Exists(src)) File.Move(src, Path.Combine(directory, $"audit.{i + 1}.jsonl"));
        }
        File.Move(LogPath, Path.Combine(directory, "audit.1.jsonl"));
    }
}
