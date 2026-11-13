namespace VEIL.Domain;

/// <summary>Pixel rectangle in physical (virtual-screen) coordinates.</summary>
public readonly record struct WindowBounds(int X, int Y, int Width, int Height);

/// <summary>Normalized display description (FR-DISP-001/007).</summary>
public sealed record DisplayInfo(string Id, string Name, WindowBounds Bounds, double ScaleFactor, bool IsPrimary);

public enum WindowKind { Normal, Dialog, Modal, Splash, Child, Unknown }

/// <summary>
/// Normalized window description (FR-WIN-002). <see cref="RuntimeId"/> is session-scoped and must never be persisted;
/// policy matching uses <see cref="ApplicationId"/> (executable path / package identity).
/// </summary>
public sealed record WindowInfo(
    string RuntimeId,
    string ProcessId,
    string ApplicationId,
    string Title,
    string DisplayId,
    WindowBounds Bounds,
    bool IsVisible,
    bool IsMinimized,
    bool IsAllowed,
    WindowKind Kind = WindowKind.Normal);

public enum WindowDisplayState { Visible, Hidden, Minimized, Unavailable }
