# ADR-002: Windows overlay and stacking

- Status: Proposed (spike result)
- Date: 2026-11-13
- Requirements: FR-DISP-002/003, FR-WIN-004/005/006

## Context
Windows has no per-window 'above overlay but below others' primitive; stacking is topmost-band based.

## Decision
One WS_POPUP window per monitor, WS_EX_TOPMOST | TOOLWINDOW | NOACTIVATE, created on a single dedicated message-pump thread. Reveal = restore if minimized, `SetWindowPos(HWND_TOPMOST)`, then `SetForegroundWindow`. Hide = `SetWindowPos(HWND_NOTOPMOST)` so the topmost overlay covers it. Process uses per-monitor-v2 DPI awareness so bounds are physical pixels.

## Consequences / open items
Verified on this machine (2 displays): Focus On created 2 overlays, Focus Off and a hard process kill both left 0. NOT yet verified: exclusive fullscreen, elevated windows, UAC, Alt+Tab ordering, mixed DPI, hot-plug on real hardware (P2.7, AT-003/004 on hardware). A window revealed *before* a later topmost window is created is ordered by creation, so reveal always runs after overlay creation.
