# 0025 — Plan

## Approved: 2026-07-28

## Traceability matrix (PRD → screen), before code

| PRD | Requirement | Screen / surface | Built |
|---|---|---|---|
| M7 [M] | OT & theatre master; operation catalogue with base charges | `/ot/theatres` + `adm.service` (OT- codes) | yes |
| M7 [M] | Operation schedule entry with theatre/time | `/ot/schedule` | yes |
| M7 [M] | Surgical team per operation | `/ot/schedule` (surgeon/assistant/anaesthetist rows) | yes |
| M7 [M] | Operative details & completion record | `/ot/case/{id}` | yes |
| M7 [M] | Charge posting → folio | completion, via `OtBilling` at the composition root | yes |
| M7 [M] | Consumables → folio + stock deduction | `/ot/case/{id}` consumables panel (FEFO) | yes |
| M7 [M] | Operation register | `/ot/register` | yes |
| M7 [S] | OT dashboard / schedule display | `/ot/board` | yes |
| M7 [C] | Pre-op checklist | — | **cut** (spec §Out of scope) |
| §11 | OT case states | guarded transitions in `OtService` | yes |
| §12 | Permissions as data | `ot.read`, `ot.schedule`, `ot.record` | yes |
| US7.3 | Team splits for M17 | `ot.case_team` rows carry role + person + amount | yes |

## Module

New `Hms.Ot` module, `ot` schema. References Kernel only; anything spanning Billing/Pharmacy/Ipd
lives in `OtBilling` at the composition root (same rule as `IpdBilling`, `EmrOrdering`).

### Entities

- `ot.theatre` — branch, name, active.
- `ot.case` — patient, `folio_id` XOR `encounter_id` (indoor case vs OPD day case), theatre,
  operation catalogue id, `scheduled_from`/`scheduled_to`, state (§11), anaesthesia type,
  findings, procedure, `started_at`/`finished_at`, cancel reason, attribution.
- `ot.case_team` — case, role (surgeon|assistant|anaesthetist|scrub), person id, name snapshot,
  the `adm.service` whose rate is that role's fee, and the amount actually posted.
- `ot.case_consumable` — case, product, qty, batch, the charge line it produced.

Double-booking is prevented by a **query under the same transaction plus an exclusion-shaped
uniqueness check**: overlapping windows for a theatre, and for any team member, are looked up
`FOR UPDATE` on the theatre row so two concurrent schedulers serialise (ADR-0015 #1 pattern).

### Service

`OtService` — schedule, transitions, completion record, cancel/postpone. It knows nothing about
money. `OtBilling` (composition root) does the folio posting and the stock issue, because those
cross module boundaries.

## Screens

1. `/ot/board` — today's theatres, what is in each, and what is next.
2. `/ot/schedule` — the scheduling form (patient, theatre, operation, window, team).
3. `/ot/case/{id}` — the case: transitions, operative record, consumables, completion.
4. `/ot/register` — the chronological operation register with a date range.
5. `/ot/theatres` — theatre master.

Nav under "Operation Theatre". Roles: **OT In-charge** (new, `ot.schedule` + `ot.record` +
`ipd.read`), surgeon uses the OPD Consultant role plus `ot.read`.

## Verification

- Integration tests: overlap refusal (theatre and surgeon), state-machine order, completion
  posting sum, double-completion refusal.
- `eng/verify/ot-thread.py`: admit → schedule → conflict refused → ready → in-theatre →
  consumable issued → complete → folio total rose by the expected amount → register shows it.
- Playwright: the board and register load; scheduling refuses a clash at the surface.
- Upgrade gate runs the thread and smokes the routes.
