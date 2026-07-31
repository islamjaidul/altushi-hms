# 0033 — Operator user guide (per-role handbook)

- **Status:** Done
- **Date:** 2026-07-29
- **PRD ref:** §4 (personas), §5 (modules), §6.2–6.6 (journeys), §7 (UX principles)
- **MVP:** in scope (documentation for the shipped fourteen modules; freeze lifted 2026-07-27)

## Problem

The product is being handed to hospital computer operators, and nothing tells an operator
which screens are theirs or what their day looks like. The PRD describes requirements, the
QA docs describe coverage — neither is usable by Rina at the front desk. The owner also
asked a question the documentation must answer plainly: *do a patient's charges (pathology,
doctor fee, medicine, ward/cabin) reach "the accounts module" automatically?* — the honest
answer (no M15 exists; the billing spine consolidates and the day-close is the accounts
hand-off) is currently recorded nowhere an operator or owner would read.

## Requirements

- [M] A single `user-guide.md` in the repo root, English only, quick-reference depth
  (numbered daily-task steps per role, not field-by-field).
- [M] One section per seeded role (12), each with: who it is, start of shift, daily tasks,
  end of shift, and when to call a supervisor.
- [M] A "How the money works" section stating exactly which charges post automatically and
  which are typed at a counter, using the owner's own example, and stating plainly that
  M15 Accounts is future scope — the day-close statement and reports are today's hand-off.
- [M] Demo login table with the go-live warning (accounts deactivated/re-passworded per
  RUNBOOK §9).
- [S] A reference section: approval matrix, printing map, public displays, troubleshooting
  (including known quirks: pharmacist post-open redirect, carry-close has no screen, IPD
  menu heading).

## Acceptance criteria

1. Every URL, menu label, and demo username in the guide matches `ModuleNav.cs`,
   `role-journeys.py` and `DevSeed.cs` (verified by grep).
2. Every AUTOMATIC/MANUAL money claim traces to service code
   (`BillingService`, `IpdBilling`, `OtBilling`, `DiagnosticsRelease`, `EmrOrdering`,
   `PharmacySale`, `DayCloseService`).
3. All 12 roles and all 14 built modules appear; the 8 unbuilt modules are named as not
   yet built, Accounts explicitly.
4. `spec-auditor` passes after close.

## Out of scope

- Screenshots (no capture pipeline yet); Bangla translation; per-field reference manual;
  any code or PRD change.

## Risks / open questions

- The guide records today's behaviour, including quirks; when a quirk is fixed the guide
  must be updated — noted as a follow-up obligation in `notes.md` at close.
