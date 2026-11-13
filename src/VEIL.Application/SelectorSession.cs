using VEIL.Domain;

namespace VEIL.Application;

public enum SelectorArea { Filter, List, OnlyAllowedToggle }

/// <summary>
/// Interactive Window Selector behaviour (UI/UX 3.2, FR-SEL-001..005).
/// Tab / Shift+Tab move between the filter box, the list and the "show only allowed" toggle.
/// List: Up/Down move, Enter reveals, 1-9 reveal that entry. Filter: characters edit the search text, so digits are
/// text there. Typing a character while in the list jumps to the filter. Esc always closes.
/// A refused reveal keeps the selector open with the policy message (AT-006).
/// </summary>
public sealed class SelectorSession(FocusService focus, WindowSelectorService selector)
{
    private IReadOnlyList<SelectorEntry> _entries = [];
    private static readonly SelectorArea[] Order = [SelectorArea.Filter, SelectorArea.List, SelectorArea.OnlyAllowedToggle];

    public bool IsOpen { get; private set; }
    public bool Persistent { get; set; }
    public SelectorArea Area { get; private set; } = SelectorArea.List;
    public string Filter { get; private set; } = string.Empty;
    public bool OnlyAllowed { get; private set; }
    public int SelectedIndex { get; private set; }
    public IReadOnlyList<SelectorEntry> Entries => _entries;
    public SelectorEntry? Selected => SelectedIndex >= 0 && SelectedIndex < _entries.Count ? _entries[SelectedIndex] : null;

    public ActionResult Open(string? filter = null, bool onlyAllowed = false)
    {
        if (IsOpen) return new(true, "Selector is already open.");
        var opened = focus.OpenSelector();
        if (!opened.Success) return opened;
        Filter = filter ?? string.Empty;
        OnlyAllowed = onlyAllowed;
        Area = SelectorArea.List;
        IsOpen = true;
        SelectedIndex = 0;
        Refresh(keep: null);
        return new(true, _entries.Count == 0 ? "No eligible windows detected." : $"{_entries.Count} window(s).");
    }

    public ActionResult Close()
    {
        if (!IsOpen) return new(true, "Selector is not open.");
        IsOpen = false;
        _entries = [];
        focus.CloseSelector();
        return new(true, "Selector closed.");
    }

    /// <summary>Called when Focus ended underneath the selector (Focus Off, emergency recovery).</summary>
    public void ForceClosed()
    {
        IsOpen = false;
        _entries = [];
    }

    // ---- focus movement -----------------------------------------------------------------------------------------

    public SelectorArea Tab(bool reverse = false)
    {
        if (!IsOpen) return Area;
        var i = Array.IndexOf(Order, Area);
        Area = Order[((i + (reverse ? -1 : 1)) % Order.Length + Order.Length) % Order.Length];
        return Area;
    }

    // ---- filter -------------------------------------------------------------------------------------------------

    /// <summary>Appends a printable character to the filter (switching to the filter box if needed).</summary>
    public ActionResult TypeChar(char c)
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        if (char.IsControl(c)) return new(false, "Not a printable character.");
        if (Filter.Length >= 64) return new(false, "Filter is at its maximum length.");
        Area = SelectorArea.Filter;
        Filter += c;
        Refresh(keep: Selected?.Window.RuntimeId);
        return new(true, Describe());
    }

    public ActionResult Backspace()
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        if (Area != SelectorArea.Filter || Filter.Length == 0) return new(false, "Nothing to delete.");
        Filter = Filter[..^1];
        Refresh(keep: Selected?.Window.RuntimeId);
        return new(true, Describe());
    }

    public ActionResult ToggleOnlyAllowed()
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        OnlyAllowed = !OnlyAllowed;
        Refresh(keep: Selected?.Window.RuntimeId);
        return new(true, Describe());
    }

    /// <summary>Replaces the whole filter text (used by a real text box).</summary>
    public ActionResult SetFilter(string text)
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        text = new string(text.Where(c => !char.IsControl(c)).Take(64).ToArray());
        if (text == Filter) return new(true, Describe());
        Filter = text;
        Refresh(keep: Selected?.Window.RuntimeId);
        return new(true, Describe());
    }

    public ActionResult SetOnlyAllowed(bool value)
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        if (value == OnlyAllowed) return new(true, Describe());
        OnlyAllowed = value;
        Refresh(keep: Selected?.Window.RuntimeId);
        return new(true, Describe());
    }

    public void Select(int index)
    {
        if (IsOpen && index >= 0 && index < _entries.Count) SelectedIndex = index;
    }

    /// <summary>Reveals the highlighted entry regardless of which control has focus.</summary>
    public ActionResult RevealSelected()
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        if (Selected is not { } entry) return new(false, "No window to select.");
        var result = focus.Reveal(entry.Window.RuntimeId);
        if (result.Success && !Persistent) Close();
        return result;
    }

    // ---- list ---------------------------------------------------------------------------------------------------

    public void Move(int step)
    {
        if (!IsOpen || _entries.Count == 0) return;
        Area = SelectorArea.List;
        SelectedIndex = ((SelectedIndex + step) % _entries.Count + _entries.Count) % _entries.Count;
    }

    /// <summary>Enter: reveals in the list, moves to the list from the filter, toggles on the toggle.</summary>
    public ActionResult Confirm()
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        switch (Area)
        {
            case SelectorArea.Filter:
                Area = SelectorArea.List;
                return new(true, Describe());
            case SelectorArea.OnlyAllowedToggle:
                return ToggleOnlyAllowed();
        }
        if (Selected is not { } entry) return new(false, "No window to select.");
        var result = focus.Reveal(entry.Window.RuntimeId);
        if (result.Success && !Persistent) Close();
        return result;
    }

    /// <summary>
    /// A digit key. In the list it is the direct shortcut for entries 1-9; in the filter it is text.
    /// </summary>
    public ActionResult PressNumber(int number)
    {
        if (!IsOpen) return new(false, "Selector is not open.");
        if (Area == SelectorArea.Filter) return TypeChar((char)('0' + number));
        var entry = WindowSelectorService.GetByNumber(_entries, number);
        if (entry is null) return new(false, $"There is no entry {number}.");
        SelectedIndex = entry.Number - 1;
        return Confirm();
    }

    private string Describe() =>
        $"Filter '{Filter}'{(OnlyAllowed ? ", only allowed" : string.Empty)}: {_entries.Count} window(s).";

    private void Refresh(string? keep)
    {
        _entries = selector.BuildEntries(Filter, OnlyAllowed);
        var idx = keep is null ? -1 : _entries.ToList().FindIndex(e => e.Window.RuntimeId == keep);
        SelectedIndex = idx >= 0 ? idx : 0;
    }
}
