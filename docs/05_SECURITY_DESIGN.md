# 05 — Security Design

## 1. Security position

VEIL Overlay Focus is a desktop visibility and workspace-control feature. It is not a security boundary against a local user with OS administrator/root privileges, and it is not a replacement for the OS lock screen, secure desktop, or managed kiosk configuration.

The OS is the authority for user identity, authentication, and file permissions.

## 2. Super Admin model

### Requirement
Exactly one designated Super Admin identity is configured for a VEIL machine installation.

### Identity
Store a stable OS identity reference, not a display name alone:
- Windows: machine/user SID or another stable OS principal identifier.
- Linux: UID plus account identity metadata; account rename/reassignment handling must be specified.

Do not use username text as the sole authorization key.

### Provisioning
- First-time provisioning requires an OS administrator-authorized setup flow.
- The installer or setup utility records one Super Admin identity.
- A second identity cannot be added through the ordinary VEIL Settings UI.
- Reassignment requires an explicit OS-admin recovery procedure.
- The application never stores the OS account password.

### Authentication
- Authenticate through supported OS mechanisms.
- Verify the authenticated principal matches the configured Super Admin identity.
- Do not implement a parallel VEIL password database for OS login credentials.
- If the platform cannot provide a trustworthy authentication result in the current session, deny Admin Settings rather than silently falling back.

## 3. Permission model

| Resource/action | Standard user | Designated Super Admin | OS administrator/root |
|---|---:|---:|---:|
| Use Focus Mode | Yes, subject to install policy | Yes | Yes |
| Select allowed windows | Yes | Yes | Yes |
| Change personal UI preference | Policy-dependent | Yes | Yes |
| Change machine-wide allow-list | No | Yes, after OS auth | Yes |
| Change protected hotkeys/policies | No | Yes, after OS auth | Yes |
| Reassign Super Admin | No | No through normal UI | Yes, recovery flow |
| Modify protected files directly | No | No unless OS permissions grant it | Yes |
| Terminate VEIL | May be possible for own process | Yes | Yes |

The designated Super Admin is the only VEIL administrator. OS administrators/root remain technically capable of overriding local software controls; this is an inherent OS privilege boundary.

## 4. Threat model

### Assets
- Machine-wide VEIL configuration.
- Super Admin identity reference.
- Application allow-list and profiles.
- Audit records.
- Integrity and availability of the desktop session.

### Threat actors
- Standard local account attempting to modify settings.
- Accidental user action.
- Malicious or malfunctioning application window.
- Local OS administrator/root.
- Malware running with the user's privileges.

### Threats and mitigations

| Threat | Mitigation | Residual risk |
|---|---|---|
| Standard user edits machine config | OS ACL/permissions; protected storage path | OS admin can override |
| User impersonates Super Admin by changing display name | Compare stable OS principal identifier | Account migration requires recovery |
| Password captured by VEIL | Never collect/store OS password; use OS auth | OS auth integration varies |
| Config tampering | Protected files, schema validation, integrity checks where useful | Root/admin can tamper |
| Overlay traps user | Emergency Recovery, process-exit teardown, safe startup | OS-level failure can still occur |
| Disallowed window appears | Default-deny policy; transient window rules; platform tests | Platform stacking limitations |
| Log leaks sensitive data | Minimize logged content; no screenshots or document contents | Titles may contain sensitive text |
| Malicious config import | Strict schema validation, size limits, atomic apply | Parser vulnerabilities |
| Hotkey hijack/conflict | Registration checks and diagnostics | Other privileged tools may intercept |

## 5. Protected configuration

- Machine-wide configuration must be stored in an OS-protected location.
- Windows: use restrictive NTFS ACLs; grant write access only to the required service/installer context and designated administrator workflow.
- Linux: use root-owned configuration directory and restrictive group/mode permissions.
- Separate user preferences from machine-wide policy.
- Use atomic writes and validated schema migrations.
- Backups must preserve permissions where practical and must not contain credentials.

## 6. Emergency recovery and fail-safe behavior

Mandatory:
- Emergency Recovery shortcut.
- Explicit Focus Off action.
- Recovery path available before overlay creation.
- Idempotent overlay teardown.
- On normal application exit, destroy all overlays.
- On process crash, rely on OS window ownership cleanup; verify this with automated process-kill tests.
- Never auto-reenter Focus after an unexpected crash unless the user has explicitly enabled a tested recovery behavior.
- If initialization partially fails, remove every overlay already created.

Do not intercept OS secure attention sequences or attempt to block system-level recovery controls.

## 7. Audit and privacy

Audit events may include:
- Admin authentication success/failure.
- Machine policy changes.
- Super Admin provisioning/reassignment.
- Configuration import/export.
- Recovery events.

Do not log:
- OS passwords or authentication secrets.
- Screenshot contents.
- Keystrokes outside VEIL hotkeys.
- Document contents or clipboard contents.
- Full window titles by default if they may contain sensitive information; allow diagnostic opt-in where needed.

Logs should rotate, have bounded retention, and use restrictive permissions.

## 8. Platform-specific security notes

### Windows
- Respect UAC and secure desktop behavior.
- Do not attempt to draw over or control the secure desktop.
- Do not claim protection from Task Manager, process termination, or another administrator.
- Validate executable identity and process ownership before applying window policy.
- Avoid broad process-kill privileges.

### Ubuntu
- Use per-user session integration for window operations.
- Protect machine-wide configuration with root ownership and restrictive permissions.
- Do not use root to manipulate ordinary desktop windows unless a narrowly justified mechanism requires it.
- Do not claim protection from TTY switching, root, or session termination.
- Wayland limitations remain applicable.

## 9. Security acceptance tests

- Standard user cannot write machine-wide policy.
- Standard user cannot provision a second Super Admin through the UI.
- Wrong OS identity is denied Admin Settings.
- Correct Super Admin identity can authenticate using the platform-supported flow.
- No password is written to disk or VEIL logs.
- Corrupt or malicious config import is rejected without overwriting valid configuration.
- Killing VEIL does not leave an overlay.
- Emergency Recovery removes overlays even if the selector is unresponsive.
- Documentation accurately states the local OS administrator/root limitation.
