using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Presentation.ViewModels;

public enum AccessState { Unchecked, Granted, Denied }

/// <summary>One entry in the settings navigation (UI/UX 3.4). Each says whether it applies to the machine or the user.</summary>
public sealed record SectionItem(string Name, string Scope, bool IsImplemented, Func<object> CreatePage)
{
    public string ScopeLabel => Scope;
    public bool IsPlanned => !IsImplemented;

    /// <summary>What a screen reader says for the navigation entry: name, scope, and whether it is available.</summary>
    public string AccessibleName => $"{Name}, applies to {Scope.ToLowerInvariant()}{(IsPlanned ? ", planned, not available yet" : string.Empty)}";
}

/// <summary>
/// Admin Login + Admin Settings shell (UI/UX 3.3, 3.4). It never collects a password: it asks <see cref="AdminService"/> to
/// verify the current OS identity against the single configured Super Admin, and shows the settings only when that succeeds.
/// FR-ADM-001/003, AT-009/AT-010.
/// </summary>
public sealed partial class AdminShellViewModel : ObservableObject
{
    private readonly AdminService _admin;
    private readonly IAuthenticationProvider _auth;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsGranted), nameof(IsDenied))] private AccessState _access;
    [ObservableProperty] private string _accessTitle = "Super Admin required";
    [ObservableProperty] private string _accessMessage = "Settings can be changed only by the designated Super Admin.";
    [ObservableProperty] private SectionItem? _selectedSection;
    [ObservableProperty] private object? _currentPage;
    /// <summary>True while files are dragged over the window: a full-window hint shows where they will go.</summary>
    [ObservableProperty] private bool _isDragActive;

    public AdminShellViewModel(
        AdminService admin,
        IConfigurationStore store,
        IAuthenticationProvider auth,
        ISessionInspector session,
        Func<IReadOnlyList<DisplayInfo>> displays,
        string version,
        IApplicationResolver? resolver = null,
        Func<IReadOnlyList<WindowInfo>>? openWindows = null,
        IFocusSessionLog? sessions = null)
    {
        _admin = admin;
        _auth = auth;
        var me = auth.GetCurrentIdentity();
        IdentityText = $"{me.DisplayName}  ·  {me.Platform}  ·  {me.Principal}";

        Sections =
        [
            new("General", "Whole machine", false, () => new PlaceholderPageViewModel("General", "Start with the OS and what Focus Mode does when switched on or off.")),
            new("Displays", "Whole machine", false, () => new PlaceholderPageViewModel("Displays", "Per-display preferences, resolution and order.")),
            new("Applications", "Whole machine", true, () => new ApplicationsPageViewModel(admin, store, resolver, openWindows)),
            new("Hotkeys", "Whole machine", true, () => new HotkeysPageViewModel(admin, store)),
            new("Appearance", "Whole machine", true, () => new AppearancePageViewModel(admin, store)),
            new("Workspace Profiles", "Whole machine", true, () => new ProfilesPageViewModel(admin, store)),
            new("Focus Timer", "Whole machine", true, () => new FocusTimerPageViewModel(admin, store, sessions)),
            new("Security", "Whole machine", true, () => new SecurityPageViewModel(auth, store)),
            new("Diagnostics", "This user", true, () => new DiagnosticsPageViewModel(session, store, displays, version)),
            new("Backup & Restore", "Whole machine", false, () => new PlaceholderPageViewModel("Backup & Restore", "Validated export and import of the configuration.")),
        ];
        SignIn();
    }

    public string IdentityText { get; }
    public IReadOnlyList<SectionItem> Sections { get; }
    public bool IsGranted => Access == AccessState.Granted;
    public bool IsDenied => Access == AccessState.Denied;

    /// <summary>Asks the OS-backed provider again; never prompts for or stores a password.</summary>
    [RelayCommand]
    private void SignIn()
    {
        var result = _admin.Authorize();
        if (result.IsGranted)
        {
            Access = AccessState.Granted;
            AccessTitle = "Signed in as Super Admin";
            AccessMessage = result.Message;
            SelectedSection ??= Sections.First(s => s.Name == "Applications");
            return;
        }
        Access = AccessState.Denied;
        CurrentPage = null;
        AccessTitle = result.Decision switch
        {
            AdminDecision.DeniedNotProvisioned => "No Super Admin is set up",
            AdminDecision.DeniedAuthUnavailable => "Identity could not be verified",
            _ => "Access denied",
        };
        AccessMessage = result.Decision switch
        {
            AdminDecision.DeniedNotProvisioned => "An OS administrator must designate the Super Admin account during setup. Settings stay locked until then.",
            AdminDecision.DeniedAuthUnavailable => "The operating system could not give a trustworthy identity for this session, so settings are not opened.",
            _ => "Only the designated Super Admin can open settings. If this account should be the Super Admin, an OS administrator can reassign it using the documented recovery procedure.",
        };
    }

    /// <summary>
    /// Something was dropped on the window. Whatever page is showing, the Applications page takes it: it is added to the
    /// allow-list and saved. Returns false (and does nothing) unless the Super Admin is signed in.
    /// </summary>
    public bool HandleDrop(IReadOnlyList<string> paths)
    {
        if (!IsGranted || paths.Count == 0) return false;
        if (CurrentPage is not ApplicationsPageViewModel)
            SelectedSection = Sections.First(s => s.Name == "Applications");
        if (CurrentPage is not ApplicationsPageViewModel page) return false;
        page.AddDropped(paths);
        return true;
    }

    partial void OnSelectedSectionChanged(SectionItem? value)
    {
        if (!IsGranted || value is null) return;
        CurrentPage = value.CreatePage();
    }
}
