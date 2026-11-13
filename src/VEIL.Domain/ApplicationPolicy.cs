namespace VEIL.Domain;

public enum TransientWindowPolicy { Hide, FollowParent }

/// <summary>Allow-list entry (FR-APP-001). <see cref="ApplicationId"/> is a stable identity such as an executable path.</summary>
public sealed record ApplicationRule(
    string ApplicationId,
    string DisplayName,
    bool Enabled = true,
    TransientWindowPolicy Transient = TransientWindowPolicy.Hide);

public sealed record PolicyDecision(bool IsAllowed, string Reason);

/// <summary>
/// Default-deny policy evaluation, independent of OS enumeration (architecture 5.4).
/// FR-APP-002, FR-APP-005.
/// </summary>
public sealed class ApplicationPolicy
{
    private readonly Dictionary<string, ApplicationRule> _rules;
    private readonly WorkspaceProfile? _profile;

    /// <param name="profile">
    /// The active workspace, if any. It only narrows the allow-list: an application must be allowed by a rule AND belong to
    /// the workspace. A profile can never allow something the Super Admin's list does not.
    /// </param>
    public ApplicationPolicy(IEnumerable<ApplicationRule> rules, WorkspaceProfile? profile = null)
    {
        _rules = new Dictionary<string, ApplicationRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules) _rules[r.ApplicationId] = r;
        _profile = profile;
    }

    public IReadOnlyCollection<ApplicationRule> Rules => _rules.Values;

    public PolicyDecision Evaluate(WindowInfo window)
    {
        if (!_rules.TryGetValue(window.ApplicationId, out var rule))
            return new(false, "Application is not on the allow-list.");
        if (!rule.Enabled)
            return new(false, "Application rule is disabled.");
        if (_profile is not null && !_profile.Includes(window.ApplicationId))
            return new(false, $"Application is not part of the '{_profile.Name}' workspace.");
        return window.Kind switch
        {
            WindowKind.Normal => new(true, "Allowed application."),
            WindowKind.Unknown => new(false, "Unknown window type is hidden by default."),
            _ when rule.Transient == TransientWindowPolicy.FollowParent => new(true, "Transient window follows its allowed application."),
            _ => new(false, $"{window.Kind} window is hidden by transient-window policy."),
        };
    }
}
