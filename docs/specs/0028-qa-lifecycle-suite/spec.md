# 0028 — QA patient-lifecycle suite: canonical doc, role-driven runner, QA agent

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §5 (all built modules), §7, §12 (role matrix), §11, §14
- **MVP:** in scope — this specifies no new product behaviour. It makes the behaviour already
  shipped in specs 0006–0027 continuously verifiable, which the §9A release gate assumes.

## Problem

Thirteen modules have shipped. The verification apparatus that grew alongside them is
substantial — thirteen Python HTTP thread-drivers, a fifteen-file Playwright suite, ~127 xUnit
tests, five guard scripts, an upgrade gate — but it has no spine, and five specific weaknesses
make it unreliable as a regression net.

1. **No canonical lifecycle document.** Nobody can answer "what does a patient's whole journey
   look like, and which of those steps is proven?" The nearest artefact,
   `eng/verify/lifecycle-thread.py`, is code: its coverage is invisible unless you read it.

2. **Authorization is only ever tested negatively.** `eng/verify/ui/tests/authz.spec.ts` proves
   nineteen hand-picked denied pairs. **Nothing anywhere asserts that a role can do its own
   job.** A permission accidentally removed from a role would break a real shift and pass CI.

3. **Five of the twelve roles never appear in the cross-module run.**
   `eng/verify/lifecycle-thread.py` opens seven sessions; `chowdhury` (OPD Consultant),
   `moinul` (Radiology Technician), `shaheen` (OT In-charge), `farhana` (Pathologist) and
   `admin` never drive it. Work those roles alone can perform is therefore never exercised
   end to end — including effective-dated repricing, which only `admin` can do and which G6
   names as a permanent money invariant.

4. **The suite cannot be pointed at a deployment.** Nine of thirteen scripts hardcode
   `http://localhost:5199`; each carries its own copy of the same `Session` class. Testing the
   real VM means editing files.

5. **Coverage drift is silent.** The Playwright route and permission tables in
   `eng/verify/ui/helpers/users.ts` are hand-synced with `DevSeed.cs`, `Perm.cs` and
   `ModuleNav.cs`, and eleven of the sixty-four protected routes are absent from them —
   including `/ipd/folio`, `/ipd/discharge`, `/emr/consult`, `/radiology/report`, `/ot/case`
   and `/lis/amend`, which are the money- and clinically-critical screens.

Spec 0020's notes already diagnosed the underlying failure mode: *"Each module's tests
exercised that module with data the test itself created. All three defects live between
modules."* Roles are the second axis of the same problem — a step performed by the wrong
session proves nothing about the operator who actually performs it.

## Requirements

- **[M]** A canonical `docs/qa/patient-lifecycle.md` enumerating every lifecycle stage and edge
  case across the built modules, each case carrying a stable `LC-<STAGE>-<nn>` id, the **role
  that performs it**, the module and screen, the PRD §, and its coverage status.
- **[M]** Every case names a real demo user from `DevSeed.cs`; the lifecycle is driven by the
  role that owns each step, not by one convenient privileged session.
- **[M]** Positive authorization coverage: for all twelve roles across all protected routes,
  reachable where the role holds the permission and refused where it does not.
- **[M]** A shared `eng/verify/_harness.py` so every script honours `BASE_URL` and shares one
  `Session`, and an environment interlock that refuses to mutate a non-local target without an
  explicit environment and typed confirmation.
- **[M]** A runner that executes the suite by tier and reports per-case results keyed to the
  document's ids, failing a run that did not exercise all twelve roles.
- **[M]** A `qa-lifecycle` subagent, invocable on command, that selects the tier for the target
  environment, runs it, and reports a triaged table. It never edits application code.
- **[S]** A CI guard joining document and runner so the two cannot drift.
- **[S]** Production runs identifiable and reversible: `QA-` tagged records and a run manifest.
- **[C]** Retro-fitting the existing thirteen scripts onto the shared harness.

## Acceptance criteria

1. `docs/qa/patient-lifecycle.md` exists, every case has an `LC-` id, a performing role and a
   coverage marker, and the gap register lists every uncovered case with a severity.
2. `python3 eng/verify/role-journeys.py` logs in as all twelve demo users and asserts the full
   role × route matrix in both directions.
3. `eng/verify/lifecycle-suite.py --tier t0` writes nothing, verified by row counts before and
   after on `reg.patient`, `bill.invoice`, `bill.receipt` and `kernel.audit_event`.
4. A mutating run against a non-localhost target refuses without `HMS_QA_ENV` and
   `HMS_QA_CONFIRM`; tier t2 against production refuses unconditionally.
5. `bash eng/check-lifecycle-traceability.sh` exits 0, and fails when a permission is added to
   `Perm.cs` with no lifecycle case.
6. A T1 run that skips a role fails as incomplete rather than passing.
7. The `qa-lifecycle` agent runs the suite against a named environment and returns the case
   table plus the role matrix.

## Out of scope

- **Fixing the defects the suite finds.** Per the agreed policy this pass documents gaps with a
  severity; remediation is a follow-up spec.
- Load and concurrency testing. None exists in the repo
  (`docs/architecture/06-deployment.md` §2a is explicit that the functional suite *"says nothing
  about 40 operators at once"*). Recorded as a gap, not built here.
- Restructuring the Playwright suite or the xUnit projects.
- Modules M12–M19, which have no code.

## Risks / open questions

- Refactoring the thirteen scripts touches nine that the CI `upgrade-path` job invokes by name.
  Filenames and CLI must stay identical.
- Production currently runs with `HMS_SEED=true` and the demo cast live. When the RUNBOOK §9
  go-live switch is executed the demo users disappear and the tier table must be revisited.
- Driving each step as its true role may reveal steps a role cannot actually perform. That is a
  finding, not a harness bug, and goes to the gap register.

## How each acceptance criterion was verified

1. **Document.** `docs/qa/patient-lifecycle.md` — 169 cases, 17 stages; every case carries an
   `LC-` id, a performing role and a coverage marker. 130 covered, 39 gaps, all 39 in the
   register with a severity (13 High). Counted by `check-lifecycle-traceability.sh --stats`.
2. **Role journeys.** `python3 eng/verify/role-journeys.py` → 15 cases, **12/12 roles**, 768
   route assertions in both directions, 0 failures.
3. **t0 writes nothing.** Row counts on `reg.patient`, `bill.invoice`, `bill.receipt`,
   `kernel.audit_event` identical before and after (`49|104|108|528` → `49|104|108|528`).
4. **Interlocks refuse.** Verified all four: non-localhost + mutating tier with no `--env`
   refuses; `--env prod --tier t2` refuses unconditionally; `--env prod --tier t1` without
   `HMS_QA_CONFIRM` refuses; `--tier t0` against the deployment runs read-only.
5. **Guard bites.** `check-lifecycle-traceability.sh` exits 0 on the current tree, and during
   development caught two real drifts: `appointments.create` enforced nowhere (now finding F2)
   and an optional-route-parameter mismatch on `/emr/charts`.
6. **Role completeness.** A t1 run in which `ot-thread.py` failed correctly reported
   `INCOMPLETE: these roles never drove the run — shaheen`.
7. **Agent and skill.** `.claude/agents/qa-lifecycle.md` and
   `.claude/skills/qa-lifecycle/SKILL.md` created in house style, and the harness confirmed
   `qa-lifecycle` registered as an invocable skill. **Partially verified:** the commands the
   agent drives were each executed by hand and are green, but the agent itself has not been run
   end to end — so its report format is unproven. First real use will confirm it.

**Not met as specified:** the `[C]` retro-fit of the thirteen legacy scripts onto `_harness.py`
was deferred — reasoning and its consequence (t1 cannot yet target a remote host) in `notes.md`.

Findings from the first run are in `docs/qa/findings-2026-07-28.md`.
