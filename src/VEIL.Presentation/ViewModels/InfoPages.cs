using VEIL.Application;
using VEIL.Domain;
using VEIL.Platform.Abstractions;

namespace VEIL.Presentation.ViewModels;

public sealed record FactRow(string Label, string Value, bool IsTechnical = false);

/// <summary>Honest stand-in for a settings group that is specified but not built yet (never a fake form).</summary>
public sealed class PlaceholderPageViewModel(string title, string description)
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public string Note => "Not implemented yet. This group is part of a later phase and changes nothing today.";
}

/// <summary>Read-only security facts (UI/UX 3.4, security design 2 and 3). Shows references, never secrets.</summary>
public sealed class SecurityPageViewModel
{
    public SecurityPageViewModel(IAuthenticationProvider auth, IConfigurationStore store)
    {
        var me = auth.GetCurrentIdentity();
        var admin = store.Load().SuperAdmin;
        Facts =
        [
            new("Current account", me.DisplayName),
            new("Account identifier", $"{me.Platform}  {me.Principal}", true),
            new("Super Admin", admin is null ? "Not provisioned" : admin.DisplayName),
            new("Super Admin identifier", admin is null ? "-" : $"{admin.Platform}  {admin.Principal}", true),
            new("This account is the Super Admin", admin is not null && admin.IsSamePrincipal(me) ? "Yes" : "No"),
            new("OS administrator rights in this session", auth.IsOsAdministrator() ? "Yes" : "No"),
            new("How identity is checked", "The operating system's authenticated session identity. VEIL never asks for or stores a password. An extra OS re-authentication prompt is not available yet."),
        ];
    }

    public string Title => "Security";
    public string Description => "Who can change VEIL's settings, and what Overlay Focus does and does not protect.";
    public IReadOnlyList<FactRow> Facts { get; }
    public string Limitation => "Overlay Focus is a visibility and workspace feature, not a secure lock or kiosk. It does not stop a local OS administrator or root, Task Manager, TTY switching, UAC prompts or the secure desktop.";
    public string Recovery => "Reassigning the Super Admin requires OS administrator authority and the documented recovery procedure. It cannot be done from this window.";
}

public sealed class DiagnosticsPageViewModel
{
    public DiagnosticsPageViewModel(ISessionInspector session, IConfigurationStore store, Func<IReadOnlyList<DisplayInfo>> displays, string version)
    {
        var s = session.Inspect();
        var cfg = store.Load();
        Session =
        [
            new("VEIL version", version),
            new("Operating system", s.Os),
            new("Session type", s.SessionType),
            new("Support", s.Support.ToString()),
            new("Notice", s.Notice),
            new("Super Admin reference", cfg.SuperAdmin is null ? "not provisioned" : $"{cfg.SuperAdmin.Platform}  {cfg.SuperAdmin.Principal}", true),
            new("Allowed applications", cfg.Rules.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Enabled hotkeys", string.Join(", ", cfg.Hotkeys.Where(h => h.Enabled).Select(h => $"{h.Command} {h.Chord}")), true),
        ];
        Displays = displays().Select(d =>
            new FactRow(d.Name + (d.IsPrimary ? "  (primary)" : string.Empty),
                $"{d.Bounds.Width} x {d.Bounds.Height}  at ({d.Bounds.X}, {d.Bounds.Y})  scale {(int)Math.Round(d.ScaleFactor * 100)}%", true)).ToList();
    }

    public string Title => "Diagnostics";
    public string Description => "What VEIL sees right now. No window titles, screenshots or secrets are shown or logged here.";
    public IReadOnlyList<FactRow> Session { get; }
    public IReadOnlyList<FactRow> Displays { get; }
}
