# VEIL — Design Baseline v1.0

**Product:** VEIL — Controlled Desktop Environment  
**Document set:** PRD, SRS, Architecture, UI/UX, Security Design, Implementation Plan  
**Status:** Approved design baseline  
**Platforms:** Windows 10/11 and Ubuntu 22.04/24.04  
**Focus model:** Overlay Focus  
**Administrator model:** OS-level exclusive Super Admin

## Purpose

VEIL creates a distraction-reduced desktop workspace by covering every detected display with a themed overlay while allowing explicitly selected application windows to appear above it. Applications continue running; VEIL controls visibility and workspace presentation rather than terminating processes.

## Documents

| File | Purpose |
|---|---|
| `docs/01_PRD.md` | Product vision, users, scope, use cases, success criteria |
| `docs/02_SRS.md` | Functional and non-functional requirements, acceptance criteria |
| `docs/03_ARCHITECTURE.md` | Components, platform adapters, data model, state machine, interfaces |
| `docs/04_UI_UX.md` | Visual language, screens, interaction, accessibility |
| `docs/05_SECURITY_DESIGN.md` | OS identity, exclusive admin, threat model, recovery |
| `docs/06_IMPLEMENTATION_PLAN.md` | Phases, work packages, dependencies, test gates, release plan |
| `docs/07_TRACEABILITY.md` | Requirement → component → test → status (what is verified live vs only unit-tested) |
| `docs/08_DESIGN_TOKENS.md` | Theme, style and design tokens derived from `resources/images`, contrast rules, usage |
| `docs/adr/` | Architecture Decision Records (ADR-001..008) |

## Baseline decisions

1. Shared cross-platform application and domain logic; platform-specific window/display control.
2. Windows 10/11 and Ubuntu are target platforms.
3. VEIL 1.0 Linux support targets X11 sessions. Wayland is an explicitly deferred compatibility target.
4. Overlay Focus is a visibility/workspace feature, not a secure OS lock screen or kiosk.
5. OS account identity and OS permissions are authoritative for Super Admin.
6. The application must fail safe: a crash, display topology change, or shutdown must not permanently hide the user's desktop.
7. Emergency recovery is always available and must not depend on the VEIL UI remaining responsive.

## Suggested repository layout

```text
VEIL/
├── docs/
├── src/
│   ├── VEIL.App/
│   ├── VEIL.Presentation/
│   ├── VEIL.Application/
│   ├── VEIL.Domain/
│   ├── VEIL.Infrastructure/
│   ├── VEIL.Platform.Abstractions/
│   ├── VEIL.Platform.Windows/
│   └── VEIL.Platform.Linux.X11/
├── tests/
│   ├── VEIL.UnitTests/
│   ├── VEIL.IntegrationTests/
│   └── VEIL.PlatformTests/
├── assets/
├── installers/
└── README.md
```

## Install and try it

```powershell
.\installers\windowsuild.ps1                       # tests + publish + package into .\dist
.\dist\VEIL-1.0.0-dev-win-x64\Install-VEIL-Trial.cmd   # this user only, no administrator rights
.\dist\VEIL-1.0.0-dev-win-x64\Install-VEIL.cmd         # whole machine (UAC prompt)
```

VEIL starts with Windows and is running as soon as setup ends (tray icon + hotkeys); it covers the screen only when you press `Ctrl+Alt+V`. `Ctrl+Alt+Esc` is the emergency exit. Details, options and uninstall: `installers/windows/README.md`.

## Implementation status

Windows-first spike per the delivery strategy: shared Domain/Application/Infrastructure layers, the Windows Win32 adapter (displays, overlays, windows, hotkeys, identity), Linux session detection, and a console host. **Not release-ready**; see `docs/07_TRACEABILITY.md` for exactly what is verified. `VEIL.Presentation` holds the Avalonia theme and the Admin Settings window (opened by `veil settings`). The Ubuntu X11 adapter (Phase 3) is not started.

```bash
dotnet build VEIL.slnx
dotnet test tests/VEIL.UnitTests
src/VEIL.App/bin/Debug/net10.0/veil.exe diag      # read-only
src/VEIL.App/bin/Debug/net10.0/veil.exe focus --seconds 5   # covers every display for 5 s
src/VEIL.App/bin/Debug/net10.0/veil.exe settings            # Admin Settings window (Super Admin only)
```

Emergency Recovery is `Ctrl+Alt+Esc`; `Ctrl+C` in the console also tears everything down. Set `VEIL_CONFIG_DIR` to use a scratch configuration directory.

## Document control

- Version: 1.0
- Baseline date: 2026-11-13
- Changes to platform support, security boundary, or overlay behavior require an Architecture Decision Record (ADR).
