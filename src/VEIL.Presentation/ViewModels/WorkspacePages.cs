using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VEIL.Application;
using VEIL.Domain;

namespace VEIL.Presentation.ViewModels;

// ---- Workspace profiles (UI/UX 4.1) -----------------------------------------------------------------------------------

/// <summary>One application of the allow-list, with a switch for whether the selected workspace includes it.</summary>
public sealed partial class ProfileAppToggle : ObservableObject
{
    [ObservableProperty] private bool _included;

    public ProfileAppToggle(string applicationId, string displayName, bool included)
    {
        ApplicationId = applicationId;
        DisplayName = displayName;
        _included = included;
    }

    public string ApplicationId { get; }
    public string DisplayName { get; }
    public string AccessibleName => $"{DisplayName}: part of this workspace";
}

public sealed partial class ProfileRowViewModel : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary))] private string _name;

    public ProfileRowViewModel(string name, IEnumerable<ApplicationRule> rules, IEnumerable<string> included)
    {
        _name = name;
        var chosen = new HashSet<string>(included, StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules) Apps.Add(new ProfileAppToggle(r.ApplicationId, r.DisplayName, chosen.Contains(r.ApplicationId)));
    }

    public ObservableCollection<ProfileAppToggle> Apps { get; } = [];
    public string Summary => $"{Name}  ·  {Apps.Count(a => a.Included)} of {Apps.Count}";
    public void RaiseSummary() => OnPropertyChanged(nameof(Summary));

    /// <summary>Ids that are in the profile but no longer on the allow-list are kept, so removing an app and adding it back loses nothing.</summary>
    public IReadOnlyList<string> Orphans { get; init; } = [];

    public WorkspaceProfile ToProfile() =>
        new(Name.Trim(), Apps.Where(a => a.Included).Select(a => a.ApplicationId).Concat(Orphans).ToList());
}

/// <summary>
/// Workspace editor. A workspace only ever narrows the Super Admin's allow-list, so the choices here are the applications
/// already allowed. Saving goes through <see cref="AdminService"/>, which re-authorizes.
/// </summary>
public sealed partial class ProfilesPageViewModel : ObservableObject
{
    private readonly AdminService _admin;
    private readonly IConfigurationStore _store;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] private ProfileRowViewModel? _selected;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private StatusKind _statusKind;
    [ObservableProperty] private bool _isDirty;

    public ProfilesPageViewModel(AdminService admin, IConfigurationStore store)
    {
        _admin = admin;
        _store = store;
        Reload();
    }

    public string Title => "Workspace Profiles";
    public string Description => "A workspace is a named set of applications, for example Coding or Research. Switch between them from the keyboard without leaving Focus Mode. A workspace can only narrow the allowed applications, never widen them.";
    public IReadOnlyList<string> Suggestions { get; } = ["Coding", "Engineering", "AI Lab", "Research"];
    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];
    public bool HasProfiles => Profiles.Count > 0;
    public bool HasNoProfiles => Profiles.Count == 0;
    public bool HasSelection => Selected is not null;
    public bool HasNoApplications { get; private set; }

    private void Reload()
    {
        foreach (var p in Profiles) Unhook(p);
        Profiles.Clear();
        var cfg = _store.Load();
        HasNoApplications = cfg.Rules.Count == 0;
        foreach (var p in cfg.Profiles) Profiles.Add(Hook(Make(p.Name, cfg.Rules, p.ApplicationIds)));
        Selected = Profiles.FirstOrDefault();
        IsDirty = false;
        Changed();
        OnPropertyChanged(nameof(HasNoApplications));
    }

    private static ProfileRowViewModel Make(string name, IReadOnlyList<ApplicationRule> rules, IReadOnlyList<string> included)
    {
        var ids = rules.Select(r => r.ApplicationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ProfileRowViewModel(name, rules, included) { Orphans = included.Where(i => !ids.Contains(i)).ToList() };
    }

    private ProfileRowViewModel Hook(ProfileRowViewModel row)
    {
        row.PropertyChanged += OnRowChanged;
        foreach (var a in row.Apps) a.PropertyChanged += OnToggleChanged;
        return row;
    }

    private void Unhook(ProfileRowViewModel row)
    {
        row.PropertyChanged -= OnRowChanged;
        foreach (var a in row.Apps) a.PropertyChanged -= OnToggleChanged;
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileRowViewModel.Name)) IsDirty = true;
    }

    private void OnToggleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        IsDirty = true;
        foreach (var p in Profiles) p.RaiseSummary();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasNoProfiles));
    }

    private string UniqueName(string wanted)
    {
        var name = wanted;
        for (var i = 2; Profiles.Any(p => p.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{wanted} {i}";
        return name;
    }

    [RelayCommand]
    private void AddNamed(string? name)
    {
        if (Profiles.Count >= WorkspaceProfile.MaxProfiles) { Say($"At most {WorkspaceProfile.MaxProfiles} workspaces can be defined.", StatusKind.Error); return; }
        var rules = _store.Load().Rules;
        var row = Hook(Make(UniqueName(string.IsNullOrWhiteSpace(name) ? "New workspace" : name.Trim()), rules, []));
        Profiles.Add(row);
        Selected = row;
        IsDirty = true;
        Changed();
        Say("Workspace added. Choose its applications, then save.", StatusKind.Info);
    }

    [RelayCommand]
    private void Remove(ProfileRowViewModel? row)
    {
        if (row is null || !Profiles.Remove(row)) return;
        Unhook(row);
        if (Selected == row) Selected = Profiles.FirstOrDefault();
        IsDirty = true;
        Changed();
        Say("Workspace removed. Save to apply.", StatusKind.Info);
    }

    [RelayCommand]
    private void Save()
    {
        var result = _admin.UpdateProfiles(Profiles.Select(p => p.ToProfile()).ToList());
        if (result.Success) { IsDirty = false; Say(result.Message, StatusKind.Success); }
        else Say(result.Message, StatusKind.Error); // nothing was stored; the edits stay on screen
    }

    [RelayCommand]
    private void Revert()
    {
        Reload();
        Say("Changes discarded.", StatusKind.Info);
    }

    private void Say(string text, StatusKind kind) { StatusText = text; StatusKind = kind; }
}

// ---- Appearance: motion and what the Focus screen shows ----------------------------------------------------------------

public sealed partial class AppearancePageViewModel : ObservableObject
{
    private readonly AdminService _admin;
    private readonly IConfigurationStore _store;

    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _showClock;
    [ObservableProperty] private bool _showSystemStatus;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private StatusKind _statusKind;

    public AppearancePageViewModel(AdminService admin, IConfigurationStore store)
    {
        _admin = admin;
        _store = store;
        Load();
    }

    public string Title => "Appearance";
    public string Description => "How the Focus screen looks. Motion is gentle by default: a slow glow, one drifting dot and a short fade-in. Windows' own \"Animation effects\" setting is always respected as well.";

    private void Load()
    {
        var p = _store.Load().Preferences;
        ReducedMotion = p.ReducedMotion;
        ShowClock = p.ShowClock;
        ShowSystemStatus = p.ShowSystemStatus;
        IsDirty = false;
    }

    partial void OnReducedMotionChanged(bool value) => IsDirty = true;
    partial void OnShowClockChanged(bool value) => IsDirty = true;
    partial void OnShowSystemStatusChanged(bool value) => IsDirty = true;

    [RelayCommand]
    private void Save()
    {
        var next = _store.Load().Preferences with { ReducedMotion = ReducedMotion, ShowClock = ShowClock, ShowSystemStatus = ShowSystemStatus };
        var result = _admin.UpdatePreferences(next);
        if (result.Success) { IsDirty = false; StatusText = result.Message + " The next time Focus Mode starts it uses them."; StatusKind = StatusKind.Success; }
        else { StatusText = result.Message; StatusKind = StatusKind.Error; }
    }

    [RelayCommand]
    private void Revert()
    {
        Load();
        StatusText = "Changes discarded.";
        StatusKind = StatusKind.Info;
    }
}

// ---- Focus timer (UI/UX 4.2) --------------------------------------------------------------------------------------------

public sealed record SessionRow(string Started, string Planned, string Actual, string Outcome, string Workspace);

public sealed partial class FocusTimerPageViewModel : ObservableObject
{
    private readonly AdminService _admin;
    private readonly IConfigurationStore _store;

    [ObservableProperty] private string _defaultMinutesText = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private StatusKind _statusKind;

    public FocusTimerPageViewModel(AdminService admin, IConfigurationStore store, IFocusSessionLog? sessions, TimeProvider? time = null)
    {
        _admin = admin;
        _store = store;
        var cfg = store.Load();
        DefaultMinutesText = cfg.Preferences.DefaultTimerMinutes.ToString(CultureInfo.InvariantCulture);
        IsDirty = false;
        var chord = cfg.Hotkeys.FirstOrDefault(h => h.Command == HotkeyCommand.FocusTimer && h.Enabled)?.Chord;
        HowToStart = chord is null
            ? "Start a session from the tray icon: Focus timer."
            : $"Press {chord} to start or stop a session, or use the tray icon: Focus timer.";

        var now = (time ?? TimeProvider.System).GetLocalNow();
        var recent = sessions?.ReadRecent(200) ?? [];
        Today = Total(recent.Where(r => r.Started.ToLocalTime().Date == now.Date));
        ThisWeek = Total(recent.Where(r => r.Started.ToLocalTime() > now.AddDays(-7)));
        foreach (var r in recent.Take(12))
            Recent.Add(new SessionRow(
                r.Started.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture),
                $"{r.PlannedMinutes} min planned",
                Duration(r.ActualSeconds),
                r.Completed ? "Completed" : "Stopped early",
                r.Workspace ?? "All applications"));
    }

    public string Title => "Focus Timer";
    public string Description => "Timed focus sessions with the time left shown on the Focus screen. VEIL never ends Focus Mode when time is up; it only tells you, quietly. The focus time you actually spent is recorded on this computer.";
    public IReadOnlyList<int> Presets => FocusTimer.Presets;
    public string HowToStart { get; }
    public string Today { get; }
    public string ThisWeek { get; }
    public ObservableCollection<SessionRow> Recent { get; } = [];
    public bool HasRecent => Recent.Count > 0;

    partial void OnDefaultMinutesTextChanged(string value) => IsDirty = true;

    private static string Total(IEnumerable<FocusSessionRecord> rows) => Duration(rows.Sum(r => r.ActualSeconds));

    private static string Duration(int seconds) =>
        seconds >= 3600 ? $"{seconds / 3600} h {seconds % 3600 / 60} min" : seconds >= 60 ? $"{seconds / 60} min" : $"{seconds} s";

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(DefaultMinutesText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes is < 1 or > FocusTimer.MaxMinutes)
        {
            StatusText = $"Enter a whole number of minutes between 1 and {FocusTimer.MaxMinutes}.";
            StatusKind = StatusKind.Error;
            return;
        }
        var result = _admin.UpdatePreferences(_store.Load().Preferences with { DefaultTimerMinutes = minutes });
        if (result.Success) { IsDirty = false; StatusText = $"Default session length is now {minutes} min."; StatusKind = StatusKind.Success; }
        else { StatusText = result.Message; StatusKind = StatusKind.Error; }
    }
}
