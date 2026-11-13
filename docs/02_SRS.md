# 02 — Software Requirements Specification (SRS)

## 1. Purpose and conventions

This document defines testable requirements for VEIL 1.0. Requirement IDs are stable references for implementation, test cases, and traceability.

Priority:
- **P0:** release blocking.
- **P1:** required for a complete 1.0 experience unless explicitly deferred.
- **P2:** optional enhancement.

## 2. Supported environment

| OS | 1.0 target | Notes |
|---|---|---|
| Windows 10 | Yes | Supported editions to be confirmed during packaging tests |
| Windows 11 | Yes | Primary Windows validation target |
| Ubuntu 22.04 | Yes, X11 | GNOME on Xorg or another tested X11 window manager |
| Ubuntu 24.04 | Yes, X11 | GNOME on Xorg or another tested X11 window manager |
| Linux Wayland | No full support commitment | Show unsupported-session notice or restricted mode |

Minimum hardware: modern x64 desktop/laptop, at least one display, keyboard and pointing device. ARM support is not included in 1.0 unless separately approved.

## 3. Functional requirements

### 3.1 Application lifecycle

- **FR-LIFE-001 (P0):** VEIL shall start in a non-covering safe state until initialization and display discovery complete.
- **FR-LIFE-002 (P0):** VEIL shall provide explicit Focus On, Focus Off, and Exit actions.
- **FR-LIFE-003 (P0):** On normal exit, VEIL shall destroy all overlay windows and restore the user's ordinary desktop visibility.
- **FR-LIFE-004 (P0):** On unhandled process termination, no persistent overlay process or orphan overlay window shall remain.
- **FR-LIFE-005 (P1):** VEIL shall optionally start with the OS user session, but shall not silently enter Focus Mode on first boot or after an unexpected crash unless the user has explicitly enabled a recoverable policy.

### 3.2 Display management

- **FR-DISP-001 (P0):** VEIL shall enumerate all active displays at startup.
- **FR-DISP-002 (P0):** VEIL shall create one overlay surface per active display.
- **FR-DISP-003 (P0):** Overlay bounds shall cover the complete usable display area according to the selected full-screen policy.
- **FR-DISP-004 (P0):** VEIL shall detect display topology changes while running.
- **FR-DISP-005 (P0):** When a display is added, VEIL shall create an overlay on it if Focus Mode is active.
- **FR-DISP-006 (P0):** When a display is removed, VEIL shall release its associated overlay and update window/display mappings.
- **FR-DISP-007 (P1):** VEIL shall support mixed display resolutions, scaling factors, and portrait/landscape orientation.
- **FR-DISP-008 (P1):** VEIL shall expose a diagnostic view of detected displays and overlay status.

### 3.3 Window discovery and control

- **FR-WIN-001 (P0):** VEIL shall enumerate eligible top-level application windows.
- **FR-WIN-002 (P0):** The window list shall include stable runtime identity, process/application identity, title when available, display, and visibility state.
- **FR-WIN-003 (P0):** VEIL shall distinguish an application from individual windows belonging to that application.
- **FR-WIN-004 (P0):** VEIL shall allow the user to select an eligible window and present it above the overlay.
- **FR-WIN-005 (P0):** VEIL shall allow the user to hide the selected window behind the overlay without terminating its process.
- **FR-WIN-006 (P0):** VEIL shall preserve the overlay on all displays while an approved window is presented.
- **FR-WIN-007 (P0):** VEIL shall handle windows that close, minimize, become unresponsive, or change title while the selector is open.
- **FR-WIN-008 (P1):** VEIL shall support window thumbnails where the platform provides a safe and reliable capture API.
- **FR-WIN-009 (P1):** VEIL shall support multiple simultaneously visible allowed windows.
- **FR-WIN-010 (P1):** VEIL shall allow a window to be moved/resized without exposing the underlying desktop.

### 3.4 Application policy

- **FR-APP-001 (P0):** VEIL shall maintain an allow-list of applications permitted to appear above the overlay.
- **FR-APP-002 (P0):** A window shall not be shown above the overlay unless it matches an allowed application policy or an explicitly approved transient-window rule.
- **FR-APP-003 (P0):** The user shall be able to distinguish allowed applications from currently visible windows.
- **FR-APP-004 (P1):** The user shall be able to add/remove applications from the allow-list through Admin Settings.
- **FR-APP-005 (P1):** VEIL shall define behavior for child, modal, dialog, splash, and elevated windows; unknown transient windows default to hidden.
- **FR-APP-006 (P1):** VEIL shall not terminate or suspend disallowed applications as a side effect of hiding them.

### 3.5 Window Selector

- **FR-SEL-001 (P0):** A global hotkey shall open the Window Selector.
- **FR-SEL-002 (P0):** The selector shall show eligible windows and their allowed/hidden/visible state.
- **FR-SEL-003 (P0):** Selecting an entry shall activate and reveal that window if policy permits.
- **FR-SEL-004 (P1):** The selector shall support keyboard navigation and direct numeric shortcuts for the first nine entries.
- **FR-SEL-005 (P1):** The selector shall show a clear empty state when no eligible windows are detected.

### 3.6 Hotkeys

- **FR-HK-001 (P0):** VEIL shall register configurable global shortcuts for Focus On, Focus Off, Hide All, Selector, and Emergency Recovery.
- **FR-HK-002 (P0):** VEIL shall detect registration conflicts and report the conflicting shortcut.
- **FR-HK-003 (P0):** Emergency Recovery shall remain available independently of the normal overlay UI.
- **FR-HK-004 (P1):** Numeric window-selection shortcuts shall be configurable.
- **FR-HK-005 (P1):** Hotkeys shall be platform-adapted and documented; unsupported combinations shall be rejected.

Suggested defaults (subject to conflict testing):
- `Ctrl+Alt+V`: Focus On
- `Ctrl+Alt+U`: Focus Off
- `Ctrl+Alt+H`: Hide All
- `Ctrl+Alt+Tab`: Window Selector
- `Ctrl+Alt+Esc`: Emergency Recovery

### 3.7 Administration and configuration

- **FR-ADM-001 (P0):** Admin Settings shall require successful OS-level authentication as the designated Super Admin.
- **FR-ADM-002 (P0):** VEIL shall support exactly one configured Super Admin identity per machine installation.
- **FR-ADM-003 (P0):** Non-admin users shall not be able to modify protected machine-wide VEIL policy.
- **FR-ADM-004 (P0):** The Super Admin identity shall be provisioned through an explicit setup process.
- **FR-ADM-005 (P0):** VEIL shall not store the OS account password.
- **FR-ADM-006 (P0):** VEIL shall provide a safe, documented Super Admin recovery/reassignment procedure requiring OS administrator authority.
- **FR-ADM-007 (P1):** Admin changes shall be recorded in a local audit log.
- **FR-ADM-008 (P1):** Configuration shall support validated backup and restore.

### 3.8 Profiles and visual customization

- **FR-PROF-001 (P1):** VEIL shall support named workspace profiles.
- **FR-PROF-002 (P1):** A profile shall contain application allow-list, preferred visible windows, theme, and optional display layout.
- **FR-PROF-003 (P1):** Profile switching shall not terminate applications.
- **FR-UI-001 (P0):** The focus canvas shall provide a configurable hacker/mystic visual theme.
- **FR-UI-002 (P1):** The focus canvas shall display active window count and provide access to the selector.
- **FR-UI-003 (P1):** The user shall be able to reduce or disable animation and sound effects.

## 4. Non-functional requirements

- **NFR-PERF-001:** On reference hardware, Focus On/Off target is <= 1 second at the 95th percentile.
- **NFR-PERF-002:** Window reveal target is <= 500 ms at the 95th percentile, excluding application startup.
- **NFR-PERF-003:** Idle CPU target is <2% average over 10 minutes on reference hardware.
- **NFR-PERF-004:** Idle memory target is <150 MB on reference hardware; measure and revise after prototype.
- **NFR-REL-001:** An 8-hour soak test shall complete without an overlay leak or unhandled crash.
- **NFR-REL-002:** Failure of one display overlay shall not prevent teardown of other overlays.
- **NFR-REL-003:** Overlay teardown shall be idempotent and safe to call repeatedly.
- **NFR-SEC-001:** VEIL shall not claim to prevent access by local OS administrators.
- **NFR-SEC-002:** Protected configuration shall use OS file permissions/ACLs.
- **NFR-SEC-003:** Secrets, including OS passwords, shall not be stored by VEIL.
- **NFR-UX-001:** Every active Focus state shall have a visible indicator and a documented recovery shortcut.
- **NFR-UX-002:** Keyboard navigation shall be supported for all primary operations.
- **NFR-MAINT-001:** Shared business logic shall not directly call Win32, X11, or compositor APIs.
- **NFR-MAINT-002:** Platform-specific functionality shall be behind explicit interfaces and adapters.
- **NFR-PORT-001:** Windows and Linux shall share domain models, application policies, and most presentation logic.

## 5. State model

States:
- `Inactive`: no overlays; normal desktop.
- `Preparing`: enumerate displays and create overlays.
- `Focused`: overlays active; permitted windows may appear above them.
- `SelectorOpen`: Focused with selector UI visible.
- `Recovering`: emergency teardown and restore.
- `ErrorSafe`: overlays removed or disabled; diagnostic message available.

Allowed transitions:
- Inactive -> Preparing -> Focused
- Preparing -> ErrorSafe
- Focused <-> SelectorOpen
- Focused -> Inactive
- Focused/SelectorOpen -> Recovering -> Inactive
- Any runtime state -> Recovering on fatal shutdown signal

## 6. Acceptance test summary

| ID | Test | Expected result |
|---|---|---|
| AT-001 | Enable Focus with 1 display | Full display covered |
| AT-002 | Enable Focus with 3 displays | All 3 displays covered |
| AT-003 | Add display during Focus | New display covered automatically |
| AT-004 | Remove display during Focus | Removed display resources released |
| AT-005 | Select allowed app window | Window visible above overlay |
| AT-006 | Select disallowed app window | Window remains hidden; policy message shown |
| AT-007 | Hide active window | Overlay becomes visible over that window |
| AT-008 | Kill VEIL process | No orphan cover remains |
| AT-009 | Open Admin as non-admin | Access denied |
| AT-010 | Open Admin as designated Super Admin | OS authentication succeeds |
| AT-011 | Register conflicting hotkey | Clear conflict message; existing bindings remain intact |
| AT-012 | Run on Wayland | VEIL reports unsupported/restricted mode; does not pretend full support |

## 7. Traceability

Every FR/NFR must map to:
1. An implementation component.
2. At least one test case.
3. A release status in the implementation plan.

No requirement is considered complete solely because the UI has been implemented.
