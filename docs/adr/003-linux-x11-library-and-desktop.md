# ADR-003: Linux X11 library and desktop

- Status: Proposed
- Date: 2026-11-13
- Requirements: FR-DISP, FR-WIN, FR-LIFE; plan P3.3-P3.5

## Context
Linux adapter must not start before the core is proven on one platform (plan, delivery strategy).

## Decision
Phase 3 delivers session detection and honest Wayland handling now (`LinuxSessionInspector`, AT-012). The X11 binding (candidate: direct libX11/libXrandr P/Invoke vs a managed binding) and EWMH stacking are decided after the Ubuntu spike on GNOME/Xorg.

## Consequences / open items
No X11 display/window/hotkey adapter exists yet. `veil focus` on Linux reports unsupported rather than faking behaviour.
