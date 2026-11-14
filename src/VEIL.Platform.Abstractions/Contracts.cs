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
    /// <summary>Tray mode (no overlay): puts the window away by minimizing it. Defaults to <see cref="Hide"/>.</summary>
    bool Minimize(string runtimeId) => Hide(runtimeId);
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

/// <summary>Where and how large a HUD window (the password form, the dock's display) should be, in physical pixels, plus the display scale for fonts.</summary>
public sealed record SelectorPlacement(WindowBounds Bounds, double Scale, string DisplayId);

// ---- Dock: the allowed applications, docked at the left edge (opened with Tab) ---------------------------------------

/// <summary>
/// How Focus Mode looks. <b>Panel</b> is the full lock: the overlay covers every screen and the allowed-apps list opens on
/// demand. <b>Tray</b> takes the curtain away and leaves a slim, always-visible tray of pinned applications on the screen
/// edge; a window dragged onto it is put away there and a click brings it back. A hotkey switches between the two.
/// </summary>
public enum DockMode { Panel, Tray }

/// <summary>
/// One tile. In the list it is one allowed window; in the tray it is one pinned application (<see cref="RuntimeId"/> is empty
/// when that application has no window right now).
/// </summary>
public sealed record DockTile(string RuntimeId, string ApplicationId, string AppName, string Title, bool IsShown, bool IsPinned);

public sealed record DockViewState(DockMode Mode, bool Expanded, IReadOnlyList<DockTile> Tiles, IReadOnlyList<DockTile> Pinned, string Status,
    IReadOnlyList<string>? Workspaces = null, string? ActiveWorkspace = null);

/// <summary>
/// The dock surface. It never takes keyboard focus (so the application in front keeps it) and, like the selector, raises its
/// events on a worker thread, never on the window thread (ADR-009).
/// </summary>
public interface IDockView : IDisposable
{
    bool IsVisible { get; }
    /// <summary><paramref name="area"/> is the display's bounds; the view docks itself to the left edge of it.</summary>
    void Show(DockViewState state, SelectorPlacement area);
    void Update(DockViewState state);
    void Hide();

    /// <summary>A tile was clicked: show or hide that window.</summary>
    event Action<string>? TileClicked;
    /// <summary>
    /// Tray mode: a window of another program was dragged by its title bar and dropped on the dock (argument: its runtime id).
    /// </summary>
    event Action<string>? WindowDropped;
    /// <summary>Tray mode: the "Lock" button on the strip asks to go back to the full lock.</summary>
    event Action? LockRequested;
    /// <summary>The X on a list tile: take that application out of the tray (argument: application id).</summary>
    event Action<string>? RemoveRequested;
    /// <summary>A workspace chip was clicked (null: all allowed applications).</summary>
    event Action<string?>? WorkspaceRequested;
    /// <summary>An application tile was dropped on the tray (argument: application id).</summary>
    event Action<string>? PinRequested;
    /// <summary>A tray tile was dropped outside the tray, or right-clicked (argument: application id).</summary>
    event Action<string>? UnpinRequested;
    /// <summary>The "…" button or the close button.</summary>
    event Action? ExpandToggleRequested;
}

// ---- Unlock: the password form behind the lock icon of the full lock ----------------------------------------------------

/// <summary>
/// The password form. It is the only part of the full lock that takes keyboard input. Events are raised on a worker
/// thread, never on the window thread (ADR-009).
/// </summary>
public interface IUnlockView : IDisposable
{
    bool IsVisible { get; }
    /// <summary>Shows the form centred on <paramref name="placement"/>'s display and focuses the password box.</summary>
    void Show(SelectorPlacement placement, string prompt);
    void SetStatus(string text);
    void Hide();

    event Action<string>? Submitted;
    event Action? Cancelled;
}
