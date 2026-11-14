# ADR-010: The application, not one window, is the unit of visibility

- Status: Accepted
- Date: 2026-11-14
- Requirements: FR-WIN-001..007, AT-005/AT-006; builds on ADR-002 (stacking) and ADR-009 (never block)

## Context
Revealing a window made only that one window TOPMOST. Windows enumeration skips owned windows (dialogs, popups), so an application's dialog (for example the NdevTool WATCH_BUF window) was never layered with its main window: it opened behind the overlay, or ended up on a different layer from its owner. The dock tile stands for one window per application and toggles it, so for an application with several windows a click showed one and hid another ("sometimes this one hides, sometimes that one").

## Decision
1. **Reveal and Hide act on the whole application** (every visible window of the same process, owned ones included). Policy is still decided per `ApplicationId`, so nothing new is allowed.
2. **Dialogs opened later follow.** While Focus Mode shows an application, `FocusService.KeepShownApplications` (every 400 ms, overlay up only) lifts any window of that process that is not yet TOPMOST, without activating it and without reordering windows that already are.
3. **The revealed set is per application**: revealing or hiding one window adds or removes the other listed windows of that application too, so the dock tile toggle is consistent.
4. **Tray mode** lowers the application's other windows when one is minimized; owned dialogs minimize with their owner.
5. ADR-009 still holds: all layering calls are asynchronous, hung applications are skipped, Emergency Recovery is unchanged.

## Consequences
One extra top-level enumeration (3 s budget) per reveal/hide and per tick while an application is shown. A dialog can still sit behind the overlay for up to ~400 ms after it appears.

## Verification and open items
Unit tests: `Showing_and_hiding_an_application_covers_all_its_windows`, `Shown_applications_are_kept_consistent_only_while_the_overlay_is_up`. Not yet verified live with NdevTool + WATCH_BUF.
