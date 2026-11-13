# 04 — UI/UX Design Specification

## 1. Experience goals

VEIL should feel like entering a quiet, controlled digital chamber rather than opening a conventional productivity dashboard.

Visual identity:
- Hacker / mystic / restrained cyberpunk.
- Deep charcoal and near-black backgrounds.
- Violet, indigo, and cyan as accent colors.
- Subtle geometry, fine grid lines, restrained glow.
- Avoid excessive animation, clutter, and decorative text.
- Clear contrast and legible typography take priority over atmosphere.

## 2. Design principles

1. Focus canvas is calm and minimal.
2. Primary actions are reachable by keyboard.
3. Status is explicit: Focus Active, number of windows, number of displays.
4. Allowed and currently visible are different states and must never be conflated.
5. Admin and security controls are separated from ordinary focus controls.
6. Animation and sound can be disabled.
7. UI must scale across mixed DPI and multiple monitors.

## 3. Main screens

### 3.1 Focus Canvas

Purpose: replace visual desktop noise with a quiet branded background.

Elements:
- VEIL wordmark.
- Minimal clock/date (optional).
- Focus status indicator.
- Active window count.
- Window Selector affordance.
- Current workspace profile (when profiles are enabled).
- Small display status indicator.
- Focus timer: the time left in the centre of the ring, with a thin arc that shrinks as the session runs down. When time is up the caption pulses softly ("Session complete") and the system plays one soft chime; Focus Mode is never ended by the timer.
- Quiet CPU and memory line, bottom left, muted and small (switch off in Settings > Appearance).

Motion (implemented): the glow of the ring breathes on a four second cycle, one dot drifts around the middle ring once every thirty seconds, and the overlay fades in over 0.4 s. Nothing flashes or moves fast. Reduced motion, or Windows' "Animation effects" turned off, removes all of it.

Avoid:
- System monitoring dashboards by default.
- Notification feeds.
- Unnecessary widgets.
- Large animated visual elements.

### 3.2 Window Selector

Required fields per item:
- Application icon.
- Application name.
- Window title.
- Display name/number.
- Allowed / Not Allowed.
- Visible / Hidden / Minimized / Unavailable state.

Actions:
- Reveal selected window.
- Hide selected window.
- Switch to next/previous.
- Filter/search.
- Show only allowed windows toggle.

Keyboard:
- Up/Down: move selection.
- Enter: reveal selected window.
- Esc: close selector.
- 1–9: select corresponding visible list entry when enabled.
- Tab/Shift+Tab: move between filter and list controls.

Thumbnail:
- Optional for 1.0.
- Must handle protected content and capture failures gracefully.
- Do not capture or retain screenshots beyond what is required for the live selector.

### 3.3 Admin Login

- Show the current OS account identity.
- Explain that only the configured Super Admin can enter Settings.
- Invoke supported OS authentication; do not build a password collection form that stores or forwards OS credentials.
- Show a clear access-denied state for non-admin users.
- Provide a documented OS-admin recovery route, not a hidden bypass.

### 3.4 Admin Settings

Navigation groups:
- General
- Displays
- Applications
- Hotkeys
- Appearance
- Workspace Profiles
- Security
- Diagnostics
- Backup & Restore

Settings must show whether they apply to the current user or the whole machine.

### 3.5 Diagnostics

Show:
- OS and desktop session type.
- VEIL version.
- Display list and overlay health.
- Window enumeration status.
- Hotkey registration status.
- Current Super Admin identity reference (not secrets).
- Log export action.

## 4. Interaction model

### Focus On
1. Validate supported session.
2. Arm recovery.
3. Discover displays.
4. Create overlays.
5. Apply allowed-window visibility policy.
6. Show active status.

### Focus Off
1. Enter recovery/teardown sequence.
2. Remove overlays.
3. Restore ordinary desktop visibility.
4. Preserve running application state.
5. Return to Inactive.

### Hide All
- Immediately place overlays above all work windows.
- Do not close or minimize applications unless a later policy explicitly requires it.

### Selector
- Open a compact panel on the primary display by default.
- Optionally position on the display containing the pointer.
- Keep selector itself above the overlay.
- Close after selection unless user enables persistent selector mode.

## 5. Default hotkeys

| Command | Suggested default |
|---|---|
| Focus On | Ctrl+Alt+V |
| Focus Off | Ctrl+Alt+U |
| Hide All | Ctrl+Alt+H |
| Window Selector | Ctrl+Alt+L (Ctrl+Alt+Tab is held by Windows) |
| Emergency Recovery | Ctrl+Alt+Esc |
| Next allowed window | Ctrl+Alt+Right |
| Previous allowed window | Ctrl+Alt+Left |
| Hide current window | Ctrl+Alt+Down |
| Start / stop focus timer | Ctrl+Alt+T |
| Next workspace | Ctrl+Alt+P |

All bindings are configurable. If a shortcut cannot be registered, do not silently replace it.

## 6. Visual tokens

| Token | Direction |
|---|---|
| Background | Near-black / charcoal |
| Primary accent | Violet / indigo |
| Secondary accent | Cyan |
| Success | Muted green |
| Warning | Amber |
| Error | Red |
| Surface | Layered dark gray |
| Border | Low-contrast cool gray |
| Typography | Modern sans-serif; monospace only for technical metadata |
| Motion | Short, subtle transitions; reduced-motion option |

## 7. Multi-monitor behavior

- Every active display receives a consistent focus canvas.
- Primary display may show the clock and selector affordance.
- Secondary displays remain visually quiet unless a permitted work window is assigned.
- Display identification in settings must be unambiguous (name, resolution, scale, primary marker).
- When a display is removed, windows assigned to it must be re-evaluated and not become permanently inaccessible.
- When a display is added, VEIL applies the active focus policy.

## 8. Accessibility and usability

- Keyboard operation for all primary controls.
- Text contrast suitable for prolonged use.
- Do not encode allowed/blocked state by color alone; include labels/icons.
- Respect OS scaling and text scaling where possible.
- Reduced motion and mute options.
- Avoid rapid flashing, intense glow, or high-frequency animation.
- Clear emergency recovery instructions in onboarding and diagnostics.

## 9. UX copy

Preferred terms:
- Focus Mode
- Focus Canvas
- Window Selector
- Allowed Applications
- Active Windows
- Super Admin
- Emergency Recovery

Avoid calling Focus Mode “secure lock”, “unbreakable lock”, or “kiosk” in user-facing copy.
