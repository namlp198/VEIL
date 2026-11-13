using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace VEIL.Presentation;

/// <summary>Application shell that carries the VEIL theme (tokens + styles). Hosts and test harnesses build on it.</summary>
public sealed class VeilApp : Avalonia.Application
{
    /// <summary>Set by the host before starting the desktop lifetime; creates the window to show.</summary>
    public static Func<Window>? MainWindowFactory { get; set; }

    /// <summary>Called once the application is initialised; the resident agent uses it to create its tray icon.</summary>
    public static Action<Avalonia.Application>? Started { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && MainWindowFactory is { } factory)
            desktop.MainWindow = factory();
        Started?.Invoke(this);
        base.OnFrameworkInitializationCompleted();
    }
}
