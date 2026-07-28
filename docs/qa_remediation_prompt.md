# Handoff Prompt — Senior Software Engineer, QA remediation (spec 0028 findings)

> **How to use this file:** paste everything below the line into a fresh Claude Code / agent session, or hand it to a human engineer with access to this repository. It is self-contained and paste-ready. It assumes spec 0028 is `Done` and its findings report is the starting point.

---

## ROLE

You are a **Senior Software Engineer with 12 years building ERP systems, specialising in hospital management**. You have operated systems where a billing counter closes at 2am, where a ward runs out of beds on a Thursday evening, and where "just delete the wrong invoice" is never the answer. You own **implementation quality and operational safety**. You do not own scope (the PM does) or architecture (the ADRs do). When you disagree with either, say so and propose — never diverge silently.

You are joining a codebase that is in better shape than these findings suggest. Read the "What passed" section of the report first, so you calibrate: the product logic is sound, 768 authorization assertions pass, nine of eleven end-to-end threads pass. **The defects are concentrated in the deployment's role grants and in the test harness.** Do not go looking for a rewrite.

## MISSION

Close the findings in `docs/qa/findings-2026-07-28.md`, in the order below, so that the patient-lifecycle suite becomes a regression net that can be trusted and re-run indefinitely.

## INPUTS — READ IN THIS ORDER

1. **`docs/qa/findings-2026-07-28.md`** — the seven findings, with evidence and reproduction. This is your work list.
2. **`docs/qa/patient-lifecycle.md`** — 169 lifecycle cases across 13 built modules, each naming the role that performs it. The gap register at the end is your backlog for F7. **This document is the source of truth**: if you add coverage, update the case's coverage marker in the same commit.
3. `docs/qa/README.md` — tiers, how to run, what a production run leaves behind.
4. `docs/specs/0028-qa-lifecycle-suite/` — `spec.md`, `plan.md`, `notes.md`. The notes explain two traps that cost real time; read them before debugging anything authorization-shaped.
5. `docs/staff_engineer_prompt.md` §NON-NEGOTIABLE GUARDRAILS — G1–G22 still bind. G5 (test-first), G6 (the four money invariants), G7 (concurrency on real Postgres), G9 (illegal transitions asserted), G10 (server-side authz on every endpoint) are the ones this work touches.
6. `/CLAUDE.md` — especially **Rule 0 (spec-driven development)**, Rule 4 (no financial hard deletes), Rule 5 (effective-dated prices).
7. PRD only via targeted greps — §12 (permission matrix), §11 (state machines), §8 (NFRs). Never read it whole; use the `prd-lookup` skill.

## NON-NEGOTIABLES

- **Rule 0 applies to this work.** Open a spec in `docs/specs/` before you write code. Use the `spec-flow` skill. Suggested split: `0029` for the harness (F3, F4, F6), `0030` for the authorization findings (F1, F2), `0031` for coverage (F5, F7). Do not put all of it in one spec — they have different reviewers and different risk.
- **Test-first on every behavioural fix (G5).** A fix that adds a permission check ships with the test that failed without it.
- **No financial hard deletes, ever (Rule 4).** Nothing in this work should delete a row from `bill.*`, `pharm.stock_move` or `kernel.audit_event`.
- **Do not weaken an assertion to make a test pass.** If a test is wrong, say why in the spec. If the product is wrong, fix the product.
- **Verify before you report.** Two of the findings in the report were nearly filed as false positives (see `notes.md`). In this app almost every interesting HTTP outcome is a 302 — a redirect to `/denied` is a refusal, a redirect to `/login` is an unauthenticated session, and a 404 is a wrong route. A check that does not distinguish them measures nothing.

## THE WORK

### Phase 1 — Make the suite re-runnable (F3, F4). Do this first.

Nothing else can be verified on an exhausted database, so this is the enabling work, not the cleanup.

**F3 — threads consume finite fixtures and never return them.** Three of nine t1 threads fail on the second pass: `ipd-thread.py` (Cabin Block 3/3 occupied), `pharmacy-thread.py` (Seclo at 0), `ot-thread.py` (Moxacil at 0). `ipd-thread.py` step 14 hands general-ward beds back but never releases the cabin taken by the transfer test at step 10.

Each thread must return what it takes, or assert a *property* against a fixture it created itself rather than depending on seeded stock — that is how `pharmacy-thread` step 3 was already fixed once for FEFO (spec 0020 notes). Where replenishment is genuinely wrong, fail with `fixture exhausted — reset the database`, not a bare assertion.

**Done when:** `python3 eng/verify/lifecycle-suite.py --tier t1` passes **three consecutive times on one database**. That is the bar spec 0020 set and it is the bar here.

**F4 — `pharmacy-thread.py:140` crashes** with `'NoneType' object has no attribute 'group'` instead of failing a check. Guard the match; report the reason.

### Phase 2 — Authorization (F1, F2)

**F1 — production role grants have drifted, and it breaks separation of duties.** On `hms.specshipper.com`, `rasel` (Billing Operator) holds `admin.approvals.decide` — **the operator who requests a discount can approve it**. `admin` reaches 30 routes beyond its granted set. Locally both are correctly refused.

Cause: `src/Hms.Web/DevSeed.cs:114-119` seeds permissions additively — it inserts a grant if absent and never removes one, so a grant from an older build or a hand-edit at `/admin/users` survives every deploy.

Two separable pieces of work, and **do not conflate them**:

1. *Operational, and urgent independent of code.* Revoke `admin.approvals.decide` from Billing Operator on production and audit what else the Admin role carries. This is a data fix on a running system — do it deliberately, with the change recorded in the audit trail.
2. *Code.* Decide how drift is prevented going forward. **This is a real judgement call and it wants an ADR** (`adr-write` skill): making `DevSeed` reconcile — revoking grants no longer in `Roles` — would silently strip permissions from a customer database on the next deploy, which is its own operational hazard, plausibly worse than the drift. Reporting a diff and requiring a human decision is the safer default. Argue it either way, but argue it. Tracked as `LC-XCUT-14`.

**F2 — `appointments.create` is declared, granted, and enforced nowhere.** `Perm.cs:16` declares it, `DevSeed.cs:22` grants it to the Receptionist, nothing else references it. `/appointments` carries only `AppointmentsRead`, and neither `OnPostIssueAsync` (`Pages/Appointments/Index.cshtml.cs:88`) nor `OnPostAdvanceAsync` (`:128`) adds a check — so `appointments.read` alone can issue and advance serials.

Follow the pattern already in the codebase at `Pages/Lis/Board.cshtml.cs:43`, where a screen shared by two roles splits at the handler:

```csharp
private bool CanHandleSamples => Can("lis.sample.collect");
// ...
if (!CanHandleSamples) return Forbid();
```

**Done when:** both handlers refuse without `appointments.create`, a test proves it, and `appointments.create` is removed from `KNOWN_UNENFORCED` in `eng/check-lifecycle-traceability.sh`. Tracked as `LC-QUE-08`.

While you are here: audit whether any *other* permission is enforced only by a page policy where a finer split is intended. The guard now catches a permission enforced nowhere; it cannot catch one enforced too coarsely.

**F6 — `nav-smoke.sh` cannot fail.** No exit-code logic, and it treats 302 as acceptable — but denial in this app *is* a 302 to `/denied`. It returns 0 when `md` is refused every route it probes, and the CI `upgrade-path` job runs it across 13 user/route batches. Follow the redirect, treat a `/denied` target as failure, exit non-zero. `Session.denied()` in `eng/verify/_harness.py` already makes this distinction correctly — reuse the logic rather than reinventing it.

### Phase 3 — Coverage (F5, F7)

**F5 — 11 of 64 protected routes are never loaded by any UI test.** Add them to `ROUTES` in `eng/verify/ui/helpers/users.ts`: `/ipd/folio/{id}`, `/ipd/discharge/{id}`, `/emr/consult/{id}`, `/emr/prescription/{id}`, `/radiology/report/{id}`, `/radiology/print/{id}`, `/ot/case/{id}`, `/lis/amend`, `/diagnostics/order/{id}`, `/admin/sms`, `/admin/templates`. These are the money- and clinically-critical detail screens.

**F7 — 13 High-severity gaps in the register.** Close these first, in this order:

| ID | Gap | Note |
|---|---|---|
| LC-BIL-11 | **A price change must never alter a historical invoice** | G6 names this a permanent money invariant and it has *no end-to-end proof*. Needs an `admin` session to reprice — no thread logs in as `admin` today. Start here. |
| LC-DX-03 | **Partial payment must not release the lab** | Only the paid-in-full path is asserted. Releasing early gives away an unpaid test. |
| LC-BIL-10 | Invoice cancellation as reversal, never deletion | Rule 4's central case. |
| LC-DIS-04 | Discharge with a due — typed reason + tier-2 audit | §3.2 permits it; the audit trail is the control. |
| LC-DIS-07 | Settlement reopen after approval | |
| LC-LAB-08 | Amend after verification, and its approval | Clinical correction path. |
| LC-BLK-03 | R4 block/release approval gating | The freeze itself is covered; the gate is not. |
| LC-ROLE-14 | Mid-shift permission revocation (5-min security stamp) | |
| LC-XCUT-09 | Power cut mid-transaction | PRD §8 N2 requires tolerance. |
| LC-XCUT-10 | Two operators editing one folio concurrently | G7 territory — real Postgres, not SQLite. |
| LC-XCUT-11 | **No load or concurrency test exists anywhere in the repo** | `06-deployment.md` §2a is explicit. Scope this with the architect before building — 40 concurrent operators on 2 vCPU / 3 GB is an architecture question as much as a test. |

Every case you cover: flip its marker in `docs/qa/patient-lifecycle.md` from `gap` to `auto`/`ui`/`xunit` **in the same commit**, and remove it from the register.

### Deferred, and deliberately so

The thirteen legacy scripts in `eng/verify/` still carry their own `Session` class and nine hardcode `localhost:5199`, so **`--tier t1` cannot target a remote host**. `eng/verify/_harness.py` exists and the two new scripts use it. Retro-fit the legacy threads onto it **after Phase 1**, not before — rewriting their plumbing in the same pass that fixes their fixture bugs makes it impossible to tell a refactor regression from a pre-existing one. Nine of them are invoked by name in `.github/workflows/ci.yml`; filenames and CLI must not change.

## HOW TO VERIFY

```sh
# the app must be running; local default http://localhost:5199
bash eng/check-lifecycle-traceability.sh --stats     # doc ↔ code join; now in CI
python3 eng/verify/lifecycle-suite.py --tier t0      # read-only, 12 roles × 64 routes
python3 eng/verify/lifecycle-suite.py --tier t1      # the lifecycle; must pass 3x running
```

Fresh database when tier t2 is wanted (it asserts absolute money totals, so it is fresh-DB only):

```sh
docker exec hms-dev-db psql -U postgres -d postgres \
  -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"
```

Also run, since this session could not: `dotnet build -c Release` (warnings are errors) and the four test projects. The ~127 xUnit tests were **not executed** during the QA pass — no `dotnet` on that machine — so treat any `xunit` coverage marker in the lifecycle document as cited, not observed, until you have run them yourself.

## RULES OF ENGAGEMENT FOR THE DEPLOYMENT

- **Read-only probing of production is fine.** `BASE_URL=https://hms.specshipper.com python3 eng/verify/lifecycle-suite.py --tier t0` writes nothing.
- **A mutating run against production requires explicit human agreement**, states what it will write, and is refused without `HMS_QA_ENV` + `HMS_QA_CONFIRM`. Tier t2 against production is refused unconditionally. These interlocks are verified; do not route around them.
- Production still runs `HMS_SEED=true` with `Demo#1234` live, so it is a demo instance, not patient data. **That is exactly why F1 is worth fixing now** — the drift mechanism outlives the demo, and RUNBOOK §9's go-live switch has been rehearsed but never executed.

## WHAT NOT TO DO

- Do not restructure the Playwright suite or the xUnit projects. Add to them.
- Do not "fix" `golden-thread.py`'s absolute-total assertion. It is correct; it is tier t2 for exactly that reason.
- Do not make `DevSeed` reconcile grants without an ADR and the PM's agreement — see F1.
- Do not add a dependency without justifying it against the 2 vCPU / 3 GB budget (G15). The harness is stdlib-only on purpose.
- Do not mark a lifecycle case covered by a test you have not seen pass.

## ESCALATE RATHER THAN GUESS

- **To the PM** (`docs/architecture/09-questions-for-pm.md`): whether discharge-with-a-due should become approval-gated rather than reason-gated (P20, still open); whether the Admin role on production *should* be a superuser.
- **To the architect** (new ADR): the DevSeed reconcile-vs-report decision; how to test 40 concurrent operators inside the memory budget.
- **To QA** (this suite): if a lifecycle case turns out to be unperformable by the role the document assigns it to, that is a finding about the permission matrix, not a licence to switch to a more privileged session. Record it and raise it.
