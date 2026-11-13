# 01 — Product Requirements Document (PRD)

## 1. Product summary

VEIL is a cross-platform desktop focus environment. When Focus Mode is enabled, VEIL places a themed overlay over every detected display. Only application windows explicitly allowed by the user may be presented above the overlay. All other applications continue running in the background and are visually concealed.

VEIL is not intended to replace the operating system shell, secure desktop, or OS login screen. It is a controlled workspace layer designed to reduce visual distraction and make switching between approved work windows fast.

## 2. Problem statement

A conventional desktop exposes notifications, unrelated applications, background windows, taskbars, and multiple monitors simultaneously. This creates visual noise and makes it easy to leave the intended task. Existing full-screen applications generally do not provide a unified, multi-monitor focus layer that selectively reveals chosen windows while preserving running application state.

## 3. Product vision

**Hide the Noise. Reveal the Focus.**

VEIL provides one consistent, deliberate work surface across Windows and Ubuntu, with a distinctive hacker/mystic visual identity and fast keyboard-driven control.

## 4. Goals

- G1: Cover all detected displays when Focus Mode is active.
- G2: Reveal selected application windows above the overlay without closing background applications.
- G3: Make window selection and focus transitions fast through global hotkeys.
- G4: Provide configurable application allow-lists and reusable workspace profiles.
- G5: Restrict VEIL administration to one designated OS-level Super Admin identity.
- G6: Recover safely from application crashes, display changes, and shutdown.
- G7: Share product logic and UI across Windows and Ubuntu while using native platform adapters.

## 5. Non-goals for VEIL 1.0

- Replacing Windows Explorer, GNOME Shell, or the Linux display server.
- Providing a tamper-proof kiosk or secure lock screen.
- Preventing a local OS administrator from terminating or modifying VEIL.
- Blocking operating-system secure attention sequences, TTYs, or privileged system dialogs.
- Full Wayland window-management support.
- Cloud sync, team administration, plugin marketplace, AI assistant, or remote policy management.
- Automatically deciding which applications are productive.

## 6. Target users and primary scenarios

### Primary user
A single workstation owner who wants a controlled desktop and has authority to designate the machine's Super Admin account.

### Scenarios
1. Start VEIL and enter Focus Mode across all monitors.
2. Keep an IDE, terminal, and browser running but reveal only the selected window(s).
3. Press a hotkey to open the Window Selector and switch to another allowed window.
4. Temporarily hide all work windows and return to the VEIL background.
5. Change settings after authenticating as the designated Super Admin.
6. Disconnect or connect a monitor while Focus Mode is active.
7. Recover the normal desktop if VEIL exits unexpectedly.

## 7. Product principles

- **Focus, not confinement:** VEIL controls visible workspace content; it does not claim to secure the OS session.
- **Fail safe:** VEIL must never leave a persistent black/covered desktop after a crash or forced shutdown.
- **Keyboard first:** common operations must be available through configurable global hotkeys.
- **Predictable visibility:** users can always identify which windows are allowed and which are currently visible.
- **Platform honesty:** platform-specific limitations must be visible in product documentation and UI.
- **Minimal surface:** keep the focus canvas calm; place configuration in a separate admin area.

## 8. Scope by release

### VEIL 1.0 — Core
- Multi-monitor overlay creation and teardown.
- Display topology change detection.
- Window enumeration and identification.
- Allowed application policy.
- Active window selection and stacking above overlay.
- Window Selector.
- Global hotkeys: Focus On, Focus Off, Hide All, Selector, Emergency Recovery.
- OS identity-based Super Admin access to settings.
- Configuration persistence and backup/restore.
- Windows and Ubuntu X11 adapters.
- Diagnostics and crash-safe recovery.

### VEIL 1.x — Enhancement candidates
- Workspace profiles and per-profile application sets.
- Remembered window geometry per display.
- Focus timer and session history.
- Launchpad for approved applications.
- Theme and wallpaper packs.
- Better window thumbnails and multi-window grouping.

### Later
- Wayland-specific integration where compositor APIs permit.
- Optional managed/kiosk deployment, designed as a separate OS-level product mode.
- Cloud profile sync, only after security and privacy review.

## 9. Success metrics

| Metric | Target for 1.0 |
|---|---|
| Display coverage | Every supported active display receives an overlay |
| Focus transition | Focus On/Off completes within 1 second on reference hardware, excluding OS scheduling outliers |
| Window selection | Selected window becomes visible within 500 ms on reference hardware |
| Crash recovery | Overlay is removed after process termination; no persistent cover remains |
| Resource use | Idle VEIL target: under 150 MB RAM; under 2% average CPU over a 10-minute idle test on reference hardware |
| Reliability | 8-hour soak test without overlay leak or unhandled crash |
| Admin enforcement | Non-admin OS account cannot open or modify protected VEIL settings |
| Cross-platform | Core acceptance suite passes on supported Windows and Ubuntu X11 configurations |

Targets are engineering goals and must be validated on named reference machines before release.

## 10. Product risks

- Window stacking behavior differs by OS, window manager, fullscreen mode, and application.
- Windows secure desktop and UAC prompts are outside VEIL's control.
- Wayland intentionally restricts arbitrary cross-application window control.
- Global hotkey registration may conflict with other applications.
- OS account administration differs between Windows and Linux distributions.
- Some applications create helper, splash, modal, or elevated windows that need explicit policy.

## 11. Release acceptance

VEIL 1.0 is acceptable when all P0 requirements in the SRS pass on the supported platform matrix, emergency recovery is verified, and the product accurately communicates that Overlay Focus is not a security boundary.
