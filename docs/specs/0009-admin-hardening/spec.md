# 0009 — S5: Dashboard, admin, import, hardening

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** §9A.2 modules 7 & 8, §8 (N3/N5/N6), §12
- **MVP:** in scope

## Problem
The MD needs today-so-far + closed-day numbers from the same rows Accounts will consume (§6.6);
the construction-phase sale needs the import pipeline (F1 beat); operations need backup/restore
and disk/clock sentinels.

## Requirements
- [M] Dashboard read-model: view over day_close_summary + live session receipts; numbers never
      shift when M15 arrives (§6.6).
- [M] Import pipeline (ADR-0010): staged upload → validate with per-row errors → idempotent upsert
      by natural key → audited batch with reversal; catalog+price importer first.
- [M] Backup/restore scripts (ADR-0013): nightly dump exists (S1); restore script, two modes.
- [S] Audit viewer, 2FA enrolment, masters screens, disk/clock sentinels.

## Acceptance criteria
1. Read-model query returns identical totals before/after a (simulated) Accounts-style re-read.
2. Import: bad rows reported per-row; re-import updates not duplicates; reversal batch restores.
3. Restore script documented and exercised against a scratch database.

## Out of scope
BI beyond drill-to-register; patient-master import (post-MVP per ADR-0010).

## Risks / open questions
None new.
