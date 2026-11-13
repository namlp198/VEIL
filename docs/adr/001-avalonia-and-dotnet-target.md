# ADR-001: Avalonia and .NET target

- Status: Accepted (runtime) / Deferred (UI)
- Date: 2026-11-13
- Requirements: NFR-PORT-001, NFR-MAINT-001

## Context
Architecture requires a pinned .NET LTS and Avalonia version before coding. The workspace SDK is 10.0.401 and Avalonia 12.1.5 is already restored locally.

## Decision
Pin `net10.0` and SDK 10.0.401 (`global.json`, `Directory.Build.props`). Shared layers (Domain, Application, Infrastructure) have no OS or UI dependency. The Avalonia presentation project is created in Phase 6 and pinned to 12.1.5 at that time; Phases 1-5 use the console spike host `VEIL.App`.

## Consequences / open items
Confirm .NET 10 is LTS-supported through the release window before Phase 7. No preview runtime in release builds.
