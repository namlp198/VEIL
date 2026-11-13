using VEIL.Domain;

namespace VEIL.Application;

/// <summary>Where the user's own choices live between runs (the active workspace). Never holds policy or secrets.</summary>
public interface IUserStateStore
{
    string? LoadActiveWorkspace();
    void SaveActiveWorkspace(string? name);
}

/// <summary>
/// Tracks which workspace profile is active and cycles through them (UI/UX 4.1). The sequence is "all allowed
/// applications", then each profile in order, so the user can always return to the full list from the keyboard. Switching
/// is a per-user choice, so it never touches the machine configuration.
/// </summary>
public sealed class ProfileManager
{
    private readonly Func<VeilConfiguration> _config;
    private readonly IUserStateStore? _state;
    private readonly object _gate = new();
    private string? _active;

    public ProfileManager(Func<VeilConfiguration> config, IUserStateStore? state = null)
    {
        _config = config;
        _state = state;
        try { _active = state?.LoadActiveWorkspace(); } catch { _active = null; }
    }

    public event Action? Changed;

    /// <summary>The active profile, or null for "all allowed applications". A stored name that no longer exists counts as none.</summary>
    public WorkspaceProfile? Active
    {
        get
        {
            string? name;
            lock (_gate) name = _active;
            return name is null ? null : Find(_config().Profiles, name);
        }
    }

    public string? ActiveName => Active?.Name;

    public IReadOnlyList<string> Names => _config().Profiles.Select(p => p.Name).ToList();

    private static int IndexOf(List<string> names, string name) => names.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    public ActionResult Select(string? name)
    {
        WorkspaceProfile? target = null;
        if (!string.IsNullOrWhiteSpace(name))
        {
            target = Find(_config().Profiles, name);
            if (target is null) return new(false, $"There is no workspace named '{name}'.");
        }
        Apply(target?.Name);
        return new(true, target is null ? "Workspace: all allowed applications." : $"Workspace: {target.Name}.");
    }

    public ActionResult Next() => Step(+1);
    public ActionResult Previous() => Step(-1);

    private ActionResult Step(int direction)
    {
        var names = _config().Profiles.Select(p => p.Name).ToList();
        if (names.Count == 0) return new(false, "No workspaces are defined. Add one in Settings.");
        // slot 0 is "none"; slots 1..n are the profiles
        var current = ActiveName;
        var index = current is null ? 0 : IndexOf(names, current) + 1;
        index = ((index + direction) % (names.Count + 1) + names.Count + 1) % (names.Count + 1);
        return Select(index == 0 ? null : names[index - 1]);
    }

    private void Apply(string? name)
    {
        lock (_gate)
        {
            if (string.Equals(_active, name, StringComparison.OrdinalIgnoreCase)) return;
            _active = name;
        }
        try { _state?.SaveActiveWorkspace(name); } catch { /* remembering the choice is best effort */ }
        Changed?.Invoke();
    }

    private static WorkspaceProfile? Find(IReadOnlyList<WorkspaceProfile> profiles, string name) =>
        profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
