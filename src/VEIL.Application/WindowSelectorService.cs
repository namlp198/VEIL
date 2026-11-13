using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Application;

public sealed record SelectorEntry(
    int Number,
    WindowInfo Window,
    PolicyDecision Decision,
    WindowDisplayState State)
{
    public bool IsAllowed => Decision.IsAllowed;
}

/// <summary>
/// Builds the Window Selector view. Allowed and visible are separate dimensions (UI/UX principle 4).
/// FR-SEL-002/004/005.
/// </summary>
public sealed class WindowSelectorService(IWindowService windows, Func<ApplicationPolicy> policy, Func<IReadOnlyCollection<string>> revealed)
{
    public IReadOnlyList<SelectorEntry> BuildEntries(string? filter = null, bool onlyAllowed = false)
    {
        var pol = policy();
        var shown = revealed().ToHashSet();
        var candidates = new List<(WindowInfo Window, PolicyDecision Decision)>();
        foreach (var w in windows.GetWindows())
        {
            var decision = pol.Evaluate(w);
            if (onlyAllowed && !decision.IsAllowed) continue;
            if (!string.IsNullOrWhiteSpace(filter) && !Matches(w, filter.Trim())) continue;
            candidates.Add((w, decision));
        }

        // Stable order so numeric shortcuts (FR-SEL-004) do not move: allowed first, then application name, then the
        // session-scoped runtime id. Z-order, reveal state and window titles are deliberately NOT sort keys.
        var ordered = candidates
            .OrderBy(c => c.Decision.IsAllowed ? 0 : 1)
            .ThenBy(c => Path.GetFileNameWithoutExtension(c.Window.ApplicationId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Window.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Window.RuntimeId, StringComparer.Ordinal)
            .ToList();

        var result = new List<SelectorEntry>(ordered.Count);
        foreach (var (w, decision) in ordered)
        {
            var state = w.IsMinimized ? WindowDisplayState.Minimized
                : shown.Contains(w.RuntimeId) ? WindowDisplayState.Visible
                : WindowDisplayState.Hidden;
            result.Add(new SelectorEntry(result.Count + 1, w with { IsAllowed = decision.IsAllowed }, decision, state));
        }
        return result;
    }

    /// <summary>Numeric shortcuts address the first nine entries only (FR-SEL-004).</summary>
    public static SelectorEntry? GetByNumber(IReadOnlyList<SelectorEntry> entries, int number) =>
        number is >= 1 and <= 9 && number <= entries.Count ? entries[number - 1] : null;

    /// <summary>Cycles through allowed windows for Next/Previous shortcuts. Returns null when none are allowed.</summary>
    public static SelectorEntry? Cycle(IReadOnlyList<SelectorEntry> entries, string? currentRuntimeId, int step)
    {
        var allowed = entries.Where(e => e.IsAllowed).ToList();
        if (allowed.Count == 0) return null;
        var idx = allowed.FindIndex(e => e.Window.RuntimeId == currentRuntimeId);
        var next = idx < 0 ? (step > 0 ? 0 : allowed.Count - 1) : ((idx + step) % allowed.Count + allowed.Count) % allowed.Count;
        return allowed[next];
    }

    private static bool Matches(WindowInfo w, string filter) =>
        w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(w.ApplicationId).Contains(filter, StringComparison.OrdinalIgnoreCase);
}
