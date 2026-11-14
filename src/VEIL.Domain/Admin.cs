namespace VEIL.Domain;

/// <summary>
/// Stable OS principal reference (SID on Windows, UID on Linux). Equality of authority ignores
/// <see cref="DisplayName"/> so renaming an account never grants or removes authority (security design 2).
/// </summary>
public sealed record SuperAdminIdentity(string Platform, string Principal, string DisplayName)
{
    public bool IsSamePrincipal(SuperAdminIdentity? other) =>
        other is not null
        && string.Equals(Platform, other.Platform, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Principal, other.Principal, StringComparison.OrdinalIgnoreCase);
}

public sealed record AuditEvent(DateTimeOffset Timestamp, string Identity, string Action, string Result);

public sealed record FocusPreferences(bool ReducedMotion = false, bool AutoEnterFocusOnStart = false, bool ShowClock = true,
    bool SelectorOnPointerDisplay = false, bool ShowSystemStatus = true, int DefaultTimerMinutes = 25);

/// <summary>Persisted machine configuration. Contains no clear-text passwords or tokens (only a salted hash of the lock password).</summary>
public sealed record VeilConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public SuperAdminIdentity? SuperAdmin { get; init; }
    public IReadOnlyList<ApplicationRule> Rules { get; init; } = [];
    public IReadOnlyList<HotkeyBinding> Hotkeys { get; init; } = DefaultHotkeys.Create();
    public FocusPreferences Preferences { get; init; } = new();
    /// <summary>Added after schema 1 shipped: older files simply have none, which is valid.</summary>
    public IReadOnlyList<WorkspaceProfile> Profiles { get; init; } = [];
    /// <summary>Salted hash of the password that ends the full lock (see <see cref="LockPassword"/>); null means the default.</summary>
    public string? LockPasswordHash { get; init; }
}
