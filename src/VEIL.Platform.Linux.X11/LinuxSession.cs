using System.Runtime.InteropServices;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Linux.X11;

/// <summary>
/// Phase 3 scope: session detection and honest Wayland handling (AT-012, FR-LIFE, plan P3.5). The X11/EWMH display and
/// window adapters follow the Windows proof-of-concept per the delivery strategy and are intentionally not stubbed
/// with fake behaviour.
/// </summary>
public sealed class LinuxSessionInspector(Func<string, string?>? env = null) : ISessionInspector
{
    private readonly Func<string, string?> _env = env ?? Environment.GetEnvironmentVariable;

    public SessionInfo Inspect()
    {
        var type = (_env("XDG_SESSION_TYPE") ?? string.Empty).Trim().ToLowerInvariant();
        var hasWayland = !string.IsNullOrEmpty(_env("WAYLAND_DISPLAY"));
        var hasX = !string.IsNullOrEmpty(_env("DISPLAY"));

        if (type == "wayland" || (type.Length == 0 && hasWayland))
            return new("Linux", "Wayland", hasX ? SessionSupport.Restricted : SessionSupport.Unsupported,
                "Wayland restricts cross-application window control. VEIL 1.0 does not support Overlay Focus here; log in to an X11 (Xorg) session.");
        if (type == "x11" || (type.Length == 0 && hasX))
            return new("Linux", "X11", SessionSupport.Supported,
                "X11 session detected. Behaviour depends on the window manager; GNOME on Xorg is the reference environment.");
        return new("Linux", string.IsNullOrEmpty(type) ? "Unknown" : type, SessionSupport.Unsupported,
            "No graphical X11 session was detected.");
    }
}

/// <summary>Identity is the numeric UID (stable across renames). Authentication is the OS-authenticated login session.</summary>
public sealed class LinuxAuthenticationProvider : IAuthenticationProvider
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    public SuperAdminIdentity GetCurrentIdentity() =>
        new("Linux", OperatingSystem.IsLinux() ? GetEuid().ToString() : "unknown", Environment.UserName);

    public bool IsOsAdministrator() => OperatingSystem.IsLinux() && GetEuid() == 0;

    public AuthenticationResult Authenticate() =>
        OperatingSystem.IsLinux()
            ? new(AuthenticationStatus.Verified, GetCurrentIdentity(), "Login session identity verified.")
            : new(AuthenticationStatus.Unavailable, null, "Not a Linux session.");
}

/// <summary>Root-owned, 0755 directory; files should be 0644 (security design 5).</summary>
public sealed class LinuxProtectedStorage : IProtectedStorage
{
    public void EnsureProtectedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
