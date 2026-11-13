using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Presentation.ViewModels;

public enum StatusKind { None, Success, Error, Info }

public sealed partial class RuleRowViewModel : ObservableObject
{
    [ObservableProperty] private string _applicationId;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AllowedLabel), nameof(DialogsLabel), nameof(RemoveLabel))] private string _displayName;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _followParent;

    public RuleRowViewModel(ApplicationRule rule)
    {
        _applicationId = rule.ApplicationId;
        _displayName = rule.DisplayName;
        _enabled = rule.Enabled;
        _followParent = rule.Transient == TransientWindowPolicy.FollowParent;
    }

    // Accessible names say which application a control belongs to ("Allowed" alone is ambiguous in a list of rows).
    public string AllowedLabel => $"{DisplayName}: allowed to appear above the overlay";
    public string DialogsLabel => $"{DisplayName}: include its dialogs";
    public string RemoveLabel => $"Remove {DisplayName} from the list";

    public ApplicationRule ToRule() => new(ApplicationId, DisplayName, Enabled, FollowParent ? TransientWindowPolicy.FollowParent : TransientWindowPolicy.Hide);
}

/// <summary>A program that is running right now and could be allowed with one click (no path to find).</summary>
public sealed record OpenAppItem(string DisplayName, string ApplicationId)
{
    public string AddLabel => $"Allow {DisplayName}";
}

/// <summary>
/// Allow-list editor (FR-APP-001/004). Every save goes through <see cref="AdminService"/>, which re-authorizes.
/// Applications are added by dropping an .exe or a shortcut on the window, or by picking one that is open now; typing a
/// path stays available as the fallback.
/// </summary>
public sealed partial class ApplicationsPageViewModel : ObservableObject
{
    public const int MaxRules = 1000;
    private const int MaxDroppedAtOnce = 50;
    private readonly AdminService _admin;
    private readonly IConfigurationStore _store;
    private readonly IApplicationResolver _resolver;
    private readonly Func<IReadOnlyList<WindowInfo>> _openWindows;

    [ObservableProperty] private string _newApplicationId = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private StatusKind _statusKind;
    [ObservableProperty] private bool _isDirty;

    public ApplicationsPageViewModel(AdminService admin, IConfigurationStore store,
        IApplicationResolver? resolver = null, Func<IReadOnlyList<WindowInfo>>? openWindows = null)
    {
        _admin = admin;
        _store = store;
        _resolver = resolver ?? new PathApplicationResolver();
        _openWindows = openWindows ?? (() => []);
        Reload();
        RefreshOpenApps();
    }

    public string Title => "Allowed Applications";
    public string Description => "Only windows of these applications can be shown above the overlay. Everything else stays hidden. Hiding never closes or suspends an application.";
    public ObservableCollection<RuleRowViewModel> Rules { get; } = [];
    public ObservableCollection<OpenAppItem> OpenApps { get; } = [];
    public bool IsEmpty => Rules.Count == 0;
    public bool HasOpenApps => OpenApps.Count > 0;

    private void Reload()
    {
        foreach (var r in Rules) r.PropertyChanged -= OnRowChanged;
        Rules.Clear();
        foreach (var r in _store.Load().Rules) { var row = new RuleRowViewModel(r); row.PropertyChanged += OnRowChanged; Rules.Add(row); }
        IsDirty = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => IsDirty = true;

    private bool Contains(string id) => Rules.Any(r => string.Equals(r.ApplicationId, id, StringComparison.OrdinalIgnoreCase));

    private void AddRow(ApplicationRule rule)
    {
        var row = new RuleRowViewModel(rule);
        row.PropertyChanged += OnRowChanged;
        Rules.Add(row);
        IsDirty = true;
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Add()
    {
        var id = NewApplicationId.Trim();
        if (id.Length == 0) { Say("Drop an application here, pick one below, or enter the full path of its executable.", StatusKind.Error); return; }
        if (id.Length > 1024) { Say("That path is too long.", StatusKind.Error); return; }
        // A typed path may also be a shortcut or a relative path: resolve it the same way a drop would be.
        var resolved = _resolver.Resolve(id);
        var app = resolved.Application ?? new ResolvedApplication(id, Path.GetFileNameWithoutExtension(id));
        if (Rules.Count >= MaxRules) { Say($"At most {MaxRules} applications can be listed.", StatusKind.Error); return; }
        if (Contains(app.ApplicationId) || Contains(id)) { Say("That application is already on the list.", StatusKind.Error); return; }
        AddRow(new ApplicationRule(app.ApplicationId, app.DisplayName));
        NewApplicationId = string.Empty;
        Say("Added. Save to apply.", StatusKind.Info);
    }

    /// <summary>
    /// Files dropped on the window. Each program (or shortcut to one) is added and the list is saved straight away, so
    /// dropping really is all it takes. Anything that is not an application is skipped with a reason.
    /// </summary>
    public void AddDropped(IReadOnlyList<string> paths)
    {
        var added = new List<string>();
        var already = 0;
        var problems = new List<string>();
        foreach (var path in paths.Take(MaxDroppedAtOnce))
        {
            var result = _resolver.Resolve(path);
            if (result.Application is null) { problems.Add($"{Path.GetFileName(path)}: {result.Error}"); continue; }
            if (Contains(result.Application.ApplicationId)) { already++; continue; }
            if (Rules.Count >= MaxRules) { problems.Add($"At most {MaxRules} applications can be listed."); break; }
            AddRow(new ApplicationRule(result.Application.ApplicationId, result.Application.DisplayName));
            added.Add(result.Application.DisplayName);
        }
        if (paths.Count > MaxDroppedAtOnce) problems.Add($"Only the first {MaxDroppedAtOnce} items were used.");

        if (added.Count > 0)
        {
            var saved = _admin.UpdateAllowList(Rules.Select(r => r.ToRule()).ToList());
            if (!saved.Success) { Say(saved.Message, StatusKind.Error); return; } // rows stay on screen, nothing was stored
            IsDirty = false;
            RefreshOpenApps();
        }

        var parts = new List<string>();
        if (added.Count > 0) parts.Add($"Added {string.Join(", ", added)} and saved.");
        if (already > 0) parts.Add(already == 1 ? "1 was already on the list." : $"{already} were already on the list.");
        if (problems.Count > 0) parts.Add("Skipped: " + string.Join("; ", problems));
        if (parts.Count == 0) parts.Add("Nothing to add.");
        Say(string.Join(" ", parts), added.Count > 0 ? StatusKind.Success : problems.Count > 0 ? StatusKind.Error : StatusKind.Info);
    }

    /// <summary>Programs with a window open right now, minus those already allowed. A quick alternative to dropping.</summary>
    [RelayCommand]
    private void RefreshOpenApps()
    {
        OpenApps.Clear();
        IReadOnlyList<WindowInfo> windows;
        try { windows = _openWindows(); }
        catch (Exception) { windows = []; } // the list is a convenience; never let it break the page
        foreach (var group in windows.Where(w => !w.ApplicationId.StartsWith("unknown:", StringComparison.Ordinal))
                     .GroupBy(w => w.ApplicationId, StringComparer.OrdinalIgnoreCase))
        {
            if (Contains(group.Key)) continue;
            var name = _resolver.Resolve(group.Key).Application?.DisplayName ?? Path.GetFileNameWithoutExtension(group.Key);
            OpenApps.Add(new OpenAppItem(name, group.Key));
        }
        OnPropertyChanged(nameof(HasOpenApps));
    }

    [RelayCommand]
    private void AllowOpenApp(OpenAppItem? item)
    {
        if (item is null) return;
        if (Contains(item.ApplicationId)) { RefreshOpenApps(); return; }
        if (Rules.Count >= MaxRules) { Say($"At most {MaxRules} applications can be listed.", StatusKind.Error); return; }
        AddRow(new ApplicationRule(item.ApplicationId, item.DisplayName));
        var saved = _admin.UpdateAllowList(Rules.Select(r => r.ToRule()).ToList());
        if (saved.Success) { IsDirty = false; Say($"Added {item.DisplayName} and saved.", StatusKind.Success); RefreshOpenApps(); }
        else Say(saved.Message, StatusKind.Error);
    }

    [RelayCommand]
    private void Remove(RuleRowViewModel? row)
    {
        if (row is null || !Rules.Remove(row)) return;
        row.PropertyChanged -= OnRowChanged;
        IsDirty = true;
        OnPropertyChanged(nameof(IsEmpty));
        Say("Removed. Save to apply.", StatusKind.Info);
    }

    [RelayCommand]
    private void Save()
    {
        var result = _admin.UpdateAllowList(Rules.Select(r => r.ToRule()).ToList());
        if (result.Success) { IsDirty = false; Say(result.Message, StatusKind.Success); RefreshOpenApps(); }
        else Say(result.Message, StatusKind.Error); // the stored list is unchanged; the edits stay on screen
    }

    [RelayCommand]
    private void Revert()
    {
        Reload();
        RefreshOpenApps();
        Say("Changes discarded.", StatusKind.Info);
    }

    private void Say(string text, StatusKind kind) { StatusText = text; StatusKind = kind; }
}
