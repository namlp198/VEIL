using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VEIL.Presentation.ViewModels;

namespace VEIL.Presentation.Views;

public sealed partial class AdminWindow : Window
{
    public AdminWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Dropping an application anywhere on the window adds it to the allow-list: no path to look up or type.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public AdminWindow(AdminShellViewModel viewModel) : this() => DataContext = viewModel;

    private AdminShellViewModel? Shell => DataContext as AdminShellViewModel;

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accept = Shell is { IsGranted: true } && HasFiles(e);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        if (Shell is { } shell) shell.IsDragActive = accept;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (Shell is { } shell) shell.IsDragActive = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Shell is not { } shell) return;
        shell.IsDragActive = false;
        e.Handled = true;
        if (!shell.IsGranted || !HasFiles(e)) return;
        var paths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        shell.HandleDrop(paths);
    }

    /// <summary>
    /// Opens the window at an explicit place (physical pixels) and size (device-independent pixels), so it lands on the
    /// chosen display at that display's scale. Must be called before the window is shown.
    /// </summary>
    public void PlaceOn(double widthDip, double heightDip, int x, int y)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = widthDip;
        Height = heightDip;
        MinWidth = Math.Min(MinWidth, widthDip);
        MinHeight = Math.Min(MinHeight, heightDip);
        Position = new PixelPoint(x, y);
    }
}
