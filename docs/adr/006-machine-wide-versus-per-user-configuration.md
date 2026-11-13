# ADR-006: Machine-wide versus per-user configuration

- Status: Proposed
- Date: 2026-11-13
- Requirements: NFR-SEC-002, FR-ADM-003/007/008

## Context
Policy must be protected from standard users, while denied attempts must still be recordable.

## Decision
Machine policy (Super Admin, allow-list, hotkeys) in `%ProgramData%\VEIL` (Linux `/etc/veil`), created with SYSTEM/Administrators full control and Users read-only; override with `VEIL_CONFIG_DIR` for development. JSON, schema-versioned, size-limited, strictly validated, written atomically. Audit log is JSONL with rotation; when the protected location is not writable it falls back to a per-user directory and never changes the authorization outcome.

## Consequences / open items
FINDING: the first audit implementation crashed for a standard user because of the very ACL it relied on; fixed and covered by tests. SQLite (architecture 9) is deferred: JSON covers all Phase 1-5 data. Introduce SQLite only when profiles/history (1.x) need queries. Per-user preferences location is not yet split out.
