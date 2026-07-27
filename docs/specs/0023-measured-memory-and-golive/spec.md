# 0023 — Measured memory, seeded history, and a rehearsed go-live

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §14 (volumetrics), §8 N1/N3 (responsiveness under load), §16 (3 GB VM), §9A.4
- **MVP:** in scope — Wave-0 items 8 and 9 of `11-build-plan-phase2.md`, the only ones never closed

## Problem

Two Wave-0 items were planned and never finished. Both are now blocking honesty rather than
features.

1. **The memory budget is an estimate presented as a deliverable.** `06-deployment.md` §2 says
   "all figures are estimates until measured against real containers", and eleven modules are
   about to be built on top of them. The phase-2 build plan attaches an **abort criterion** to
   this number — sustained RSS above 2.2 GB on the VM profile forces a consolidation stop — and
   there is no measurement to test it against. An abort criterion nobody can evaluate is not a
   safety rail.

2. **Every measurement so far ran against a near-empty database.** Spec 0010's 90-day
   §14-shaped history generator was deferred and never built, so page timings and memory were
   observed on a few hundred rows. A query plan that is fine on 200 invoices and fine on 25,000
   is a claim we have not earned.

3. **The go-live switch has never been executed or rehearsed.** `RUNBOOK.md` §9 is written and
   correct on paper. Demo seed data and one shared password (`Demo#1234`) are live on the public
   URL, and the verification runs have written test patients into that same database. The
   procedure that fixes this has never been run even once, so we do not know it works.

## Requirements

- [M] A history generator producing **90 days** of §14-typical activity for a 100-bed hospital —
  registrations, OPD invoices, diagnostic orders, samples and results, pharmacy sales,
  admissions/discharges with folios, and a closed counter session per day — written through the
  **real services**, so every row obeys the same invariants a live row does.
- [M] Idempotent and re-runnable; never runs by accident (explicit command, not a config flag
  that could be true in production).
- [M] A repeatable RSS measurement over the generated database: app and Postgres, at rest and
  under the Playwright suite, recorded as **measured** figures in `06-deployment.md` §2 with the
  estimates kept alongside for comparison.
- [M] The abort criterion of `11-build-plan-phase2.md` §2.9 evaluated explicitly against the
  measurement, with a stated verdict.
- [M] A go-live rehearsal that executes every step of `RUNBOOK.md` §9 against a scratch instance
  and proves each one: seeding off, credentials rotated, demo accounts deactivated, the shared
  password refused everywhere, a rotated credential still working, zero provisional prices.
- [S] Page-timing spot checks over the loaded database for the screens most likely to degrade
  (dues, audit, reports, dashboard).

## Acceptance criteria

1. `dotnet run --project src/Hms.Web -- generate-history --days 90` populates a database with
   §14-typical volumes and exits non-zero on any failure; running it twice does not duplicate.
2. Row counts after generation are within the §14 "typical/day" bands, and stated in `notes.md`.
3. `eng/verify/measure-rss.sh` prints a table of measured RSS and writes it into
   `06-deployment.md` §2; the numbers are labelled measured, with the date and the row counts
   they were measured against.
4. The abort criterion is evaluated in `notes.md` with an explicit pass/fail verdict.
5. `eng/verify/golive-rehearsal.py` passes: on a scratch instance with seeding disabled, the
   demo password authenticates **nowhere**, a rotated credential authenticates, a deactivated
   account is refused, and `/admin/masters` reports zero provisional prices.
6. The existing suites stay green (unit, integration, architecture, Playwright, upgrade gate).

## Out of scope

- **Executing the go-live switch on the production VM.** P16 asks who owns that cutover and is
  unanswered; firing it would disable the demo seed and rotate the shared password on the live
  URL. This spec proves the procedure works and leaves the trigger to the PM's written
  instruction, exactly as the runbook says.
- Load/concurrency testing (many simultaneous operators). Measuring RSS under a single-user
  functional suite is not a concurrency claim and is not written as one.
- Any change to the deployment topology or container limits — this spec measures them, and only
  proposes a change if the measurement demands one.

## Risks / open questions

- The measurement happens on a developer Mac (arm64, Docker Desktop), not the amd64 VM. Absolute
  RSS will differ. Mitigation: measure on **both**, record both, and treat the VM figure as the
  one the budget is judged against — the Mac figure is the fast feedback loop.
- Generating 90 days through the real services is slower than bulk SQL. Accepted deliberately:
  data that skips the services is data whose shape we cannot trust.
- P16 remains open. Recommended default unchanged: vendor executes on the PM's written
  instruction; this spec removes every excuse except that instruction.
