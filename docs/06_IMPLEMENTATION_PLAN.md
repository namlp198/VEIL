# 06 — Implementation Plan

## 1. Delivery strategy

Build the native window/display core before polishing the visual experience. The highest technical risk is not the wallpaper or settings UI; it is predictable multi-monitor overlay stacking and selective window presentation across different operating systems.

Use vertical slices with a demonstrable build at every phase. Do not implement Windows and Linux simultaneously until the core platform abstraction has been proven on one reference platform.

Recommended sequence:
1. Windows technical spike.
2. Ubuntu X11 technical spike.
3. Shared platform abstractions.
4. Cross-platform core and UX.
5. Security and packaging hardening.

## 2. Phase roadmap

### Phase 0 — Repository and specification baseline
Deliverables:
- Repository scaffold.
- .NET/Avalonia versions pinned.
- Coding standards and SDD workflow.
- ADR template and initial ADRs.
- CI build and unit-test pipeline.
- Requirement-to-test traceability matrix.

Exit gate:
- Clean build on Windows and Ubuntu build agents.
- All architecture decisions with unresolved risk are recorded.

### Phase 1 — Overlay Engine proof of concept
Work packages:
- P1.1 Display enumeration.
- P1.2 One overlay per display.
- P1.3 Full-screen bounds and DPI behavior.
- P1.4 Overlay teardown.
- P1.5 Display hot-plug handling.
- P1.6 Process-kill and crash recovery tests.

Exit gate:
- 1, 2, and 3-display scenarios pass.
- Add/remove display while focused passes.
- Forced process termination leaves no persistent overlay.
- No window-selection feature is required yet.

### Phase 2 — Window Manager proof of concept
Work packages:
- P2.1 Enumerate top-level windows.
- P2.2 Normalize process/application identity.
- P2.3 Raise selected window above overlay.
- P2.4 Hide selected window behind overlay.
- P2.5 Multiple allowed windows.
- P2.6 Modal/transient window behavior.
- P2.7 Fullscreen and elevated-app compatibility tests.

Exit gate:
- At least three representative applications can be selected and used above the overlay.
- Background windows remain running.
- Desktop is not exposed during normal switching.
- Unsupported window types fail safely.

### Phase 3 — Platform abstraction and Linux X11 adapter
Work packages:
- P3.1 Define display/window/hotkey/authentication interfaces.
- P3.2 Implement Windows adapter.
- P3.3 Implement Ubuntu X11 adapter.
- P3.4 GNOME on Xorg reference validation.
- P3.5 Detect Wayland and show support limitation.
- P3.6 Cross-platform integration test suite.

Exit gate:
- Shared application/domain code builds for both targets.
- Required 1.0 acceptance tests pass on Windows and Ubuntu X11.
- Wayland is detected and handled honestly.

### Phase 4 — Window Selector and Hotkeys
Work packages:
- P4.1 Selector list and states.
- P4.2 Search/filter and keyboard navigation.
- P4.3 Global hotkey registration.
- P4.4 Conflict detection.
- P4.5 Hide All and Emergency Recovery.
- P4.6 Optional thumbnails after privacy/performance review.

Exit gate:
- All primary actions are keyboard accessible.
- Hotkey conflicts do not corrupt existing bindings.
- Emergency Recovery works when the selector UI is unresponsive.

### Phase 5 — Super Admin and protected configuration
Work packages:
- P5.1 Define stable OS identity model.
- P5.2 First-run provisioning.
- P5.3 OS authentication integration.
- P5.4 Machine-wide protected storage and ACLs.
- P5.5 Admin Settings authorization.
- P5.6 Audit log and recovery/reassignment procedure.
- P5.7 Configuration backup/restore.

Exit gate:
- Exactly one Super Admin identity is configured.
- Non-admin cannot modify machine-wide policy.
- Wrong identity is denied.
- No OS password is stored.
- OS administrator override limitation is documented.

### Phase 6 — Product UX and profiles
Work packages:
- P6.1 Focus Canvas visual design.
- P6.2 Hacker/mystic theme assets.
- P6.3 Admin Settings screens.
- P6.4 Workspace Profiles.
- P6.5 Per-display preferences.
- P6.6 Reduced motion and accessibility.
- P6.7 Diagnostics screen.

Exit gate:
- UX flows are complete and keyboard accessible.
- Profile switching does not terminate applications.
- Mixed DPI and display layouts pass.

### Phase 7 — Hardening and release
Work packages:
- P7.1 8-hour soak tests.
- P7.2 CPU/RAM profiling.
- P7.3 Installer and uninstall behavior.
- P7.4 Auto-start behavior and safe startup.
- P7.5 Upgrade/migration tests.
- P7.6 Crash and recovery regression suite.
- P7.7 User documentation and release notes.

Exit gate:
- All P0 requirements pass.
- No open critical/high severity defects.
- Backup, rollback, uninstall, and emergency recovery are verified.
- Platform support matrix is published.

## 3. Milestone priority

| Milestone | Priority | Main outcome |
|---|---|---|
| M0 | P0 | Buildable repository and approved architecture |
| M1 | P0 | Multi-monitor overlay proof |
| M2 | P0 | Selective window reveal/hide proof |
| M3 | P0 | Windows + Ubuntu X11 adapters |
| M4 | P0 | Selector, hotkeys, emergency recovery |
| M5 | P0 | Super Admin and protected config |
| M6 | P1 | Final UI, profiles, diagnostics |
| M7 | P0 | Release hardening and acceptance |

## 4. Initial technical spikes

Before committing to full implementation, run two small spikes.

### Spike A — Windows
- Enumerate displays.
- Create per-monitor overlay windows.
- Enumerate top-level windows.
- Place one test window above overlays.
- Verify multi-monitor, DPI, fullscreen, Alt+Tab, UAC, and process termination behavior.

### Spike B — Ubuntu X11
- Confirm X11 session detection.
- Enumerate displays with RandR.
- Enumerate top-level windows via EWMH.
- Create per-display overlays.
- Test stacking/activation with GNOME on Xorg.
- Test hotkeys and process termination.

Each spike must produce:
- Source code.
- Reproduction steps.
- Test matrix.
- Known limitations.
- ADR recommendation.

## 5. Definition of Done

A work item is done only when:
- Requirement ID is referenced.
- Code is reviewed and formatted.
- Unit tests pass.
- Platform integration tests pass where applicable.
- Logs and failure states are handled.
- No secrets or sensitive window content are logged.
- Documentation is updated.
- Manual acceptance steps are reproducible.

## 6. Risk register

| Risk | Severity | Mitigation |
|---|---|---|
| Z-order differs across window managers | High | Early native spikes; platform-specific tests |
| Wayland blocks required control | High | Explicitly exclude full Wayland from 1.0 |
| Fullscreen/exclusive fullscreen bypasses overlay | High | Define supported behavior; test common cases |
| Elevated windows cannot be controlled | Medium/High | Respect OS privilege boundaries; show limitation |
| Overlay remains after crash | Critical | Process-kill tests, idempotent teardown, recovery supervisor |
| Admin model differs by OS | High | Stable identity abstraction and separate platform implementation |
| Hotkey conflicts | Medium | Registration result handling and configurable defaults |
| Mixed DPI geometry bugs | Medium | Normalize physical/logical coordinates and test matrices |

## 7. Suggested initial backlog

1. Create solution and project skeleton.
2. Add platform abstractions and normalized models.
3. Implement Windows display enumeration.
4. Implement Windows overlay prototype.
5. Implement overlay teardown and crash tests.
6. Implement Windows window enumeration.
7. Implement selected-window stacking prototype.
8. Record Windows technical ADRs.
9. Repeat the spike on Ubuntu X11.
10. Finalize cross-platform interface contracts.
11. Implement selector and hotkeys.
12. Implement Super Admin provisioning and protected settings.
13. Complete UI, profiles, and release hardening.

## 8. Release checklist

- [ ] Supported OS/session matrix verified.
- [ ] All active displays covered.
- [ ] Display hot-plug tested.
- [ ] Selected allowed windows appear correctly.
- [ ] Disallowed windows remain behind overlay.
- [ ] Focus Off restores desktop.
- [ ] Emergency Recovery verified.
- [ ] Forced termination leaves no overlay.
- [ ] Super Admin identity provisioned and tested.
- [ ] Standard user denied protected settings.
- [ ] No credentials stored or logged.
- [ ] Configuration backup/restore verified.
- [ ] Installer, upgrade, and uninstall verified.
- [ ] Known limitations documented.
