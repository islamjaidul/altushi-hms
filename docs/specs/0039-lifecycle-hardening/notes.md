# 0039 — notes

Written while drafting. Appended to, not rewritten, once work starts.

## Doc drift this spec should sweep up

A compliance audit run against 0038/0039 found the archive mechanics clean, but flagged
documentation that is now stale. None of it belongs to 0038 (report-only by design), so it lands
here:

1. **`docs/qa/module-coverage.md` M20 row** still reads "no test referenced `SmsQueue`."
   `SmsQueueTests` exists — a pure documentation correction.
2. **`docs/qa/module-coverage.md` M16 row** still reads business logic = `NONE`. Superseded twice:
   spec 0037's `hrm-thread.py` (37/37) and this audit's payroll probes. WP3 commits to rewriting
   the row with evidence.
3. **Three different counts for the t1 tier.** `.claude/skills/qa-lifecycle/SKILL.md:8` says 169
   cases (actual 175) and `:34` says nine threads; `docs/qa/README.md:13` lists ten and `:21` says
   twelve. Reconcile to the real numbers, and record whether `hrm-thread.py` gets a tier or stays
   deliberately separate.
4. **Spec 0034's `plan.md:172-176`** pre-assigned Waves B/C/D to spec numbers 0037/0038/0039, all
   three of which became something else. 0034's `In Progress` status is correct — Waves B–D really
   are unbuilt — but its archived plan now names the wrong specs. Add a note there so a future
   reader does not assume they shipped.
5. **Pre-existing, unrelated:** specs 0006–0010 have no `plan.md` and make no retroactive claim.
   Flagged before in 0032's write-up as "retroactive claims that cannot be verified"; recorded
   again here so it is not lost.

## Status

`In Progress` as of 2026-08-03, with `plan.md` archived and the index row in sync. The handoff
brief for the engineer picking this up is `docs/lifecycle_hardening_prompt.md`.

## Not committed

Everything from 0038 and 0039 is uncommitted at the time of writing — the new spec directories, the
QA docs, and the probes under `eng/verify/audit/`. Two subagents committed probe scripts to `main`
unasked during 0038 and both were reverted with `git reset --soft`; committing is the user's call.
0038's AC3 says the probes "exist as committed scripts", which is not literally true in the git
sense until that happens.

## The sequencing trap

Worth repeating outside `plan.md` because it is the one thing that could make this work *worse*
before it makes it better: **SQLSTATE `23514` is caught nowhere.** Adding WP2's domain CHECK
constraints without WP1's input gate and a `23514` handler converts silent data corruption into
visible HTTP 500s in front of operators. Ship them together.

## Execution log (2026-08-03, spec picked up)

Work ran as one orchestrated session: two page-sweep agents (WP1), a payroll agent (WP3), a
schema-model agent (WP2 contexts), a platform agent (WP6), with the input gate, migrations,
WP4, WP5 and the ADR done directly. Decisions the plan left open were made and recorded in
**ADR-0028**: base-class gate · reject decimals with a message · Bounds constants
(200/40/20/500/4k/10k) · intra-schema FKs only, `ON DELETE RESTRICT`, `NOT VALID` posture ·
orphans retained + constrained-forward (never deleted) · xmin tokens · doctor master in
`appt` (not `adm`) so its FKs stay intra-schema.

Traps that materialised, beyond the plan's list:
- EF scaffolded every new FK `ON DELETE CASCADE` — a delete machine under hard rule 4. Fixed
  structurally: a model-wide `DeleteBehavior.Restrict` loop in each context, then regenerated.
- `AlterColumn text→varchar(n)` would abort migration on any database already holding an
  oversized row; replaced with `CHECK (length(col) <= n) NOT VALID` (columns stay text — the
  snapshot's varchar is deliberate cosmetic drift, see ADR-0028).
- The doctor-master backfill's `setval(…, 1)` consumed id 1 on a fresh database, shifting all
  seeded doctor ids by one and silently breaking `golden-thread.py`'s hardcoded doctor 1.
  Fixed with the three-argument setval (`is_called = rows exist`).
- The `ipd.bed_stay` EXCLUDE constraints are `DEFERRABLE INITIALLY DEFERRED`: EF orders a
  transfer's INSERT before the UPDATE that closes the old stay, so an immediate check would
  refuse a legitimate transfer mid-transaction.
- `probe-validation.py`'s AUD-VAL-13 fixture *posted* pct −50 and a 100 KB dx and relied on
  the defect to obtain its admission; reworked to valid fixtures + explicit reject probes,
  database assertions unchanged (they still go red if either value ever stores again).
- The probe corpus gained AUD-VAL-27..31 (counter session, OT schedule, pharmacy transfers,
  product master, discharge) — five handlers the audit never sampled.

Deliberately deferred, with reasons:
- WP5.2 patient merge repair — PRD `[S]`, no probe covers it, and the repoint-history write
  path deserves its own spec. The gap (MergedInto/Active written nowhere) is unchanged.
- `VALIDATE CONSTRAINT` on the NOT VALID set — after the legacy orphan classes are reconciled
  (ADR-0028 reversal triggers).
- Runtime cut-over to `hms_app` — grants are applied and ready at every boot; the switch is a
  deployment connection-string change now documented in `deploy/compose.yml`.

## Closure evidence (2026-08-03, spec → Done)

**Fresh-database DoD run** (recreated `hms` + `hrm`, hosts rebooted): `lifecycle-suite.py
--tier all` GREEN — 14 scripts, 0 failed, 12/12 roles; `probe-validation` 144 payloads /
0 confirmed defects; `probe-authz-seams` 16/0; `probe-public-phi` 5/0; `probe-payroll-math`
10/0; `probe-payroll-staged` 4/0; `hrm-thread` 37/0; traceability OK (175 cases, 162 covered,
13 known gaps).

**Used-database double run** (same `hms` after the full pass + probes + repeated t1): the
second consecutive t1 went red exactly as the DoD intended it to — three checks, all at the
ward-indent FEFO issue. Cause: every Napa batch at the main outlet read zero. `stock_move`
arithmetic closed perfectly (980 received = 980 consumed), i.e. not a leak but a *class*:
t1 scripts provision their own patients but issued seeded stock and never replenished it.
Fixed with `_harness.ensure_demo_stock(make)` — no-op above 15 sellable units, otherwise
replenishes 100 through the operator path (PO → approve⚿ → order → GRN), called by
`lifecycle-thread` and `ipd-thread`. After the fix: **t0+t1 GREEN twice consecutively**,
`hrm-thread` green three times consecutively, `probe-validation` 144/0 twice on the used DB.

**`dotnet test hms-erp.slnx`: 407 passed / 0 failed** (pre-0039 baseline 343). The new FKs
correctly broke 29 fixtures that invented parent rows; all repaired by seeding real parents
(`IpdSeed`, doctor rows, counter rows) — no constraint weakened. Two were not fixtures:

- **HR policy EXCLUDEs refused legitimate re-dating** (23P01). `PolicyResolver.OpenOrNewAsync`
  closes the open rule and inserts its successor in one SaveChanges; EF batches the INSERT
  first, and the 0034-era GiST constraints were statement-timed. `HrPolicyExcludeDeferrable0039`
  rebuilds all eight as `DEFERRABLE INITIALLY DEFERRED` — same decision as `ipd.bed_stay`
  (WP2), same invariant at COMMIT, additive under 03 §12. Applied to the live `hrm` DB.
- **HR tests ran blind to their own branch.** The WP5 filter pins contexts to
  `BranchScope.Current`; tests seeding per-test branches now set the scope the way
  `BranchResolutionMiddleware` does per request. The WP3 agent's `ZDiagTests` debugging
  scratch became `HrBranchIsolationTests` — an SQL-level proof that a row one branch wrote
  is invisible to a context scoped elsewhere and visible to one scoped to it.

**Probe amendments in this pass** (intent preserved, fix-shape assumptions removed — each can
still go red against the pre-fix behaviour):

- `probe-payroll-math` AUD-M16-03 asserted `unpaid == 0`, written when no set-pay screen
  existed. Its own message stated the real invariant ("…and no screen to give them one");
  the check now walks every unpaid employee's record and requires the set-pay POST form.
  The unpaid rows were "QA Probe Kamal" — hired by `hrm-thread` itself, never paid.
  The thread now pays its hire through the 0039 screen (new case HRM-EMP-08, thread 37→38),
  so the screen is exercised end-to-end every run and the accumulation stops.
- `probe-payroll-staged` AUD-M16-07 computed expected overtime from `gross // days` — but
  gross *includes* the OT being predicted, so the expectation chased its own answer and
  drifted red once any other earning existed. It now derives the base as `gross − ot_paid`.
  Verified arithmetic: engine paid 584 from base 14,500/31 days (exact multiply-first,
  round once = 585 ± 1); both pre-fix truncation modes (0 Tk and whole-taka-per-minute)
  still fail the check.

Guards at close: additive-migrations OK (HR script incl. the constraint rebuild), fkeys OK,
no-hard-deletes OK, ui-tokens OK, no-external-hosts OK, no-native-date OK.

Nothing committed — committing is the user's call.
