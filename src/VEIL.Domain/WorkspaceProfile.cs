namespace VEIL.Domain;

/// <summary>
/// A named working set (UI/UX 4.1: Coding, Engineering, AI Lab, Research). It can only narrow what the Super Admin's
/// allow-list already permits, never widen it, so a profile is a convenience and not a way around the policy.
/// </summary>
public sealed record WorkspaceProfile(string Name, IReadOnlyList<string> ApplicationIds)
{
    public const int MaxProfiles = 50;
    public const int MaxNameLength = 40;

    public bool Includes(string applicationId) =>
        ApplicationIds.Any(id => string.Equals(id, applicationId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null when valid, otherwise what is wrong (shown to the user and used by configuration validation).</summary>
    public static string? Validate(IReadOnlyList<WorkspaceProfile>? profiles)
    {
        if (profiles is null) return "Workspace profiles are missing.";
        if (profiles.Count > MaxProfiles) return $"At most {MaxProfiles} workspace profiles can be defined.";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in profiles)
        {
            if (p is null || string.IsNullOrWhiteSpace(p.Name)) return "A workspace profile needs a name.";
            if (p.Name.Length > MaxNameLength) return $"Workspace names are limited to {MaxNameLength} characters.";
            if (!names.Add(p.Name.Trim())) return $"There are two workspace profiles named '{p.Name}'.";
            if (p.ApplicationIds is null) return $"Workspace '{p.Name}' has no application list.";
            if (p.ApplicationIds.Count > 1000) return $"Workspace '{p.Name}' lists too many applications.";
            if (p.ApplicationIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 1024))
                return $"Workspace '{p.Name}' has an invalid application identifier.";
        }
        return null;
    }
}
