using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.UnitTests;

internal sealed class FakeDisplays : IDisplayService
{
    public List<DisplayInfo> Current { get; } = [];
    public event EventHandler<DisplayTopologyChangedEventArgs>? TopologyChanged;

    public FakeDisplays(int count) { for (var i = 0; i < count; i++) Current.Add(Make(i)); }

    public static DisplayInfo Make(int i) => new($"D{i}", $"Display {i}", new WindowBounds(i * 1920, 0, 1920, 1080), 1.0, i == 0);
    public IReadOnlyList<DisplayInfo> GetDisplays() => Current.ToList();
    public void Raise() => TopologyChanged?.Invoke(this, new DisplayTopologyChangedEventArgs(GetDisplays()));
}

internal sealed class FakeOverlayHost : IOverlayHost
{
    public List<FakeSurface> Created { get; } = [];
    public string? FailOnDisplay { get; set; }

    public IOverlaySurface Create(DisplayInfo display)
    {
        if (display.Id == FailOnDisplay) throw new InvalidOperationException("boom");
        var s = new FakeSurface(display.Id);
        Created.Add(s);
        return s;
    }

    public int Live => Created.Count(s => !s.Disposed);
}

internal sealed class FakeSurface(string id) : IOverlaySurface
{
    public string DisplayId => id;
    public bool Disposed { get; private set; }
    public int DisposeCalls { get; private set; }
    public bool ThrowOnDispose { get; set; }
    public bool IsHealthy => !Disposed;

    public void Dispose()
    {
        DisposeCalls++;
        Disposed = true;
        if (ThrowOnDispose) throw new InvalidOperationException("dispose failed");
    }
}

internal sealed class FakeWindows : IWindowService
{
    public List<WindowInfo> Windows { get; } = [];
    public HashSet<string> Raised { get; } = [];
    public bool RevealFails { get; set; }

    public IReadOnlyList<WindowInfo> GetWindows() => Windows.ToList();
    public bool Reveal(string id) { if (RevealFails) return false; Raised.Add(id); return true; }
    public bool Hide(string id) => Raised.Remove(id) || Windows.Any(w => w.RuntimeId == id);

    public static WindowInfo Win(string id, string app, string title = "t", WindowKind kind = WindowKind.Normal, bool minimized = false) =>
        new(id, "1", app, title, "D0", new WindowBounds(0, 0, 100, 100), !minimized, minimized, false, kind);
}

internal sealed class FakeAuth(SuperAdminIdentity current, bool isOsAdmin, AuthenticationStatus status = AuthenticationStatus.Verified) : IAuthenticationProvider
{
    public SuperAdminIdentity GetCurrentIdentity() => current;
    public bool IsOsAdministrator() => isOsAdmin;
    public AuthenticationResult Authenticate() => new(status, status == AuthenticationStatus.Verified ? current : null, status.ToString());
}

internal sealed class MemoryStore : IConfigurationStore
{
    public VeilConfiguration Config { get; set; } = new();
    public VeilConfiguration Load() => Config;
    public void Save(VeilConfiguration configuration) => Config = configuration;
}

internal sealed class MemoryAudit : IAuditLog
{
    public List<AuditEvent> Events { get; } = [];
    public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
}

/// <summary>Time that only moves when a test says so.</summary>
internal sealed class ManualTime : TimeProvider
{
    private long _ticks;
    private DateTimeOffset _utc = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => 1000;                 // 1 tick = 1 ms
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public override DateTimeOffset GetUtcNow() => _utc;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by)
    {
        Interlocked.Add(ref _ticks, (long)by.TotalMilliseconds);
        _utc += by;
    }
}

internal sealed class MemoryUserState : IUserStateStore
{
    public string? Active { get; set; }
    public string? LoadActiveWorkspace() => Active;
    public void SaveActiveWorkspace(string? name) => Active = name;
}

internal sealed class MemorySessionLog : IFocusSessionLog
{
    public List<FocusSessionRecord> Records { get; } = [];
    public void Append(FocusSessionRecord record) => Records.Add(record);
    public IReadOnlyList<FocusSessionRecord> ReadRecent(int max) => Records.AsEnumerable().Reverse().Take(max).ToList();
}

/// <summary>Resolves "paths" from a table, so tests never touch the file system.</summary>
internal sealed class FakeResolver(Dictionary<string, ResolveResult> table) : IApplicationResolver
{
    public ResolveResult Resolve(string path) =>
        table.TryGetValue(path, out var r) ? r : ResolveResult.Fail("That file does not exist.");

    public static ResolveResult App(string id, string name) => ResolveResult.Ok(new ResolvedApplication(id, name));
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "veil-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { /* best effort */ } }
}
