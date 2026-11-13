# ADR-005: Super Admin identity and authentication

- Status: Proposed (interim)
- Date: 2026-11-13
- Requirements: FR-ADM-001..006, NFR-SEC-003

## Context
Identity must be a stable OS principal; passwords must never be collected or stored.

## Decision
Identity = Windows SID / Linux UID plus platform, compared case-insensitively; display name never grants authority. Provisioning and reassignment require OS administrator authority and a second identity cannot be added through normal use. Authorization runs on every protected change. Interim authentication = the interactive logon token the OS already authenticated; if the session cannot give a trustworthy identity the request is denied, never downgraded.

## Consequences / open items
OPEN: no explicit OS re-authentication prompt (e.g. credential/Windows Hello/polkit) yet, so a session left unlocked by the Super Admin is sufficient. Provisioning was verified only for the denial path (non-elevated); the elevated success path is covered by unit tests with fakes, not run live. Provisioning from an installer is part of ADR-007.
