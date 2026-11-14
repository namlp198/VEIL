using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VEIL.Application;
using VEIL.Domain;

namespace VEIL.Presentation.ViewModels;

public sealed partial class HotkeyRowViewModel : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem))] private string _chord;
    [ObservableProperty] private bool _enabled;

    public HotkeyRowViewModel(HotkeyBinding binding)
    {
        Command = binding.Command;
        _chord = binding.Chord;
        _enabled = binding.Enabled;
    }

    public HotkeyCommand Command { get; }
    public string Label => Command switch
    {
        HotkeyCommand.FocusOn => "Focus On",
        HotkeyCommand.FocusOff => "Focus Off",
        HotkeyCommand.HideAll => "Hide All",
        HotkeyCommand.EmergencyRecovery => "Emergency Recovery",
        HotkeyCommand.NextWindow => "Next allowed window",
        HotkeyCommand.PreviousWindow => "Previous allowed window",
        HotkeyCommand.HideCurrent => "Hide current window",
        HotkeyCommand.FocusTimer => "Start / stop focus timer",
        HotkeyCommand.NextProfile => "Next workspace",
        HotkeyCommand.Dock => "Allowed apps panel (also Tab)",
        HotkeyCommand.TrayMode => "Switch full lock / tray mode",
        _ => Command.ToString(),
    };
    public bool IsLocked => Command == HotkeyCommand.EmergencyRecovery;
    public string ChordLabel => $"{Label} shortcut";
    public string EnabledLabel => IsLocked ? $"{Label} enabled (required, cannot be turned off)" : $"{Label} enabled";

    /// <summary>Immediate feedback while typing; conflicts between rows are reported on save.</summary>
    public string? Problem => KeyChord.TryParse(Chord, out _) ? null : "Not a valid shortcut. Use modifiers plus a key, e.g. Ctrl+Alt+V.";
    public bool HasProblem => Problem is not null;

    public HotkeyBinding ToBinding() => new(Command, Chord.Trim(), Enabled);
}

/// <summary>Hotkey editor (FR-HK-001/002/005). Invalid or conflicting sets are refused and the stored bindings stay intact (AT-011).</summary>
public sealed partial class HotkeysPageViewModel : ObservableObject
{
    private readonly AdminService _admin;
    private readonly IConfigurationStore _store;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private StatusKind _statusKind;
    [ObservableProperty] private bool _isDirty;

    public HotkeysPageViewModel(AdminService admin, IConfigurationStore store)
    {
        _admin = admin;
        _store = store;
        Reload();
    }

    public string Title => "Hotkeys";
    public string Description => "Global shortcuts. A shortcut that cannot be registered is reported, never replaced silently. Emergency Recovery must stay bound and enabled.";
    public ObservableCollection<HotkeyRowViewModel> Rows { get; } = [];

    private void Reload()
    {
        foreach (var r in Rows) r.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        foreach (var b in _store.Load().Hotkeys) { var row = new HotkeyRowViewModel(b); row.PropertyChanged += OnRowChanged; Rows.Add(row); }
        IsDirty = false;
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => IsDirty = true;

    [RelayCommand]
    private void Save()
    {
        var result = _admin.UpdateHotkeys(Rows.Select(r => r.ToBinding()).ToList());
        if (result.Success) { IsDirty = false; StatusText = result.Message; StatusKind = StatusKind.Success; }
        else { StatusText = result.Message; StatusKind = StatusKind.Error; }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        foreach (var r in Rows) r.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        foreach (var b in DefaultHotkeys.Create()) { var row = new HotkeyRowViewModel(b); row.PropertyChanged += OnRowChanged; Rows.Add(row); }
        IsDirty = true;
        StatusText = "Defaults loaded. Save to apply.";
        StatusKind = StatusKind.Info;
    }

    [RelayCommand]
    private void Revert()
    {
        Reload();
        StatusText = "Changes discarded.";
        StatusKind = StatusKind.Info;
    }
}
