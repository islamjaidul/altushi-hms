# 0022 — Upgrade-path testing: boot against the previous release's data, in CI

- **Status:** Accepted
- **Date:** 2026-07-27
- **Answers:** review debt #1 (`10-mvp-review.md` §6); the reference-band production defect (spec `0013`, commit `095f552`)
- **Spec:** `docs/specs/0014-phase2-review-and-plan/` (decision) · Wave-0 build spec (implementation)

## Context

Every local and CI test run starts from a freshly created database, so every migration and
every seed has only ever been proven against clean schemas. The one defect that reached
production came exactly this way: report templates written before reference bands existed
deserialised with a null band list, result entry 500'd on the deployed instance, and every
local test stayed green. Additive-only migrations are necessary but not sufficient — data
written by version N must still be readable and workable by version N+1. Phase 2 adds a
schema-bearing module per wave; each deploy repeats this exposure against live hospital data.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Keep additive-only migrations as the sole guard | No new infrastructure | Already failed once: shape-compatible ≠ meaning-compatible; old *data* under new *code* is untested |
| Test restores from production backups | Real data | Real patient data in CI is a privacy non-starter; backups don't exist for demo-stage customers |
| **Versioned upgrade fixture in CI (chosen)** | Deterministic, private, catches the actual failure class | The fixture must be maintained at each release cut |

## Decision

A CI gate (and local script under `eng/verify/`) that, per release:

1. Restores a **schema + seed snapshot of the previous release** (a `pg_dump` checked into
   `eng/verify/upgrade/`, refreshed at each release cut — the dump of the demo dataset, never
   production data).
2. Boots the **current** build against it, letting migrations and seed-upgraders run.
3. Runs the golden thread and the module end-to-end scripts against the upgraded database —
   not just "did migrations apply", but "does the product still work on old data".

Rules that follow: every migration lands with the upgrade gate green; a seed-upgrader (like
the reference-band backfill) is the standard remedy for old-shape data, and it must be proven
here, not on the VM. Each new module's release adds its dump to the fixture at the next cut.

## Consequences

- The clean-database blind spot is closed with one job; the failure class that already
  reached production cannot recur silently.
- Deploys per `deploy/RUNBOOK.md` §4 gain a rehearsed upgrade, not just a snapshot-rollback.
- Cost accepted: one dump per release kept in-repo (demo data, small); the gate adds minutes
  to CI, run on merge rather than on every push if it grows slow.

## Reversal trigger

If the fixture chain becomes heavy (many releases), collapse to N-1 and N-2 only — the
deployment reality is single-customer VMs updated release-to-release, so deep chains guard
nothing real.
