using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Application;

public interface IConfigurationStore
{
    VeilConfiguration Load();
    void Save(VeilConfiguration configuration);
}

public interface IAuditLog
{
    void Write(AuditEvent auditEvent);
}

public enum AdminDecision { Granted, DeniedNotProvisioned, DeniedWrongIdentity, DeniedAuthUnavailable }

public sealed record AdminAuthorization(AdminDecision Decision, string Message)
{
    public bool IsGranted => Decision == AdminDecision.Granted;
}

/// <summary>
/// Super Admin model (security design 2): exactly one OS identity, verified through the OS, never a VEIL password.
/// FR-ADM-001..006, FR-ADM-007.
/// </summary>
public sealed class AdminService(IAuthenticationProvider auth, IConfigurationStore store, IAuditLog audit, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Verifies the current OS principal against the single configured Super Admin (AT-009/AT-010).</summary>
    public AdminAuthorization Authorize()
    {
        var config = store.Load();
        var current = auth.GetCurrentIdentity();
        if (config.SuperAdmin is null)
            return Record(current, "admin.authorize", new(AdminDecision.DeniedNotProvisioned, "No Super Admin has been provisioned."));

        var result = auth.Authenticate();
        if (result.Status == AuthenticationStatus.Unavailable)
            return Record(current, "admin.authorize", new(AdminDecision.DeniedAuthUnavailable, result.Message));
        if (result.Status != AuthenticationStatus.Verified || !config.SuperAdmin.IsSamePrincipal(result.Identity))
            return Record(current, "admin.authorize", new(AdminDecision.DeniedWrongIdentity, "This OS account is not the designated Super Admin."));
        return Record(current, "admin.authorize", new(AdminDecision.Granted, "Super Admin verified."));
    }

    /// <summary>First-time provisioning; needs OS administrator authority and refuses to add a second identity (FR-ADM-002/004).</summary>
    public ActionResult Provision(SuperAdminIdentity target)
    {
        var current = auth.GetCurrentIdentity();
        if (!auth.IsOsAdministrator())
            return Fail(current, "admin.provision", "OS administrator authority is required to provision the Super Admin.");
        var config = store.Load();
        if (config.SuperAdmin is not null)
            return Fail(current, "admin.provision", "A Super Admin is already configured. Use the OS-admin reassignment procedure.");
        if (!TrySave(config with { SuperAdmin = target }, out var saveError)) return Fail(current, "admin.provision", saveError!);
        return Ok(current, "admin.provision", $"Super Admin provisioned for {target.DisplayName}.");
    }

    /// <summary>Documented recovery/reassignment procedure; requires OS administrator authority (FR-ADM-006).</summary>
    public ActionResult Reassign(SuperAdminIdentity target)
    {
        var current = auth.GetCurrentIdentity();
        if (!auth.IsOsAdministrator())
            return Fail(current, "admin.reassign", "OS administrator authority is required to reassign the Super Admin.");
        var config = store.Load();
        if (!TrySave(config with { SuperAdmin = target }, out var saveError)) return Fail(current, "admin.reassign", saveError!);
        return Ok(current, "admin.reassign", $"Super Admin reassigned to {target.DisplayName}.");
    }

    /// <summary>Protected, machine-wide allow-list change: authorizes on every call (FR-ADM-003, FR-APP-004).</summary>
    public ActionResult UpdateAllowList(IReadOnlyList<ApplicationRule> rules)
    {
        var authz = Authorize();
        var current = auth.GetCurrentIdentity();
        if (!authz.IsGranted) return Fail(current, "policy.update", authz.Message);
        if (!TrySave(store.Load() with { Rules = rules }, out var saveError)) return Fail(current, "policy.update", saveError!);
        return Ok(current, "policy.update", $"Allow-list updated ({rules.Count} rule(s)).");
    }

    /// <summary>Protected change of how the Focus screen looks and behaves (motion, clock, system line, timer length).</summary>
    public ActionResult UpdatePreferences(FocusPreferences preferences)
    {
        var authz = Authorize();
        var current = auth.GetCurrentIdentity();
        if (!authz.IsGranted) return Fail(current, "preferences.update", authz.Message);
        if (preferences.DefaultTimerMinutes is < 1 or > FocusTimer.MaxMinutes)
            return Fail(current, "preferences.update", $"The default focus timer must be between 1 and {FocusTimer.MaxMinutes} minutes.");
        if (!TrySave(store.Load() with { Preferences = preferences }, out var saveError)) return Fail(current, "preferences.update", saveError!);
        return Ok(current, "preferences.update", "Preferences updated.");
    }

    /// <summary>Protected workspace-profile change (UI/UX 4.1). Profiles only narrow the allow-list, but they are still policy.</summary>
    public ActionResult UpdateProfiles(IReadOnlyList<WorkspaceProfile> profiles)
    {
        var authz = Authorize();
        var current = auth.GetCurrentIdentity();
        if (!authz.IsGranted) return Fail(current, "profiles.update", authz.Message);
        if (WorkspaceProfile.Validate(profiles) is { } problem) return Fail(current, "profiles.update", problem);
        if (!TrySave(store.Load() with { Profiles = profiles }, out var saveError)) return Fail(current, "profiles.update", saveError!);
        return Ok(current, "profiles.update", $"Workspaces updated ({profiles.Count}).");
    }

    /// <summary>Protected hotkey change. Rejects invalid or conflicting sets and keeps the existing bindings (AT-011).</summary>
    public ActionResult UpdateHotkeys(IReadOnlyList<HotkeyBinding> bindings)
    {
        var authz = Authorize();
        var current = auth.GetCurrentIdentity();
        if (!authz.IsGranted) return Fail(current, "hotkeys.update", authz.Message);
        var problems = DefaultHotkeys.Validate(bindings);
        if (problems.Count > 0) return Fail(current, "hotkeys.update", string.Join(" ", problems));
        if (!TrySave(store.Load() with { Hotkeys = bindings }, out var saveError)) return Fail(current, "hotkeys.update", saveError!);
        return Ok(current, "hotkeys.update", "Hotkeys updated.");
    }

    /// <summary>
    /// The machine configuration folder is protected (security design 5): a Super Admin running without administrator
    /// rights cannot write it. That must be a clear refusal, never an unhandled exception that takes the window down.
    /// </summary>
    private bool TrySave(VeilConfiguration configuration, out string? error)
    {
        try
        {
            store.Save(configuration);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            error = ex is UnauthorizedAccessException
                ? "Nothing was saved: Windows denied write access to the protected configuration folder. Machine-wide settings can only be changed when VEIL runs with administrator rights."
                : "Nothing was saved: the configuration file could not be written (" + ex.Message + ").";
            return false;
        }
    }

    private AdminAuthorization Record(SuperAdminIdentity who, string action, AdminAuthorization result)
    {
        Write(who, action, result.Decision.ToString());
        return result;
    }

    private ActionResult Ok(SuperAdminIdentity who, string action, string message)
    {
        Write(who, action, "Success");
        return new(true, message);
    }

    private ActionResult Fail(SuperAdminIdentity who, string action, string message)
    {
        Write(who, action, "Denied");
        return new(false, message);
    }

    private void Write(SuperAdminIdentity who, string action, string result) =>
        audit.Write(new AuditEvent(_time.GetUtcNow(), who.Principal, action, result));
}
