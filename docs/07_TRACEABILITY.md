# 07 — Requirement Traceability and Implementation Status

Status as of 2026-11-13. Legend:

- **Live** — implemented and exercised on a real Windows machine (2 displays).
- **Unit** — implemented in shared code and covered by unit tests with fakes; not yet run against real platform behaviour.
- **Partial** — part of the requirement exists.
- **Open** — not implemented.

"Done" in the SRS sense (traceability section 7) additionally needs platform integration tests, so nothing here is release-complete.

## Functional requirements

| ID | Pri | Component | Tests | Status |
|---|---|---|---|---|
| FR-LIFE-001 | P0 | `FocusService` starts `Inactive`; only `focus` creates overlays | `FocusServiceTests` | Live |
| FR-LIFE-002 | P0 | `FocusOn` / `FocusOff` / host exit | `FocusServiceTests` | Live (Exit = process end) |
| FR-LIFE-003 | P0 | `FocusService.Dispose`, final `EmergencyRecover` | `Dispose_destroys_overlays` | Live |
| FR-LIFE-004 | P0 | Overlays are owned by the process | smoke: hard kill left 0 overlays | Live (Windows only; not automated) |
| FR-LIFE-005 | P1 | `veil run` agent (idle at start), HKCU/HKLM `Run` entry written by `installers/windows/install.ps1` | `agent-smoke`, `install-test` (scripts, not in repo CI); `AutoStartTests` | Live for the per-user install (starts idle, never covers the screen); machine install dry-run only |
| FR-DISP-001 | P0 | `WindowsDisplayService.GetDisplays` | smoke (`veil diag`) | Live |
| FR-DISP-002 | P0 | `OverlayManager` + `WindowsOverlayHost` | `OverlayManagerTests`; smoke (2 displays → 2 overlays) | Live |
| FR-DISP-003 | P0 | Overlay bounds = monitor rect, per-monitor-v2 DPI | smoke | Partial (mixed DPI unverified) |
| FR-DISP-004 | P0 | `WM_DISPLAYCHANGE`, 300 ms debounce | `Topology_change_*` (fakes) | Unit (no real hot-plug run) |
| FR-DISP-005 | P0 | `OverlayManager.Reconcile` | `Added_display_*` | Unit |
| FR-DISP-006 | P0 | `OverlayManager.Reconcile` | `Removed_display_*` | Unit |
| FR-DISP-007 | P1 | scale factor captured per display | — | Partial |
| FR-DISP-008 | P1 | `veil diag` | smoke | Partial (CLI only) |
| FR-WIN-001 | P0 | `WindowsWindowService.GetWindows` | smoke (`veil windows`) | Live |
| FR-WIN-002 | P0 | `WindowInfo` (hwnd runtime id, exe-path identity) | smoke | Live |
| FR-WIN-003 | P0 | `ApplicationId` vs `RuntimeId` | `ApplicationPolicyTests` | Live |
| FR-WIN-004 | P0 | `FocusService.Reveal` → topmost | `Allowed_window_is_revealed`; smoke (`veil focus --reveal`) | Live (one classic Win32 app, `charmap.exe`) |
| FR-WIN-005 | P0 | `FocusService.Hide` → notopmost | `Hide_all_*`; smoke (Hide All) | Live (Hide All and per-window `HideCurrent`) |
| FR-WIN-006 | P0 | overlay never destroyed by reveal | `Reveal_failure_keeps_the_overlay` | Unit |
| FR-WIN-007 | P0 | stale handles rejected (`IsWindow`) | `Stale_window_is_reported_not_thrown` | Unit |
| FR-WIN-008 | P1 | — | — | Open (P4.6, privacy review first) |
| FR-WIN-009 | P1 | reveal is additive | `FocusService` | Partial |
| FR-WIN-010 | P1 | — | — | Open |
| FR-APP-001 | P0 | `ApplicationPolicy`, config rules | `ApplicationPolicyTests` | Live (rule set via CLI) |
| FR-APP-002 | P0 | default-deny in `Evaluate`, enforced in `Reveal` | `Disallowed_window_*` | Unit |
| FR-APP-003 | P0 | `SelectorEntry` (Allowed ≠ State) | `WindowSelectorTests` | Unit |
| FR-APP-004 | P1 | `AdminService.UpdateAllowList`, `veil admin allow`, Applications page (`ApplicationsPageViewModel`) | `AdminServiceTests`, `AdminViewModelTests`; headless screenshots | Partial (editor built and rendered headless; not launched from the host, typed paths only) |
| FR-APP-005 | P1 | `WindowKind` + `TransientWindowPolicy` | `Transient_*` | Partial (Windows adapter reports every top-level window as `Normal`; owned windows are excluded rather than classified) |
| FR-APP-006 | P1 | hide never terminates | `Hide_all_*` | Unit |
| FR-SEL-001 | P0 | `Selector` hotkey | — | Partial (Ctrl+Alt+Tab is OS-held, see ADR-004) |
| FR-SEL-002 | P0 | `WindowSelectorService` | `WindowSelectorTests` | Unit |
| FR-SEL-003 | P0 | `FocusService.Reveal` | `FocusServiceTests` | Unit |
| FR-SEL-004 | P1 | `GetByNumber`, `Cycle` | `Numeric_shortcuts_*` | Partial (no UI) |
| FR-SEL-005 | P1 | empty list | `Empty_state_*` | Partial (no UI) |
| FR-HK-001 | P0 | `WindowsHotkeyService` | smoke | Live |
| FR-HK-002 | P0 | error 1409 reported with chord | smoke (Ctrl+Alt+Tab conflict) | Live |
| FR-HK-003 | P0 | handler on message thread; start refused without it | `Emergency_recovery_*` | Unit (not tested with a hung UI) |
| FR-HK-004 | P1 | — | — | Open |
| FR-HK-005 | P1 | `KeyChord.TryParse` | `HotkeyTests` | Unit |
| FR-ADM-001 | P0 | `AdminService.Authorize` | `AdminServiceTests` | Partial (no OS re-auth prompt, ADR-005) |
| FR-ADM-002 | P0 | `Provision` refuses a second identity | `Provisioning_records_exactly_one_*` | Unit |
| FR-ADM-003 | P0 | authorize on every protected change; ACL | `Non_admin_cannot_*`; smoke (denied + audited) | Live (denial path) |
| FR-ADM-004 | P0 | `veil admin provision` | `Provisioning_needs_os_*` | Partial (elevated success path not run live) |
| FR-ADM-005 | P0 | no password handling exists | `Config_never_contains_credential_fields` | Live |
| FR-ADM-006 | P0 | `AdminService.Reassign` (OS admin) | `Reassignment_*` | Unit; recovery doc still to write |
| FR-ADM-007 | P1 | `FileAuditLog` | `FileAuditLog*Tests`; smoke | Live |
| FR-ADM-008 | P1 | `Export` / `Import` | `Export_then_import_*`, `Rejected_import_*` | Unit |
| FR-PROF-001..003 | P1 | — | — | Open (Phase 6) |
| FR-UI-001 | P0 | `VeilTheme` tokens (`assets/theme`), themed overlay canvas and selector | `ThemeTests` (46); screenshots | Partial (themed Windows overlay + selector; no Avalonia screens yet; see `08_DESIGN_TOKENS.md`) |
| FR-UI-002/003 | P1 | — | — | Open |

## Non-functional requirements

| ID | Status |
|---|---|
| NFR-PERF-001/002 | Open — Focus On/Off took well under the 4 s smoke window, but not measured (p95 needed) |
| NFR-PERF-003/004 | Open — no profiling yet (P7.2) |
| NFR-REL-001 | Open — no soak test (P7.1) |
| NFR-REL-002 | Unit — `Teardown_is_idempotent_and_survives_a_failing_overlay` |
| NFR-REL-003 | Unit + Live |
| NFR-SEC-001 | Documented — in `--help`, diagnostics notice, ADR-005 |
| NFR-SEC-002 | Live (ACL applied; standard-user write denied) |
| NFR-SEC-003 | Live — no credential fields exist |
| NFR-UX-001 | Partial — overlay text shows "Focus Active" and the recovery shortcut; confirmed to exist, not visually reviewed |
| NFR-UX-002 | Open — no UI |
| NFR-MAINT-001/002 | Met — Domain/Application reference no platform assembly; native code only in `VEIL.Platform.*` |
| NFR-PORT-001 | Partial — shared layers build and test on the .NET SDK; the Linux adapter is detection-only |

## Acceptance tests (SRS 6)

| ID | Result |
|---|---|
| AT-001 | Live (1 of 2 displays configuration not separately tested; 2-display run covered) |
| AT-002 | Unit for 3 displays; Live for 2 |
| AT-003 / AT-004 | Unit only — real hot-plug not run |
| AT-005 | Live — `charmap.exe` allow-listed and revealed; z-order top-to-bottom had it above both overlays |
| AT-006 | Live — Explorer windows (not allow-listed) stayed below the overlays. A refused reveal was only unit-tested |
| AT-007 | Live — real `Ctrl+Alt+H` (injected via `keybd_event`) moved `charmap.exe` from above the overlays to below them; process stayed alive, overlays stayed, log said "Hid 1 window(s)". Per-window `Hide` (not Hide All) not run live |
| AT-008 | Live (Windows): hard kill while focused left 0 overlays |
| AT-009 | Live (denied, audited) |
| AT-010 | Unit only — requires provisioning as an elevated OS administrator |
| AT-011 | Live (conflict reported with chord, other bindings registered) and Unit |
| AT-012 | Unit (simulated environment); not run on a real Wayland session |

## Phase status

| Phase | Status |
|---|---|
| 0 Baseline | Done except: Windows+Ubuntu CI runners unconfirmed (`.gitlab-ci.yml` written, never run); versions pinned |
| 1 Overlay engine | P1.1–P1.4, P1.6 (Windows) Live; P1.5 hot-plug Unit only |
| 2 Window manager | P2.1–P2.4 implemented; P2.5–P2.7 not done |
| 3 Linux/X11 | P3.1 contracts, P3.5 Wayland detection only |
| 4 Selector + hotkeys | Logic and hotkeys done; no selector UI, no thumbnails |
| 5 Super Admin | Core + config + audit + backup/restore done; OS re-auth prompt, recovery procedure doc, installer provisioning open |
| 6 UX + profiles | Open |
| 7 Hardening | Open |

## Next/Previous run (2026-11-13)

Two `charmap.exe` processes, both allowed. Injected `Ctrl+Alt+Right` x3 then `Ctrl+Alt+Left`: revealed A, then B (A went back below the overlays), then A (wrap-around), then B. Exactly one window was above the overlays after each press. Only one app binary was used, so cycling across different applications and a stale handle during a cycle were not run live.

### Per-window Hide (2026-11-13)

**Spec change to review:** the SRS/UI-UX hotkey list has no per-window hide, only Hide All and the selector's "Hide selected window" action (which has no UI yet). To make `FocusService.Hide` testable I added `HotkeyCommand.HideCurrent`, default `Ctrl+Alt+Down`, which hides the most recently revealed window. This is my proposal, not an approved requirement; it needs an ADR/spec decision (or removal once the selector UI exists).

`charmap.exe` and `winver.exe` both revealed at start (both above the overlays). `HideCurrent` #1 sent winver below the overlays while charmap stayed on top; #2 sent charmap below; #3 reported "Nothing to hide". Both processes stayed alive throughout and the overlays were never disturbed. Hiding a window that has vanished: unit test `Hide_of_a_vanished_window_reports_and_forgets_it` and a live run (below).

**Hide of a vanished window (live):** revealed `charmap.exe`, killed it, `HideCurrent` -> "Window is no longer available.", no crash; a second `HideCurrent` -> "Nothing to hide." (the dead handle was forgotten); overlays stayed at 2; a following Next revealed `winver.exe` normally. **Handle reuse (found and partly fixed):** a scratch harness churned create/destroy until the OS reissued the dead window's exact HWND (after 32,766 cycles, about 88 s, twice). Before the fix, `WindowsWindowService.Hide(oldId)` returned true and removed TOPMOST from the unrelated new window. Fix: `RuntimeId` is now `HWND-PID-TID` and both must still match before Reveal/Hide. Re-run with the new window on another thread: `Hide` returned false and the new window stayed topmost. Per-window Hide and Next/Previous (winver + charmap, same 5-key sequence as before) re-run live after the id change with identical results; 103 unit tests pass.
**Residual (confirmed live):** same-thread reuse re-run on the fixed code: the new window got id `3D0306-4396-27916`, identical to the dead one, and `Hide` returned true and removed its TOPMOST (after 32,766 cycles, 87 s). Handles alone cannot detect this: HWND, PID and TID all match, and Win32 exposes no per-window creation stamp. A class-name check would not help against same-class churn. Impact stays limited to demoting a window from the topmost band; nothing is closed or hidden. The harness is not part of the repo and is not automated.

**Hide while minimized (run live):** revealed `charmap.exe` (topmost=True), minimized it (topmost flag stays True), `HideCurrent` -> log "Window hidden behind the overlay", window stays minimized, topmost cleared, process alive, both overlays intact; a following Next restored it and raised it above the overlays again. Observation: a minimized window keeps the TOPMOST flag until hidden, so hiding is what actually removes it from the overlay's band; minimizing alone is not tracked by VEIL (it remains in its internal revealed set until hidden).

### Selector hotkey on a different key (2026-11-13)

Default `Ctrl+Alt+Tab` is OS-held, so Selector was rebound in `config.json` (validated by `DefaultHotkeys.Validate` on load). `Ctrl+Alt+Space` was also taken by another application (VEIL reported the conflict; my injected press then triggered that application, so injected keys can fire other apps' hotkeys). `Ctrl+Alt+F9` registered with no error. Injected press -> `[hotkey] Selector`, nine entries listed with Allowed/Not allowed and Hidden/Visible; after Next the revealed window showed `Allowed/Visible`. Overlays and z-order were unchanged by opening the selector, and the state returned to Focused.
**Findings:** (1) entries were numbered by z-order, so numbers changed after a reveal. **Fixed:** order is now allowed first, then application name, then runtime id (never z-order, reveal state or title); 2 new unit tests, 105 pass; live re-run: the list kept the same order before and after Next (only the state changed to Visible). Side effect: Next/Previous now cycle in this order (alphabetical by app) instead of z-order. Not decided: whether minimized/non-allowed windows should sort differently, and the order among several windows of one app is by hwnd, which is stable but not meaningful to a person. (2) The list includes every eligible window with its full title (e.g. browser tab titles); fine on a console, but the real selector UI and any logging must respect the "titles may be sensitive" rule in the security design. (3) Garbled non-ASCII in the captured output is the capture pipe's code page, not verified as a VEIL fault. Console selector only: no selector UI, no Enter/Esc/1-9 handling yet.

### Selector keys: Up/Down, Enter, Esc, 1-9 (2026-11-13)

New: `SelectorSession` (Application) with 16 unit tests (121 total pass), and `IHotkeyService.RegisterTransient` so the Windows adapter can register **bare** keys globally only while the selector is open. The host releases them whenever the state leaves `SelectorOpen` (Esc, a reveal, Focus Off, Emergency Recovery).

Live (injected keys, Selector on `Ctrl+Alt+F9`): F9 opened with entry 1 marked; Down moved to 2; Enter revealed About Windows and closed the selector (z-order confirmed above the overlays); F9 again, `1` revealed Character Map directly; F9, `3` (a non-allowed window) -> "Not revealed: Application is not on the allow-list.", window stayed hidden and the selector stayed open (AT-006); Esc closed it. **Bare keys released:** Down and Esc pressed while the selector was closed produced no `[selector]` log, i.e. they were not swallowed. 0 overlays after exit.

Behaviour to review: (1) while the selector is open VEIL takes Up/Down/Enter/Esc/1-9 system-wide, so another app cannot receive them until it closes; this is the console host's workaround for having no focusable selector window and should go away when a real selector window exists. (2) A selector reveal is additive, so revealing a second window leaves the first visible (Next/Previous replace; Selector does not). (3) After a refused number key the highlighted row is not reprinted. (4) Selection restarts at entry 1 on every open. Not done: filter/search, Tab/Shift+Tab, "show only allowed" toggle, `Persistent` mode in the host, and any UI.

### Selector filter/search, Tab, only-allowed toggle (2026-11-13)

New in `SelectorSession` (11 more unit tests, 132 total pass): three Tab stops (Filter, List, "only allowed" toggle) with Shift+Tab reverse; typing a letter in the list jumps to the filter; the filter matches application name or title, case-insensitive, max 64 chars; digits are shortcuts in the list but text in the filter; Backspace edits; Enter in the filter moves to the list; Enter/Space on the toggle flips it; the selection follows the same window when the filter changes; state resets on every open. The host registers bare letters, digits, Space, Backspace, Tab and Shift+Tab as transient global keys while the selector is open (released on close).

Live (injected keys): typing `i`,`n` narrowed 20 windows to 16 then 6 (allowed first); Tab moved to the list; Enter revealed About Windows (above the overlays). Reopened: Shift+Tab went to the filter; `1` became filter text (3 matches, nothing revealed); Backspace restored all 20; Tab, Tab reached the toggle; Space turned "only allowed" on and the list became the 2 allowed windows; Esc closed. No overlay remained after the process ended.

**Environmental issue (cause confirmed):** a `w` pressed right after F9 or after Down never reached VEIL (3 of 3 times), while `w` after another letter, and every other key tried (`i`, `a`, `n`, Down, Enter, Esc, Tab, Space, Backspace, digits), did. `UniKeyNT.exe` (a Vietnamese Telex keyboard hook, where `w` is a special key) is running on this machine and is the likely cause; **Confirmed afterwards:** with UniKey stopped, the same sequences delivered `w` both right after F9 and after Down (`Filter 'w'`, `'wi'`, `'win'`), and UniKey was restarted from its own path. Product implication: a hook-based input method can interfere with bare-letter global hotkeys, which is one more reason the real selector should be a normal focusable window with its own text box rather than global key capture.

Other notes: when the filter changes, the highlight follows the same window, so after clearing a filter the highlight can sit far down the list (it moved to entry 7); the host only prints the first 12 rows; "show only allowed" has no hotkey outside Tab navigation; the global capture of letters/digits while open is broader than before.

### Real focusable Selector window (2026-11-13)

New: `ISelectorView` (Abstractions), `SelectorController` (Application, 17 unit tests with a fake view; 148 total pass) and `WindowsSelectorView` (native EDIT + LISTBOX + checkbox in a topmost popup; Tab/Shift+Tab/Enter/Esc via `IsDialogMessage` on the message thread, digits via `WM_VKEYTOITEM`). The host now uses it and **no longer captures global keys**; the earlier console selector and its bare-key hotkeys are gone from the host (`IHotkeyService.RegisterTransient` and the text-mode `SelectorSession` API are still in the code, now unused by the host).

Live, UniKey **running**, selector on `Ctrl+Alt+F9`, verified by Win32 state and a screenshot (selector centred above the overlay, text box, checkbox, 20-row list with Allowed/Not allowed and Hidden/Minimized/Visible text, status line, Vietnamese titles rendered correctly): F9 opens it with real foreground and focus in the text box; typing `i`,`n` reaches the box intact (`'in'`, rows 20 -> 6) with UniKey on; Tab -> list, Down moves the highlight, Tab -> checkbox, Space turns "only allowed" on (rows 1), Shift+Tab -> list, Enter reveals About Windows (z-order above the overlays, selector gone, foreground = the revealed app); second open: `a`, Tab, digit `1` reveals directly; Esc closes; F9 then `Ctrl+Alt+Esc` removes the selector window and both overlays and the process exits. A lone `w` becomes `ư` under Telex, i.e. normal IME behaviour rather than a dropped key.

**Bug found by checking visibility, not just focus:** the first version passed all the keyboard checks while the window was **invisible** (`IsWindowVisible=False`; the screenshot showed only the overlay). Cause: a process's first `ShowWindow` call takes its show state from STARTUPINFO, and the test started `veil` hidden (same as a scheduled task or auto-start). Fixed with `SetWindowPos(SWP_SHOWWINDOW)`. The keyboard results from that invisible run were discarded and everything was re-run against the visible window. Lesson for future live tests: assert visibility and take a screenshot, do not infer them from foreground or focus.

Not done: dark/violet theming (it uses the stock light dialog look, contrary to UI/UX section 1), DPI-aware sizing and per-display placement (fixed 760x480 centred on the primary screen), app icons and thumbnails (FR-WIN-008), persistent-selector option, and any test with a screen reader.

### Selector placement, DPI scaling and multiple displays (2026-11-13)

**What could and could not be tested.** This machine has two 1920x1080 displays, both at 100%. Changing system display scaling is a system setting, so I did not do it; **no real high-DPI or mixed-DPI hardware run exists.**

Implemented: `SelectorPlacementCalculator` (Application) picks the display (primary by default, the display under the pointer with the new preference `FocusPreferences.SelectorOnPointerDisplay`) and scales the 760x480 design size by that display's scale factor, clamped to 90% of the display; the view takes the placement, creates a Segoe UI 9 pt font at that scale and lays controls out in scaled units. 20 new unit tests (168 total pass) cover scales 1.0-3.0, 4K, small and portrait displays, monitors left of and above the primary (negative coordinates), mixed DPI (scale comes from the chosen display), display edges, pointer outside all displays, no primary flag, invalid scale and empty display list.

Live on the two real displays: default -> selector at (580,200) on the primary even with the pointer on the secondary; preference on, pointer on the secondary -> selector at (2500,200)-(3260,680), wholly inside the secondary (1920+580), overlay still covering both, screenshot checked.

Rendering at a larger scale was exercised with the diagnostic override `VEIL_SCALE_OVERRIDE=2` on the 100% displays: window 1520x960 inside either display, and the screenshot shows the font, text box, checkbox row, list rows and status line all enlarged proportionally with the list filling the window. **This simulates our own scaling code only; it is not Windows' per-monitor DPI virtualization.**

Findings: (1) the focus overlay's "VEIL - Focus Active" and "Emergency Recovery" text used the default GDI font and did not scale. **Fixed:** each overlay now creates Segoe UI fonts (16 pt title, 11 pt hint) at its display's scale, sized margins from the display height, and releases the fonts when the overlay is destroyed. Checked by screenshot on the secondary display at 1x and at the 2x override: title about 210 px -> 400 px wide, hint about 230 px -> 437 px wide, text still centred and not clipped. At 100% the text is now larger and cleaner than before (default font ~8 pt). Not verified: real 200% hardware and per-display font choice when displays have different scales (each overlay takes its own display's scale, untested with real mixed DPI). (2) At override 2x the caption bar and the checkbox square stay at 100% size because the OS draws them at the real DPI; at a real 200% the OS scales them, which I could not confirm. (3) Moving the pointer to another display after the selector opened does not move it (placement is computed on open). (4) A display-scale change or hot-plug while the selector is open is not handled; it keeps its old size/position until reopened. (5) The test harness first silently failed to set the override variable, which looked like a product bug until I checked the script.

### GDI/USER handle leak check (2026-11-13)

Scratch harness (not in the repo) drove one process through 300 Focus On/Off cycles on the 2 real displays (40 ms held per cycle so paint runs and the per-overlay fonts are used), then 300 selector open/close cycles, reading `GetGuiResources` for GDI and USER objects, plus handle count and private bytes. Negative control: deliberately leaking 50 fonts moved the GDI count by exactly +50, so the measurement can see a leak.

Result: Focus On/Off GDI 52 -> 52 and USER 5 -> 5 (the 52 includes the 50 deliberately leaked control fonts); selector GDI 67 -> 67 and USER 17 -> 17; 0 overlay windows left. Handle count 256 -> 256 for Focus; 299 -> 301 for the selector (+2 appearing between cycle 150 and 200 and flat to 300). Private memory wandered between 8.5 and 12.4 MB with no upward trend. So no GDI or USER leak at this scale. Not covered: 300 cycles is not the 8-hour soak (NFR-REL-001), the harness never revealed or hid windows or ran the hot-plug path, and the selector was opened with an empty allow-list. The +2 handles are unexplained but did not grow.

### Theme and styling from `resources/images` (2026-11-13)

See `docs/08_DESIGN_TOKENS.md` and ADR-008. Implemented: JSON tokens (colours sampled from the six reference images, semantic status colours chosen), `VeilTheme`/`ThemeColor` in Domain, themed overlay (violet HUD ring, wordmark, cyan focus indicator) and a themed owner-drawn selector (number chip, title, application, policy and state pills, dark DWM caption), generated Avalonia resources and styles. 214 unit tests pass (46 for theme, including contrast thresholds and AXAML sync). A headless Avalonia load of the generated files passes 12 checks.

Findings: (1) a unit test caught `text.muted` at 4.49:1 on `bg.panel`, so the token was lightened; (2) the Avalonia compiler accepted an unresolved `StaticResource` and a `double` used as `Padding`, so only the runtime load found the latter, hence `tools/AxamlCheck`; (3) `FillRect` was declared in the wrong DLL and crashed `veil` on the first themed paint (the overlays vanished with the process, as designed) - fixed; (4) violet `#9235EC` is only 3.8:1 on the dark backgrounds, so it is restricted to fills/borders/glow. Not verified: real high-DPI hardware, Windows 10, light/high-contrast system themes, a colour-blindness simulation.

Follow-up screenshot (Character Map revealed, Tab from the text box to the list, Down): the Character Map row shows the cyan `Visible` pill next to the green `Allowed` pill, and the selected row 2 has the **cyan** left bar on the raised background (the bar is violet while the text box has focus, as in the earlier shot). All five pill kinds and both bar colours have now been seen on screen.

### Admin Settings window with the new theme (2026-11-13)

New project `VEIL.Presentation` (Avalonia 12.1.5, MVVM, CommunityToolkit.Mvvm) with `VeilApp` (Fluent dark re-coloured from the tokens), `AdminWindow` and view models: Admin Login/denied state, a nine-group navigation that states each group's scope (whole machine / this user), working **Applications** (allow-list) and **Hotkeys** editors that save through `AdminService`, read-only **Security** and **Diagnostics**, and honest "Planned" placeholders for General, Displays, Appearance, Workspace Profiles and Backup & Restore. 27 new unit tests (241 total pass) cover access states, allow-list edits and hotkey validation.

`tools/AdminShots` renders the window headless with Skia and asserts behaviour (20 checks pass); eight screenshots were reviewed (not-provisioned, access-denied, applications, empty applications, hotkey conflict, security, diagnostics, planned group). Behaviour confirmed: a non-admin account never gets a settings page (AT-009); the Super Admin does (AT-010, with a fake OS identity, not the real session); a save after the OS account changed is refused and the stored list is unchanged (policy re-authorized on every write); a conflicting or invalid hotkey set is refused, names the chord and leaves storage untouched (AT-011); Emergency Recovery cannot be disabled even if the UI is bypassed.

**Found only by looking at the screenshots:** after a refused hotkey save the red error message sat below the fold (the page scrolls), so a user would have seen nothing; the action bar and status are now pinned below the scrolling list. Also the first checkbox of each application row had no visible label (now "Allowed"). The unit-test and harness checks had all passed before either fix.

**Launched by the host (2026-11-13):** `veil settings` starts the Avalonia desktop lifetime (`VeilApp.MainWindowFactory`) and shows `AdminWindow` built from the real `AdminService`, config store and Windows identity. Live on this machine (screenshots of the real desktop): with no Super Admin configured it shows the "No Super Admin is set up" card with the real account and SID; with the config naming the current Windows account as Super Admin (written by hand into a scratch `VEIL_CONFIG_DIR`, because provisioning needs an elevated step I did not run) it shows the Applications page with the "Super Admin" badge. Both windows had title `VEIL Settings`, were responsive, and exited with code 0 when closed. The audit log recorded `DeniedNotProvisioned` then `Granted` (per-user fallback file, since the protected directory is not writable here). Remaining: `veil.exe` is still a console-subsystem app, so a console window opens next to the settings window; the long SID line wraps awkwardly in the sidebar; no `veil admin provision` run from the window (provisioning stays a CLI/elevated step); keyboard-only use, tab order and screen readers not checked; the window was not tried on the secondary display or at high DPI. Earlier headless-only caveats that still hold: identities in the headless screenshots are fake; no keyboard-only or screen-reader pass; no high-DPI or 2x render of this window; live per-row duplicate-hotkey highlighting (conflicts only appear on Save, and the message uses internal command names such as `FocusOn`); Security shows no live OS re-authentication; Displays, Appearance, Profiles, General and Backup & Restore are placeholders; the Applications editor takes a typed path (no file picker or running-window picker).

### Admin Settings on the secondary display and at high effective scale (2026-11-13)

`veil settings [--display primary|pointer]` now opens on the display under the pointer (default) or the primary display, at its design size 1180x780 dip scaled by that display's scale factor and clamped to 90% of it (`SelectorPlacementCalculator.PickDisplay`/`CenterWindow`, 6 new unit tests, 247 total pass: 100%, 4K 200%, a small 125% display, negative-origin and mixed-DPI displays, pointer outside all displays).

Live on the two real displays (real window rectangles measured, screenshots viewed): pointer on the primary -> window at (370,150) on the primary; pointer on the secondary -> (2290,150) on the secondary and rendered correctly; `--display primary` with the pointer on the secondary -> primary. All responded and exited 0.

**High DPI could not be tested for real.** Both displays are 100% and changing system scaling is a system setting I did not touch. I tried forcing 2x with `VEIL_SCALE_OVERRIDE` plus Avalonia's `AVALONIA_GLOBAL_SCALE_FACTOR`: VEIL computed the 2x placement (864x486 dip, clamped from 1180x780) but the real window measured 880x525 px, so **Avalonia's Win32 backend ignored the scale variable** and drew at 1x. What that run did show is what a 1080p display at 200% would leave the window (864x486 dip), which exposed three layout bugs: the sidebar overlapped (Workspace Profiles over the "Signed in" block, Security/Diagnostics/Backup unreachable), the Hotkeys rows overflowed horizontally (the Enabled checkbox fell outside the window), and application names were cut without an ellipsis. Fixed: the whole sidebar is now one scrolling column, hotkey rows use proportional columns, long names/paths trim with an ellipsis, and "Dialogs follow this app" became "Include dialogs" (full wording in a tooltip). A headless screenshot at 864x486 plus a rerun of the real window at that size confirm no overlap and no horizontal overflow.

Still unverified: genuine OS scaling or per-monitor DPI change while the window is open, mixed-DPI drag between displays, Windows' own scaling of the title bar and fonts at 200% (Avalonia handles it; I only exercised our sizing maths), a window dragged to a different display, and keyboard/screen-reader use. Minor: the window is centred by its client size, so the outer frame sits about 8 px right and 20 px low of true centre.

### Installer, start with Windows, resident agent (2026-11-13)

See ADR-007 and `installers/windows/README.md`. New: `veil run` (resident agent: hotkeys + tray icon, idle at start, single instance, rolling log), `veil-agent.exe` (windowless build of the same code), `veil autostart status|enable|disable`, `veil admin provision --account`, `InstallInfo` (`install.json`), `WindowsAutoStart` (HKCU/HKLM `Run`), `FocusRuntime` (hotkey handling shared by `veil focus` and the agent), icons from the design tokens, and `build.ps1` / `install.ps1` / `uninstall.ps1` / `.cmd` wrappers. 273 unit tests pass (11 new for install info and auto-start, including a real HKCU round trip).

Verified live on this PC (per-user scope, installed through `Install-VEIL-Trial.cmd`, 40 checks): dry run changes nothing; install copies a self-contained build, writes `install.json`, the HKCU `Run` value (`"...eil-agent.exe" run`), three Start Menu shortcuts and a Programs and Features entry, names the installing user as Super Admin, and **starts the agent immediately, not elevated, without covering the screen**; a tray icon named "VEIL - ready (idle)" exists (found through UI Automation in the overflow area); executing the registry command exactly as Windows would brings the agent back idle (simulated sign-in, **not a real logoff/reboot**); `Ctrl+Alt+V`/`U`/`Esc` work from the installed copy; killing the agent while focused leaves no overlay; a second launch exits 0 without a second agent; reinstall over the top keeps settings; uninstall removes the process, folder, Run value, shortcuts and registry entry, keeps settings by default, removes them with `-RemoveData`, and is harmless when nothing is installed or when run from the build package (it finds the install through the registry). The PC was returned to its previous state afterwards.

Found while doing this: (1) the Run-from-package uninstaller would have deleted the package folder (it treated its own folder as the install folder); fixed so only a folder holding `veil-agent.exe` counts. (2) Windows PowerShell 5.1 has no `$PSScriptRoot` inside `param()` defaults when run with `-File`; fixed. (3) `dotnet publish` has no `--no-incremental`; the build now resets the App project's obj/bin between the two flavours (it leaves `src/VEIL.App/bin` empty, so run `dotnet build` again for development). (4) the default Selector hotkey `Ctrl+Alt+Tab` (and `Ctrl+Alt+S`, `Ctrl+Alt+Space`) were already taken on this PC; the default is now `Ctrl+Alt+L`. (5) While a Remote Desktop window (`mstsc`) was in the foreground the hotkeys did not reach VEIL; with a normal foreground window they did.

**Not verified:** the **machine scope** (Program Files, HKLM Run, ProgramData ACL, `admin provision` from the elevated setup, the administrator-settings shortcut, UAC relaunch) was only dry-run, because no elevation was available; a real sign-out/sign-in or reboot; Windows 10 and non-admin standard-user machines; a second user on the same PC; SmartScreen/antivirus behaviour (nothing is code-signed); upgrading from an older package version; installing to a path with unusual characters; the 205 MB self-contained size on slow disks.

### Different applications (2026-11-13)

`winver.exe` and `charmap.exe`, both allow-listed by executable path, plus a non-allowed `cmd`/Windows Terminal window. Next x3 then Previous x2 revealed About Windows, Character Map, About Windows, Character Map, About Windows. Each press left exactly one of the two above the overlays (the other dropped below), the non-allowed terminal stayed below the overlays, and each revealed app became the real foreground window. Zero overlays remained afterwards. A first attempt was invalid because the Windows session was locked (foreground was LockApp), so injected keys could not arrive. Not run: Chromium/Electron/UWP apps, elevated apps, apps that vanish mid-cycle.

### Window vanishing mid-cycle (2026-11-13)

`winver.exe` + `charmap.exe` allowed. Revealed winver, killed it while revealed, Next -> Character Map revealed (stale handle ignored, no error, no crash). Pressed Next again (same window re-revealed). Killed the last allowed window while revealed, then Next and Previous -> "No allowed windows to reveal", overlays on both displays untouched, process alive, 0 overlays after exit.

**Unexplained, not fixed:** in that first run, after the kill, the log said Character Map was revealed but the z-order sample 1.2 s later had it *below* the overlays; the next Next put it on top. Four further identical runs (sampled at 150 ms to 3 s, also checking the window's TOPMOST flag) showed it on top within 150 ms every time. So it happened once in five runs and I have no cause. Candidates (unverified): foreground handoff after the kill racing with the reveal, or the sampling itself. If a user can see a window fail to appear after another is closed, pressing the shortcut again recovers it.

### Minimized window (2026-11-13)

A minimized, B normal, both allowed: Next revealed B; Next restored A (`SW_RESTORE`) and revealed it, B went below the overlays; Next went back to B; Previous went to A. Minimized windows are part of the cycle and are restored on reveal. Behaviour to note: a window VEIL restored is **not** re-minimized when it is hidden again; it stays restored behind the overlay.

Test-harness caveat: injected keys are dropped by UIPI when the foreground window belongs to an elevated process. Minimizing the test window handed foreground to such a window on this machine, so the first attempt saw no hotkeys at all. VEIL was not at fault; a normal `cmd` window was made foreground for the passing run. Real keyboards are not subject to this, but a global hotkey cannot be verified with injected input while an elevated window is in front.

## Reveal run notes (2026-11-13)

- Tested with `charmap.exe` only. Win11 Notepad is a packaged app whose launcher process exits, so its identity resolves differently; packaged-app identity (ADR-002) is an open item. Browsers, IDEs, and fullscreen apps were not tried.
- Two zero-size Explorer helper windows (`ThumbnailDeviceHelperWnd`) are topmost and sit above the overlays; they have no visible content.
- Z-order was checked by enumeration, not by screenshot.
- A UTF-8 BOM in `config.json` (default for Windows PowerShell/Notepad) made the file unreadable; fixed with a test.
- The protected config directory is read-only even for its creator unless elevated, so provisioning/allow-list edits on a real install need an elevated step.

### Responsiveness, drag-and-drop, focus timer, workspaces, motion (2026-11-13)

See ADR-009. 332 unit tests pass (59 new). `tools/AdminShots` renders the new pages headless and passes.

**Freeze hardening (ADR-009).** The window thread no longer runs hotkey, display or selector logic and no longer waits on other processes; Emergency Recovery has its own thread and takes no shared lock; lifted windows are lowered again when Focus ends; a heartbeat watchdog ends the process if the window thread wedges while Focus is on. Verified live: a real application with a blocked UI thread was present, Focus Mode started, the frozen window was skipped, and an injected `Ctrl+Alt+Esc` restored the desktop in 43 ms. **Not verified:** that the earlier build freezes under that same condition (the original trigger is unknown), the watchdog's process kill, any freeze outside this analysis (graphics driver, elevated or secure-desktop window in front).

**Drop an application (FR-APP-001).** Dropping .exe files or shortcuts (.lnk) on the Settings window adds them to the allow-list and saves immediately; a list of programs that are open now offers one-click Allow; typing a path remains as a fallback. Shortcut targets were resolved correctly for real `.lnk` files, including Start Menu shortcuts of installed programs (a shortcut to a document, folder or Microsoft Store app is refused with a reason). **Not verified:** an actual mouse drag from File Explorer onto the window (the drop handler compiles and its logic is unit-tested, but a real OLE drag was not performed), dragging from an elevated Explorer (blocked by Windows), and Store/UWP applications, which are not supported yet.

**Focus timer (UI/UX 4.2).** 25/50/90 minute presets and a configurable default, started by `Ctrl+Alt+T` or the tray menu (which turns Focus on if needed), time left and a shrinking arc on the Focus screen, a quiet notice and one soft chime when time is up, and a record of the focus time actually spent (`%LOCALAPPDATA%\VEIL\focus-sessions.jsonl`, shown on the Settings > Focus Timer page). Ending Focus Mode stops the timer and records the partial session. Pause, break timers and long-break cycles are not built.

**Workspace profiles (UI/UX 4.1).** Named sets of applications (templates: Coding, Engineering, AI Lab, Research), edited under Settings > Workspace Profiles, switched with `Ctrl+Alt+P` or the tray menu without leaving Focus Mode, remembered per user. A workspace only narrows the Super Admin's allow-list, never widens it, and windows no longer permitted go back behind the overlay at once. **Not built from the original list:** a wallpaper, hotkey, priority windows, window positions or monitor layout per profile.

**Motion.** See ADR-008 and UI/UX 3.1. Measured on this PC: about 3-5% of one core in steady state with the animation running (higher in the first seconds). Not measured: a 4K or multi-monitor setup, battery impact.

**Default and spec conflict.** The quiet CPU/RAM line is on by default because the reference text lists system status, although UI/UX 3.1 says to avoid system monitoring by default; one checkbox in Appearance turns it off.
