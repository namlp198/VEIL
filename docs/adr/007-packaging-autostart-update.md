# ADR-007: Packaging, auto-start and update

- Status: Accepted for the first installable build (Windows); updates and Linux packaging still open
- Date: 2026-11-13
- Requirements: FR-LIFE-001/003/004/005, FR-ADM-004, NFR-SEC-002; plan P7.3, P7.4

## Context
VEIL must start with Windows and be usable immediately after setup. FR-LIFE-005 forbids silently entering Focus Mode on first boot or after a crash. The security design wants machine policy in a protected location and Super Admin provisioning to be an OS-administrator action.

## Decision
- **Resident agent.** `veil run` registers the global hotkeys and a tray icon and stays idle. It never enters Focus Mode by itself; only a hotkey or the tray menu does. One instance per session (named mutex); a second launch asks the running one to open settings. A windowless build of the same code (`veil-agent.exe`, `-p:VeilGui=true`) is what starts at logon; `veil.exe` stays a console tool.
- **Start with Windows** is the registry `Run` value `VEIL`: HKLM for a machine install (all users), HKCU for a per-user install. The command is the quoted agent path plus `run`.
- **Two scopes.** *Machine*: `%ProgramFiles%\VEIL`, protected settings in `%ProgramData%\VEIL`, Super Admin set by `veil admin provision` from the elevated setup. *User* (trial): `%LOCALAPPDATA%\Programs\VEIL`, settings in the profile, not protected against that user. `install.json` next to the executables records which, and tells the agent where its settings are.
- **Starts de-elevated.** The elevated installer launches the agent through `explorer.exe` so it runs with the user's normal rights.
- **Packaging.** `installers/windows/build.ps1` produces a self-contained win-x64 package (no .NET needed), `SHA256SUMS.txt` and a zip. Scripts: `install.ps1`, `uninstall.ps1`, `.cmd` wrappers. Upgrade = reinstall over the top; settings are kept unless `-RemoveData`.
- **Default Selector hotkey** became `Ctrl+Alt+L` (see ADR-004).

## Consequences / open items
The executables and scripts are **unsigned**: SmartScreen and some antivirus products may warn. The machine scope was exercised only as a dry run (no elevation was available when it was built). No automatic update mechanism, no MSIX/MSI, no per-user settings split on a machine install, and no Linux package. Saving machine-wide settings needs the "administrator" settings shortcut because the protected folder is read-only for normal users.
