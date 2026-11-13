# ADR-004: Global hotkey mechanism

- Status: Proposed (Windows)
- Date: 2026-11-13
- Requirements: FR-HK-001/002/003/005, AT-011

## Context
Hotkeys must survive an unresponsive UI and report conflicts.

## Decision
Windows: `RegisterHotKey` bound to the message thread's hidden window with MOD_NOREPEAT. A new registration replaces the old one only after it succeeds, so a conflict never loses an existing binding. Emergency Recovery runs on the message thread and does not touch the selector. `veil focus` refuses to start if Emergency Recovery cannot be registered.

## Consequences / open items
FINDING: `Ctrl+Alt+Tab` (the suggested Selector default) is already held on this Windows machine (error 1409). Defaults remain 'subject to conflict testing'; recommend an alternative Selector default (for example `Ctrl+Alt+Space`) before 1.0. Linux mechanism undecided (ADR-003).

**Decision update (installer build):** the built-in default for Window Selector is now `Ctrl+Alt+L` (`Ctrl+Alt+Tab`, `Ctrl+Alt+Space` and `Ctrl+Alt+S` were all already taken on the test machine; `L` registered cleanly), because `Ctrl+Alt+Tab` is reserved by Windows and the first installed build would otherwise ship without a working selector. The SRS/UI-UX suggestion (`Ctrl+Alt+Tab`) is kept in those documents as the originally suggested value; the shipped default differs. A conflicting default is still reported, never replaced silently.
