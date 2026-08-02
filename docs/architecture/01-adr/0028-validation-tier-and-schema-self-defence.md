# 0028 — Validation tier and schema self-defence

- **Status:** Accepted
- **Date:** 2026-08-03

## Context

Spec 0038 found that no layer validated operator input: the model binder wrote `default` into
bare `[BindProperty]` properties, no handler inspected `ModelState` (0 DataAnnotations, ~290
bound properties), and the database's 12 CHECK constraints were all structural — not one
bounded a domain value. That single gap produced all 51 validation defects, including a decimal
payment silently receipted as 0 Tk on three cash screens (§8 N4 money-integrity breach) and a
100 KB name that locked an operator out with HTTP 431. Separately, the schema declared no
foreign keys outside ASP.NET Identity, no state-column constraints, one inert concurrency
token, and hard rule 4's `REVOKE DELETE` protected a role nothing connects as. Spec 0039
remediated by class; this ADR records the decisions so the next maintainer can tell design
from omission.

## Options considered

| Option | Pros | Cons | RAM cost |
|---|---|---|---|
| **Base-class input gate** (`HmsPageModel` page-filter refuses any binder/annotation error on a posted field) | Structural — covers all 141 handlers incl. those never probed; cannot be forgotten on the next screen | Refusal is a redirect, so typed form values are lost on error | none |
| Nullable binding per field (`long?` + explicit require) | Semantically precise per field | Touches every page, and the next screen can forget it | none |
| Validation only in handlers (status quo ante) | No framework machinery | Proven failure — 51 defects | none |

Schema posture options: (a) trust the application layer only (status quo — rejected, any
second write path bypasses it); (b) domain CHECKs + intra-schema FKs added **NOT VALID**
(chosen); (c) fully validating constraints (rejected: a legacy row that violates one would
abort the boot-time migration — an outage at a hospital — and hard rule 4 forbids deleting
the offending rows first).

## Decision

1. **The input tier is the base-class gate.** A non-GET request whose `ModelState` carries an
   error on a *posted* field never reaches its handler; the operator is redirected back with a
   plain sentence (layout-level banner). Judging only posted fields is what makes annotations
   safe on multi-form pages. Coverage is enforced by `InputGateCoverageTests` (every page model
   derives from `HmsPageModel`; the allowlist names the auth pair and the two public pages).
2. **Whole-taka entry rejects decimals with a message** — never silent rounding. The PRD
   mandates whole-taka entry (§5 M4); a typed `100.55` is a mistake to correct, not a value to
   guess at. Uniform via the gate, so no screen can decide otherwise.
3. **String bounds are constants** (`Hms.Shell.Bounds`): names 200 · codes 40 · phone 20 ·
   address 500 · notes 4,000 · clinical text 10,000 — sized against long Bangladeshi names,
   honorifics and addresses. Applied as `[StringLength]` at the binder and as
   `CHECK (length(col) <= n) NOT VALID` at the database. Columns stay `text` — equivalent in
   PostgreSQL, and re-typing to `varchar(n)` would abort migration on a database already
   holding an oversized legacy row.
4. **Every new CHECK and FK on a pre-existing table ships `NOT VALID`.** New writes are fully
   constrained; legacy rows (the audit's orphans and out-of-range values) are retained under
   hard rule 4 — reconciled or quarantined by follow-up, never deleted. `VALIDATE CONSTRAINT`
   is the explicit later step once a class is reconciled. The two `EXCLUDE` constraints on
   `ipd.bed_stay` (no `NOT VALID` form exists) apply inside guarded `DO` blocks: skipped with
   a loud warning on a database whose data already violates them, never a refused boot.
5. **Foreign keys stay inside a module's own schema.** Cross-module references remain plain
   ids validated at the service/composition edge (ADR-0003 boundary; `eng/check-fkeys.sh`
   enforces). Every FK is `ON DELETE RESTRICT`, set structurally by a model-wide loop — a
   cascade is a delete machine and hard rule 4 forbids it. Declined: unique
   `(test_order_id, test_catalog_id)` on `diag.order_test` — the same test twice on one order
   is a legitimate clinical instruction.
6. **The doctor master is `appt.doctor`** with a real identity column, replacing `MAX+1`
   minting; `doctor_schedule` and `appointment` carry FKs to it. Other modules keep snapshot
   `doctor_id` columns per the module boundary.
7. **Concurrency tokens are PostgreSQL `xmin`** (`IsRowVersion` on `uint`), as HR already did:
   the database maintains it, so it cannot be declared-but-never-assigned again (the fate of
   `Invoice.Version`, which stays as an inert plain column — dropping it is not additive).
   Conflicts surface as a 409 sentence via the fault boundary.
8. **SQLSTATE translation is centralized** in `FaultBoundaryMiddleware`: `23514`/`23505`/
   `23503`/`23502`/`40P01`/`53300` each render an operator sentence; domain exceptions render
   their message; everything else gets a recoverable page and a log entry. Constraints and the
   boundary shipped in the same change — constraints alone would have turned silent corruption
   into visible 500s.
9. **Hard rule 4's grants are applied idempotently at every boot** after migrations
   (`ApplyHardRule4GrantsAsync`), covering all schemas — a fixed migration cannot, because the
   kernel migrates before the other schemas exist. Switching runtime traffic to `hms_app` is a
   deployment configuration change (`deploy/compose.yml`); `eng/check-no-hard-deletes.sh`
   guards the code convention meanwhile.

## Consequences

- A malformed value is refused identically at three depths (binder, handler, schema); a new
  write path added tomorrow is still constrained by the deepest one.
- Refusal-by-redirect loses the operator's typed input on a gate refusal. Accepted: the
  refused value was unusable, and the sentences name the field. Revisit if operators report
  re-typing pain on long forms.
- `NOT VALID` means legacy bad rows remain queryable; reports that aggregate them must not
  assume the constraints describe history — only the future.
- The model snapshot declares `varchar(n)` while columns stay `text` (deliberate, see #3);
  anyone diffing model vs database will see this and should not "fix" it.
- No RAM cost anywhere: constraints, filters and the gate are per-request CPU noise on the
  2 vCPU / 3 GB target; the added indexes cost disk but remove the hottest seq-scans (§16).

## Reversal trigger

- If operators are measurably blocked by refusals on data that is genuinely valid in the
  field (e.g. a real name over 200 chars), widen the specific bound — widening a CHECK is
  additive.
- If a `NOT VALID` constraint's class is fully reconciled, `VALIDATE CONSTRAINT` it; if any
  class cannot be reconciled within two release cycles, revisit the quarantine story.
- If a second branch goes live and cross-branch admin queries multiply, revisit per-context
  `CurrentBranch` in favour of an explicit tenant service (see ADR-0007).
