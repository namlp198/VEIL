using System.Security.AccessControl;
using System.Security.Principal;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Platform.Windows;

/// <summary>Composition entry point for the Windows adapter. One message thread backs all native services.</summary>
public sealed class WindowsPlatform : IDisposable
{
    private readonly Win32Thread _thread;

    public WindowsPlatform()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Windows adapter requires Windows.");
        _thread = new Win32Thread();
        Displays = new WindowsDisplayService(_thread);
        Overlays = new WindowsOverlayHost(_thread);
        Windows = new WindowsWindowService();
        Hotkeys = new WindowsHotkeyService(_thread);
        Dock = new WindowsDockView(_thread);
        Unlock = new WindowsUnlockView(_thread);
        KeyGuard = new WindowsKeyGuard(_thread);
        _thread.Stalled += silent => MessageThreadStalled?.Invoke(silent);
        _thread.Fault += message => Fault?.Invoke(message);
    }

    /// <summary>
    /// The thread that owns every overlay stopped answering for several seconds. Nothing in this process can repair that
    /// (ADR-009); the host is expected to end the process, which makes Windows remove all overlay windows.
    /// </summary>
    public event Action<TimeSpan>? MessageThreadStalled;

    /// <summary>An unexpected exception was contained on a platform thread (for the diagnostic log).</summary>
    public event Action<string>? Fault;

    /// <summary>Stops all animation (the user's reduced-motion preference). Windows' own animation setting is always honoured as well.</summary>
    public bool ReducedMotion { get => Motion.ReducedByPreference; set => Motion.ReducedByPreference = value; }

    /// <summary>A soft system sound for "session complete". Asynchronous; never waits for audio.</summary>
    public void SoftChime() => Native.MessageBeep(0x40 /* MB_ICONASTERISK */);

    public IApplicationResolver Resolver { get; } = new WindowsApplicationResolver();
    public WindowsSystemStats SystemStats { get; } = new();

    /// <summary>Runs <paramref name="work"/> on the ordered command thread, never on the window thread.</summary>
    public void PostCommand(Action work) => _thread.Post(work);

    public WindowsDisplayService Displays { get; }
    public WindowsOverlayHost Overlays { get; }
    public WindowsWindowService Windows { get; }
    public WindowsHotkeyService Hotkeys { get; }
    public WindowsDockView Dock { get; }
    public WindowsUnlockView Unlock { get; }
    public WindowsKeyGuard KeyGuard { get; }
    public WindowsAuthenticationProvider Authentication { get; } = new();
    public WindowsSessionInspector Session { get; } = new();
    public WindowsProtectedStorage Storage { get; } = new();

    /// <summary>Per-monitor v2 awareness so bounds are physical pixels on mixed-DPI setups (FR-DISP-007). Call once, first.</summary>
    public static void EnableDpiAwareness()
    {
        if (OperatingSystem.IsWindows()) Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_PER_MONITOR_V2);
    }

    public void Dispose()
    {
        try { KeyGuard.Dispose(); Unlock.Dispose(); } catch { /* window thread may already be gone */ }
        try { Dock.Dispose(); } catch { /* window thread may already be gone */ }
        Hotkeys.Dispose();
        Displays.Dispose();
        _thread.Dispose();
    }
}

public sealed class WindowsSessionInspector : ISessionInspector
{
    public SessionInfo Inspect() => new("Windows", "Desktop", SessionSupport.Supported,
        "Overlay Focus is a visibility feature, not a secure lock. UAC prompts, the secure desktop and elevated windows are outside VEIL's control.");
}

/// <summary>
/// Interim ADR-005 mechanism: the identity is the SID of the interactive logon token the OS already authenticated.
/// No password is ever read. An explicit OS re-authentication prompt is still an open item.
/// </summary>
public sealed class WindowsAuthenticationProvider : IAuthenticationProvider
{
    public SuperAdminIdentity GetCurrentIdentity()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new SuperAdminIdentity("Windows", id.User?.Value ?? "unknown", id.Name);
    }

    /// <summary>Resolves a SID string or an account name (DOMAIN\user, user) to a stable identity, or null if it does not exist.</summary>
    public SuperAdminIdentity? ResolveIdentity(string accountOrSid)
    {
        try
        {
            SecurityIdentifier sid;
            if (accountOrSid.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) sid = new SecurityIdentifier(accountOrSid);
            else sid = (SecurityIdentifier)new NTAccount(accountOrSid).Translate(typeof(SecurityIdentifier));
            string name;
            try { name = ((NTAccount)sid.Translate(typeof(NTAccount))).Value; } catch (IdentityNotMappedException) { name = sid.Value; }
            return new SuperAdminIdentity("Windows", sid.Value, name);
        }
        catch (Exception ex) when (ex is ArgumentException or IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    public bool IsOsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public AuthenticationResult Authenticate()
    {
        using var id = WindowsIdentity.GetCurrent();
        if (!id.IsAuthenticated || id.IsAnonymous || id.IsGuest || id.IsSystem || id.User is null)
            return new(AuthenticationStatus.Unavailable, null, "The current session does not provide a trustworthy interactive identity.");
        return new(AuthenticationStatus.Verified, new SuperAdminIdentity("Windows", id.User.Value, id.Name), "Interactive session identity verified.");
    }
}

/// <summary>NTFS ACLs: SYSTEM and Administrators full control; Users read/execute only (NFR-SEC-002).</summary>
public sealed class WindowsProtectedStorage : IProtectedStorage
{
    public void EnsureProtectedDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        void Add(WellKnownSidType sid, FileSystemRights rights) =>
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights, inherit, PropagationFlags.None, AccessControlType.Allow));
        Add(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        Add(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        Add(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute);
        dir.SetAccessControl(security);
    }
}
