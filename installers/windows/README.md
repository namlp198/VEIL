# VEIL for Windows - build and install

VEIL is a focus overlay for Windows 10/11. Once installed it **starts with Windows**, sits in the notification area and
waits for a hotkey. It never covers your screen on its own: Focus Mode starts only when you press `Ctrl+Alt+V`.

## Install (from a built package)

Open the package folder (`VEIL-<version>-win-x64`) and double-click **one** of:

| File | Scope | Administrator rights | Where things go |
|---|---|---|---|
| `Install-VEIL-Trial.cmd` | current user only | **no** | `%LOCALAPPDATA%\Programs\VEIL`, `HKCU\...\Run`, settings in `%LOCALAPPDATA%\VEIL\config` |
| `Install-VEIL.cmd` | whole machine | yes (a UAC prompt appears) | `%ProgramFiles%\VEIL`, `HKLM\...\Run`, protected settings in `%ProgramData%\VEIL` |

Either one copies the files, registers VEIL to start when you sign in, creates Start Menu shortcuts, adds an entry to
*Settings > Apps*, and **starts VEIL immediately** (look for the VEIL ring icon in the notification area; on Windows 11 it
may be under the `^` arrow). Nothing needs a restart.

Use the **trial** install to try VEIL. Its settings folder is writable by you, so it does **not** protect the settings from
you; the machine install does (security design, section 5). Running either installer again upgrades in place and keeps your
settings.

### Hotkeys

| Action | Hotkey |
|---|---|
| Focus On / Off | `Ctrl+Alt+V` / `Ctrl+Alt+U` |
| **Emergency Recovery** (always removes the overlay) | `Ctrl+Alt+Esc` |
| Window Selector | `Ctrl+Alt+L` |
| Hide All / Hide current window | `Ctrl+Alt+H` / `Ctrl+Alt+Down` |
| Next / previous allowed window | `Ctrl+Alt+Right` / `Ctrl+Alt+Left` |

Nothing is shown above the overlay until you add applications to the allow-list: tray icon > **Open settings** >
*Applications*. If a hotkey is already used by another program VEIL says so in its log and leaves it unassigned.

### Tray icon

Right-click: turn Focus On/Off, open settings, open the log folder, exit. Left-click opens settings. Starting VEIL a second
time (for example from the Start Menu) opens its settings instead of starting another copy.

### Super Admin

Only one OS account may change VEIL's settings. The machine installer sets it up as an OS-administrator action
(`veil admin provision`), by default for the user who ran the setup; pass `-SuperAdminAccount DOMAIN\user` to choose
another. In the machine install the settings folder is read-only for normal users, so **saving settings needs the
"VEIL Settings (administrator)" shortcut**. The trial install writes a configuration naming you as Super Admin.

## Uninstall

`Uninstall-VEIL.cmd` (or *Settings > Apps > VEIL*, or the Start Menu entry). It stops VEIL, removes the start-up entry,
shortcuts and files, and **keeps your settings** unless you add `-RemoveData`. It finds the installation through the
registry, so it is safe to run from the build package.

## Build a package (developers)

Needs the .NET 10 SDK (see `global.json`) and Windows PowerShell 5.1 or PowerShell 7.

```powershell
.\installers\windows\build.ps1                 # tests, then publish, package, checksums and zip into .\dist
.\installers\windows\build.ps1 -SkipTests      # faster
.\installers\windows\build.ps1 -FrameworkDependent   # small package; the PC needs the .NET 10 runtime
```

It produces two executables from the same code: `veil.exe` (console tool: `veil diag`, `veil settings`, ...) and
`veil-agent.exe` (no console window; this is what starts at logon). The package is self-contained by default, so the target
PC needs no .NET. `installers\windows\make-icons.ps1` regenerates the icons from the design tokens.

## Options and dry run

```powershell
.\install.ps1 -Scope User -DryRun          # print every step, change nothing
.\install.ps1 -Scope Machine -NoLaunch     # install but do not start now
.\install.ps1 -NoAutoStart                 # do not register start-with-Windows
.\install.ps1 -SuperAdminAccount "PC\alice"
.\uninstall.ps1 -Scope User -RemoveData -Quiet
```

Start with Windows can also be changed afterwards: `veil autostart status|enable|disable [--machine]`.

## Troubleshooting

* **Nothing appears after install.** Open `%LOCALAPPDATA%\VEIL\logs\agent.log`. Check the VEIL icon is not under the `^` arrow.
* **A hotkey does nothing.** The log lists hotkeys that could not be registered because another program owns them.
  Change them in settings > *Hotkeys*.
* **"Nothing was saved ... needs administrator rights".** You are on a machine install: open settings with the
  administrator shortcut.
* **PowerShell refuses to run the script.** The `.cmd` files pass `-ExecutionPolicy Bypass` for this one run. If your
  organisation blocks that, run `Set-ExecutionPolicy -Scope Process RemoteSigned` in a PowerShell window and start
  `install.ps1` from there.
* **The overlay is stuck.** `Ctrl+Alt+Esc`. If VEIL itself is unresponsive, end `veil-agent.exe` in Task Manager: the
  overlay belongs to that process and disappears with it.

## What VEIL is not

Overlay Focus is a visibility and workspace feature, **not a secure lock or kiosk**. It does not stop a local
administrator, Task Manager, UAC prompts or the secure desktop. Installing needs no network access and VEIL collects and
sends nothing.
