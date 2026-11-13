using VEIL.Domain;

namespace VEIL.Platform.Abstractions;

public sealed class DisplayTopologyChangedEventArgs(IReadOnlyList<DisplayInfo> displays) : EventArgs
{
    public IReadOnlyList<DisplayInfo> Displays { get; } = displays;
}

public interface IDisplayService
{
    IReadOnlyList<DisplayInfo> GetDisplays();
    event EventHandler<DisplayTopologyChangedEventArgs>? TopologyChanged;
}

/// <summary>One overlay surface covering one display. Dispose must be idempotent (NFR-REL-003).</summary>
public interface IOverlaySurface : IDisposable
{
    string DisplayId { get; }
    bool IsHealthy { get; }
}

public interface IOverlayHost
{
    /// <summary>Creates and shows an overlay covering <paramref name="display"/>.</summary>
    IOverlaySurface Create(DisplayInfo display);
}

/// <summary>
/// Everything the Focus screen shows besides its fixed wordmark (UI/UX 3.1): clock, workspace, timer and a quiet system
/// line. Built from cached values only, because the overlay reads it on the window thread while painting and must never
/// wait for anything.
/// </summary>
public sealed record HudSnapshot(
    DateTime Now,
    bool ShowClock = true,
    bool ShowSystemStatus = true,
    string? Workspace = null,
    int VisibleWindows = 0,
    TimeSpan? TimerRemaining = null,
    double TimerProgress = 0,
    string? Notice = null,
    double? CpuPercent = null,
    double? MemoryPercent = null)
{
    public static HudSnapshot Empty => new(DateTime.Now);
}

/// <summary>The identity VEIL stores for an application the user dropped or picked.</summary>
public sealed record ResolvedApplication(string ApplicationId, string DisplayName);

public sealed record ResolveResult(ResolvedApplication? Application, string? Error)
{
    public static ResolveResult Ok(ResolvedApplication app) => new(app, null);
    public static ResolveResult Fail(string error) => new(null, error);
}

/// <summary>
/// Turns something a user dropped on the settings window (an executable or a shortcut) into the application identity the
/// allow-list uses, so nobody has to type or hunt for a path.
/// </summary>
public interface IApplicationResolver
{
    ResolveResult Resolve(string path);
}

public interface IWindowService
{
    /// <summary>Eligible top-level windows. <c>IsAllowed</c> is always false here; policy is applied by the application layer.</summary>
    IReadOnlyList<WindowInfo> GetWindows();
    /// <summary>Presents the window above the overlay. Returns false if the window is gone or cannot be revealed.</summary>
    bool Reveal(string runtimeId);
    /// <summary>Places the window behind the overlay without closing, minimizing or suspending it.</summary>
    bool Hide(string runtimeId);
}

public sealed record HotkeyRegistrationResult(bool Success, string? Error)
{
    public static HotkeyRegistrationResult Ok { get; } = new(true, null);
    public static HotkeyRegistrationResult Fail(string error) => new(false, error);
}

public sealed class HotkeyPressedEventArgs(HotkeyCommand command) : EventArgs
{
    public HotkeyCommand Command { get; } = command;
}

public sealed class TransientHotkeyEventArgs(string name) : EventArgs
{
    public string Name { get; } = name;
}

public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Short-lived global key (may be a bare key such as Enter) that exists only while a modal VEIL surface is open.
    /// Callers must unregister it when that surface closes.
    /// </summary>
    HotkeyRegistrationResult RegisterTransient(string name, KeyModifiers modifiers, string key);
    void UnregisterTransient(string name);
    event EventHandler<TransientHotkeyEventArgs>? TransientPressed;

    HotkeyRegistrationResult Register(HotkeyBinding binding);
    void Unregister(HotkeyCommand command);
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;
}

public enum AuthenticationStatus { Verified, Denied, Unavailable }

public sealed record AuthenticationResult(AuthenticationStatus Status, SuperAdminIdentity? Identity, string Message);

public interface IAuthenticationProvider
{
    /// <summary>Stable identity of the current OS session principal.</summary>
    SuperAdminIdentity GetCurrentIdentity();
    /// <summary>True when the current process holds OS administrator/root authority (required for provisioning).</summary>
    bool IsOsAdministrator();
    /// <summary>Asks the OS for a trustworthy identity verdict. Must never collect or store passwords.</summary>
    AuthenticationResult Authenticate();
}

public enum SessionSupport { Supported, Restricted, Unsupported }

public sealed record SessionInfo(string Os, string SessionType, SessionSupport Support, string Notice);

public interface ISessionInspector
{
    SessionInfo Inspect();
}

/// <summary>Applies OS-appropriate restrictive permissions (NFR-SEC-002).</summary>
public interface IProtectedStorage
{
    void EnsureProtectedDirectory(string path);
}

/// <summary>
/// One selector entry. <see cref="Text"/> is the plain one-line form (accessibility name, tests); the other members let a
/// view draw the title, application and the two status badges. Policy and state are always words, never colour alone.
/// </summary>
public sealed record SelectorRow(int Number, string Text, bool IsAllowed, string Title = "", string Application = "", string State = "");

/// <summary>Where and how large the selector should be, in physical pixels, plus the display scale for fonts.</summary>
public sealed record SelectorPlacement(WindowBounds Bounds, double Scale, string DisplayId);

public sealed record SelectorViewState(string Filter, bool OnlyAllowed, IReadOnlyList<SelectorRow> Rows, int SelectedIndex, string Status);

/// <summary>
/// A real, focusable Window Selector surface (UI/UX 3.2). It owns native text input, so input methods work normally and
/// no global key capture is needed. Events are raised on a worker thread, never on the window thread, and in order; a
/// handler that blocks therefore cannot freeze the window thread (ADR-009).
/// </summary>
public interface ISelectorView : IDisposable
{
    bool IsVisible { get; }
    void Show(SelectorViewState state, SelectorPlacement placement);
    void Update(SelectorViewState state);
    void Hide();

    event Action<string>? FilterChanged;
    event Action<int>? SelectionChanged;
    event Action<bool>? OnlyAllowedChanged;
    /// <summary>Enter, double click.</summary>
    event Action? ConfirmRequested;
    /// <summary>Esc, or the selector lost the user's attention.</summary>
    event Action? CancelRequested;
    /// <summary>A digit 1-9 pressed while the list has focus.</summary>
    event Action<int>? NumberPressed;
}
