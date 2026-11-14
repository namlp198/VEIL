namespace VEIL.Domain;

/// <summary><c>Selector</c> is retired (the Window Selector was removed). It stays only so older configuration files still parse; it is dropped on load and never registered.</summary>
public enum HotkeyCommand { FocusOn, FocusOff, HideAll, Selector, EmergencyRecovery, NextWindow, PreviousWindow, HideCurrent, FocusTimer, NextProfile, Dock, TrayMode }

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>A platform-neutral key combination such as <c>Ctrl+Alt+V</c> (FR-HK-005).</summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)
{
    private static readonly string[] NamedKeys = ["Tab", "Esc", "Left", "Right", "Up", "Down", "Space", "Enter", "Backspace"];

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = KeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) return false;
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= KeyModifiers.Ctrl; break;
                case "alt": mods |= KeyModifiers.Alt; break;
                case "shift": mods |= KeyModifiers.Shift; break;
                case "win" or "super" or "meta": mods |= KeyModifiers.Win; break;
                default:
                    if (key is not null) return false;
                    key = raw;
                    break;
            }
        }
        if (key is null || mods == KeyModifiers.None) return false; // bare keys would hijack typing
        var normalized = Normalize(key);
        if (normalized is null) return false;
        chord = new KeyChord(mods, normalized);
        return true;
    }

    private static string? Normalize(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return key.ToUpperInvariant();
        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24)
            return "F" + n;
        foreach (var named in NamedKeys)
            if (named.Equals(key, StringComparison.OrdinalIgnoreCase)) return named;
        return null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}

public sealed record HotkeyBinding(HotkeyCommand Command, string Chord, bool Enabled = true)
{
    public bool TryGetChord(out KeyChord chord) => KeyChord.TryParse(Chord, out chord);
}

public static class DefaultHotkeys
{
    public static IReadOnlyList<HotkeyBinding> Create() =>
    [
        new(HotkeyCommand.FocusOn, "Ctrl+Alt+V"),
        new(HotkeyCommand.FocusOff, "Ctrl+Alt+U"),
        new(HotkeyCommand.HideAll, "Ctrl+Alt+H"),
        new(HotkeyCommand.EmergencyRecovery, "Ctrl+Alt+Esc"),
        new(HotkeyCommand.NextWindow, "Ctrl+Alt+Right"),
        new(HotkeyCommand.PreviousWindow, "Ctrl+Alt+Left"),
        new(HotkeyCommand.HideCurrent, "Ctrl+Alt+Down"),
        new(HotkeyCommand.FocusTimer, "Ctrl+Alt+T"),
        new(HotkeyCommand.NextProfile, "Ctrl+Alt+P"),
        new(HotkeyCommand.Dock, "Ctrl+Alt+D"),
        new(HotkeyCommand.TrayMode, "Ctrl+Alt+M"),
    ];

    /// <summary>
    /// Commands introduced after a configuration file was written are added with their default chord, unless that chord is
    /// already taken, so an upgrade gains the new shortcuts without the user editing anything. Existing bindings never change.
    /// </summary>
    public static IReadOnlyList<HotkeyBinding> WithMissingDefaults(IReadOnlyList<HotkeyBinding> existing)
    {
        var result = existing.ToList();
        var used = result.Where(b => b.Enabled && b.TryGetChord(out _)).Select(b => { b.TryGetChord(out var c); return c; }).ToHashSet();
        foreach (var d in Create())
        {
            if (result.Any(b => b.Command == d.Command)) continue;
            if (d.TryGetChord(out var chord) && !used.Add(chord)) continue;
            result.Add(d);
        }
        return result;
    }

    /// <summary>Returns human-readable problems; empty means the set is valid (FR-HK-002/005).</summary>
    public static IReadOnlyList<string> Validate(IEnumerable<HotkeyBinding> bindings)
    {
        var list = bindings.ToList();
        var problems = new List<string>();
        var seen = new Dictionary<KeyChord, HotkeyCommand>();
        var commands = new HashSet<HotkeyCommand>();
        foreach (var b in list)
        {
            if (!commands.Add(b.Command)) problems.Add($"Duplicate binding for {b.Command}.");
            if (!b.TryGetChord(out var chord)) { problems.Add($"{b.Command}: '{b.Chord}' is not a valid shortcut."); continue; }
            if (!b.Enabled) continue;
            if (seen.TryGetValue(chord, out var other)) problems.Add($"{chord} is assigned to both {other} and {b.Command}.");
            else seen[chord] = b.Command;
        }
        var er = list.FirstOrDefault(b => b.Command == HotkeyCommand.EmergencyRecovery);
        if (er is null || !er.Enabled) problems.Add("Emergency Recovery must remain bound (NFR-UX-001).");
        return problems;
    }
}
