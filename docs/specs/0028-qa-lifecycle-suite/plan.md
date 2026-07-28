# 0028 — Plan

## Approved: 2026-07-28

# QA patient-lifecycle: canonical doc, traceable runner, and a QA agent

## Context

The repo has a lot of verification machinery but no **spine** holding it together. Today there
are 13 Python HTTP thread-drivers in `eng/verify/`, a 15-file Playwright suite in
`eng/verify/ui/`, ~127 xUnit tests across 4 projects, 5 guard scripts and a CI upgrade gate.
What is missing is the thing a QA engineer actually works from:

1. **No canonical lifecycle document.** Nobody can answer "what does a patient's whole journey
   look like, and which of those steps is proven?" `eng/verify/lifecycle-thread.py` is the
   closest artefact, but it is code, covers one happy path plus a few seams, and its coverage
   is invisible unless you read it.
2. **No traceability.** `eng/verify/edge-cases.py` has 8 cases with no stable IDs. The
   Playwright route/permission tables in `helpers/users.ts` are hand-synced with `DevSeed.cs`,
   `Perm.cs` and `ModuleNav.cs` — drift is silent. Nothing links a test back to a lifecycle
   step or a PRD §.
3. **Cannot target a deployment.** 9 of the 13 scripts hardcode `http://localhost:5199`; only
   `emr-thread`, `ot-thread`, `radiology-thread` and `golive-rehearsal` honour `BASE_URL`.
   Testing the real VM means editing files.
4. **Every script re-implements the same `Session` class** (login, cookie jar, antiforgery
   token scraping) — ~13 copies that drift independently.
5. **No QA agent, skill or command** anywhere in the project or global config.

Spec 0020's own notes name the underlying failure mode precisely: *"Each module's tests
exercised that module with data the test itself created. All three defects live between
modules."* The lifecycle doc is the systematic version of that lesson.

**Outcome:** one canonical lifecycle document that enumerates every stage and edge case against
the 13 implemented modules; a shared, environment-aware harness so the same suite runs against
local, the VM, or production; a runner whose cases map 1:1 to the document's case IDs; a guard
that fails if doc and runner drift apart; and a `qa-lifecycle` subagent invoked on command that
picks the right safety tier for the target environment, runs it, and reports a triaged table.

Implemented modules the doc must cover (per code, not docs): M1 Registration, M2 Front Desk,
M3 Appointments, M4 Billing/Cash/Day-Close, M5 EMR+Prescription+5A-7 nursing, M6 IPD & Folio,
M7 OT, M8 Diagnostics, M9 LIS, M10 Radiology, M11 Pharmacy, M20 Notifications, M21 Admin/Audit/
Approvals, M22 Dashboard, R3 public displays. **Not** implemented, so out of scope: M12–M19.

## Decisions taken (confirmed with the user)

- **Production is writable, but tagged and confirmed.** Not read-only. Mutating runs against
  production are permitted with a typed confirmation and QA-tagged records.
- **Shared harness + traceable runner.** Extract the duplicated `Session`, make everything
  `BASE_URL`-aware, add one runner keyed to the doc's case IDs. Reuse the existing scripts, do
  not replace them.
- **Document gaps now; automate and fix in a follow-up.** Uncovered cases are recorded in the
  doc's gap register with a severity. No defect-hunting in this pass.

## Rule 0 compliance

This is a non-trivial change, so it needs a spec **first** (`CLAUDE.md` Rule 0, `spec-flow`).

- Create `docs/specs/0028-qa-lifecycle-suite/` — next free ID is 0028.
- `spec.md` at `Draft` in the house style (`# 0028 — <Title>`, then `- **Status:**` /
  `- **Date:** 2026-07-28` / `- **PRD ref:**` / `- **MVP:**` bullets, then `## Problem` /
  `## Requirements` with `[M]`/`[S]`/`[C]` / `## Acceptance criteria` / `## Out of scope` /
  `## Risks / open questions`).
- Add the index row to `docs/specs/README.md` **in the same step** — the `Stop` hook
  (`.claude/hooks/spec-integrity.py`) reports drift immediately otherwise.
- Copy this plan verbatim to `docs/specs/0028-qa-lifecycle-suite/plan.md`, set Status
  `Approved`, add the `## Approved: 2026-07-28` line.
- `tasks.md` ticked as work proceeds; `notes.md` at the end with the gap register summary and
  follow-ups. Close at `Done` stating how each acceptance criterion was verified.

---

## Deliverable 1 — the lifecycle document

**`docs/qa/patient-lifecycle.md`** (new `docs/qa/` directory). It is neither PRD (business
requirements, PM-owned) nor architecture (technical decisions, architect-owned), so it gets its
own home rather than violating the Rule 1 role boundary.

### Case ID scheme

`LC-<STAGE>-<nn>` — stable, never renumbered, append-only once published. Stage prefixes:

`ROLE` per-role day-in-the-life · `REG` registration · `QUE` queue/appointments · `FD` front
desk · `EMR` consult & orders · `DX` diagnostics order + payment gate · `BIL` billing/cash/
day-close · `LAB` LIS · `RAD` radiology · `PHA` pharmacy · `ADM` admission · `NUR` inpatient
nursing · `OT` theatre · `DIS` discharge · `EXIT` terminal exits · `BLK` R4 bill-block ·
`XCUT` cross-cutting (incl. separation of duties).

### Per-case row format

Each case is a table row carrying: **ID · what happens · expected behaviour (observable, in
operator language) · **performed by** (demo user + role) · module + screen · PRD § · coverage**.
Coverage is one of `auto:<script>#<case>` · `auto:ui:<spec file>` · `auto:xunit:<test class>` ·
`manual` · `gap`.

The **performed by** column is mandatory and is the point of the document, not decoration. A
lifecycle case is only real if it is executed by the role that performs it in the hospital. The
suite therefore logs in as the actual operator at every step rather than reusing one convenient
privileged session.

### The 12 roles and their seeded grants

From `src/Hms.Web/DevSeed.cs:18-87` — 12 roles, 12 demo users, password `Demo#1234`, against the
38 permissions in `src/Hms.Web/Perm.cs`:

| User | Role | Owns the lifecycle stages |
|---|---|---|
| `jashim` | Receptionist | `REG`, `QUE`, `FD`, and **beds/admissions/transfers** (`ipd.manage`) |
| `rasel` | Billing Operator | `BIL`, `DX` order creation, **discharge settlement** (`ipd.settle`) |
| `ripon` | Lab Technologist | `LAB` collect + result entry — **cannot verify** |
| `farhana` | Pathologist | `LAB` verify (e-sign) **and** `RAD` report write |
| `moinul` | Radiology Technician | `RAD` study perform — **cannot report** |
| `shahid` | Billing Supervisor | approvals, session close — **has no `ipd.settle`** |
| `nasrin` | Nurse | `NUR` vitals + charts, `ipd.service.post` — **cannot write a note** |
| `chowdhury` | OPD Consultant | `EMR` note write + test ordering — nothing financial |
| `shaheen` | OT In-charge | `OT` schedule + record — money stays with billing |
| `parvin` | Pharmacist | `PHA` sale, purchase, stock, **and counter open/close** |
| `admin` | Admin | users, audit, approvals, **masters/repricing** |
| `md` | MD | **the only holder of `dashboard.read`**, plus approvals and audit |

Three consequences the implementation must respect, each verified against the seed data above:

- The `LC-BIL` effective-dated repricing case needs an **`admin`** session to change the price —
  no billing role holds `admin.masters.manage`. Existing threads have no admin session, so this
  case cannot currently be automated as written.
- The `LC-XCUT` dashboard case needs **`md`**; no other role can load `/dashboard`.
- One admission crosses **three** roles (`jashim` admits → `nasrin` posts services → `rasel`
  settles), and `shahid` cannot settle it despite being the supervisor.

### Stage `ROLE` — a day in the life of each of the 12 roles

A dedicated stage, one case group per role: **log in → land on the default screen → the sidebar
shows exactly the entitled items and nothing more → perform the role's core duty successfully →
attempt the adjacent duty and be refused**. This is the positive half of authorization, which
nothing in the repo currently asserts — `eng/verify/ui/tests/authz.spec.ts` only proves the 19
denied pairs. The sidebar assertion tests `Hms.Kernel/Auth/NavComposer.cs` (nav = permissions ∩
entitled modules) against the live grants rather than the hand-written table in
`eng/verify/ui/helpers/users.ts`.

### Separation-of-duties cases (first-class group under `XCUT`)

Drawn from the grant matrix, these are cases where the *absence* of a permission is the safety
property:

- `ripon` enters a result; **`ripon` cannot verify it** — only `farhana` can (four eyes on a
  clinical release).
- `moinul` performs the study; **`moinul` cannot write the report** — only `farhana` signs.
- `nasrin` records vitals and MAR doses; **`nasrin` cannot write or sign a prescription**.
- `chowdhury` orders tests; **`chowdhury` cannot bill or collect** for them.
- `shaheen` schedules and records the OT case; **`shaheen` cannot post the completion charge**.
- The operator who requests a discount/refund/write-off is **not** the one who decides it —
  `rasel` requests, `shahid`/`admin`/`md` approve.
- `shahid` holds `admin.approvals.decide` but **not** `ipd.settle`; `rasel` holds the reverse.

### Role × module coverage matrix

A grid in the doc: 12 roles down, the 15 implemented module surfaces across, each cell marked
`+` (positively exercised — the role does its job), `−` (negatively exercised — the role is
refused), or blank. A blank cell is a coverage hole and is visible at a glance. Today's
`lifecycle-thread.py` uses only 7 of the 12 sessions (`eng/verify/lifecycle-thread.py:57-63`),
so `chowdhury`, `moinul`, `shaheen`, `farhana` and `admin` never appear in the cross-module
run — the matrix makes that immediately obvious.

### Stage coverage the doc must contain

The doc walks one patient end to end, then branches. Representative cases per stage — the
implementation pass enumerates the full set (expect ~130–160 cases):

| Stage | Driven by | Cases include |
|---|---|---|
| `ROLE` | all 12 | per role: login · default screen · sidebar = permissions ∩ entitlements · core duty succeeds · adjacent duty refused |
| `REG` | `jashim` | new patient · returning patient found by name/UHID/phone · phone typed as `+880`, with dashes, with spaces, digits only, tail-only (spec 0020 gap 1) · no phone at all · duplicate guard fires and is acknowledged (edge 23) · unknown/unconscious patient with no name or age · age vs DOB entry · minor with guardian · patient-type general vs corporate · UHID uniqueness · ID card print + reprint · patient merge after records exist |
| `QUE` | `jashim` | serial issued · advance/skip · doctor has no session today · session full · no-show then re-issue · public queue display masks the name (R3) |
| `FD` | `jashim` | live estimate includes **unposted** bed-days without posting them · admitted-patient banner on a counter sale · read-only composition shows nothing the operator lacks permission for |
| `EMR` | `chowdhury` writes, `nasrin` vitals | vitals recorded by the nurse · draft note saved and resumed · finalise makes it immutable · correction supersedes rather than overwrites · template + favourite reuse · tests ordered from the note reach Diagnostics · prescription prints, and a corrected prescription prints as a new version · **the nurse cannot write or sign the note** |
| `DX` | `chowdhury` orders, `rasel` bills | order creates unbilled charge lines · **payment in full releases the lab; partial payment must not** · discount above threshold routes to approval and blocks until decided · order cancelled after payment · order for an admitted patient lands on the folio, not an encounter |
| `BIL` | `rasel`, `shahid` approves, `admin` reprices | counter session open/close · cart add/remove · double-click Save bills once (submission token) · invoice identity `net = gross − discount + tax + rounding_adj` · `Σ receipts + due = net` · advance collected · due collected later · refund as negative receipt with approval · invoice cancelled, never deleted · overpayment refused · discount larger than the bill refused · **a price change made by `admin` never alters a historical invoice** (effective-dated `adm.rate_version`) · day-close variance = counted − expected · **night shift: a 01:00 Dhaka receipt belongs to the previous business day** (spec 0027) · carry-close approval |
| `LAB` | `ripon` → `farhana` | sample collected → received · rejected sample spawns a recollection · result entry by the technologist · reference bands vary by age and sex · critical flag shown · report is watermarked provisional until verified · **only the pathologist can verify — the technologist who entered cannot** · amend requires approval and versions the result · delivery logged · `/public/reportstatus` self-lookup by `LB-` number leaks nothing else |
| `RAD` | `moinul` → `farhana` | study accessioned from the order · appears on the right modality worklist · marked done by the technician · **the technician cannot report** · templated report written and signed by the reporting consultant (a radiology report *is* a `lis.result`) · printed |
| `PHA` | `parvin` | POS sale · FEFO allocation picks the earliest expiry · expired batch cannot be sold · stock cannot go negative · return restocks the exact batch sold · ward indent → folio charge · indent exceeding shelf stock · quarantine · supplier return · write-off requires approval · stock audit count → adjust → post · inter-outlet transfer send/receive · staff-sale tagging · pharmacist opens and closes their own counter |
| `ADM` | `jashim` | bed availability · reservation confirmed / cancelled · admit · transfer bed and ward · bed-day accrual and catch-up (`UNIQUE(admission_id, on_date)` idempotency) · out-of-service bed · bed stuck in `Cleaning` with no housekeeping role (spec 0020 follow-up) |
| `NUR` | `nasrin` | MAR dose scheduled → administered · dose missed · glucose reading · shift handover recorded · ward service posted to the folio |
| `OT` | `shaheen` | scheduled · theatre double-booking refused · surgeon double-booking refused · patient marked ready · started · completed posts completion charges · consumable issued from stock · cancelled · postponed · **the in-charge cannot bill the case** |
| `DIS` | `jashim` → `rasel` | initiate → clinically clear → folio locks → settlement draft → reopen (approval) → financially settled → discharge · discharge **with** an outstanding due requires a typed reason and lands in tier-2 audit (§3.2, no hard block) · certificate issued · certificate reprint is audited · **`shahid` cannot settle despite being the supervisor** |
| `EXIT` | `rasel`, `shahid` | death — the family can still be billed and the folio settles · absconded — the due survives for follow-up · settled-while-blocked is not a life sentence in the ward (spec 0021) |
| `BLK` | `jashim` blocks, `shahid`/`md` decide | R4 block freezes the folio · release unfreezes · both are approval-gated |
| `XCUT` | all 12; `md` dashboard, `admin` audit | the separation-of-duties group above · permission denial per role across all 55 routes · handler-level POST bypass attempt · entitlement gating hides an unlicensed module · **`/dashboard` loads for `md` and for nobody else** · `kernel.audit_event` is append-only and the `hms_app` role has no DELETE grant · one business action = one transaction across 14 `DbContext`s · power cut mid-transaction leaves no half-write · two operators editing one folio concurrently · SMS queued and resent · whole-taka rounding, no paisa anywhere · Asia/Dhaka with no DST |

### Gap register

A final `## Gap register` section lists every case marked `gap`, each with a severity using the
`spec-auditor` rubric (**High** = money, permissions, audit or a terminal exit is unproven ·
**Medium** = a cross-module seam is unproven · **Low** = cosmetic or convenience). This section
is the input to the follow-up spec. Known likely entries, already identified during
exploration:

- **No positive authorization coverage anywhere** — only the 19 denied pairs in
  `eng/verify/ui/tests/authz.spec.ts`. No test asserts a role *can* do its job. (High)
- **5 of the 12 roles never appear in the cross-module run** — `chowdhury`, `moinul`,
  `shaheen`, `farhana`, `admin` (`eng/verify/lifecycle-thread.py:57-63` uses 7 sessions). (High)
- **The separation-of-duties rules are unasserted** — nothing proves `ripon` cannot verify his
  own result, or that `moinul` cannot sign a report. (High)
- **Effective-dated repricing is untestable with the current sessions** — it needs `admin`,
  which no thread logs in as, so G6's "changing a price never alters a historical invoice"
  invariant has no end-to-end proof. (High)
- No load or concurrency test exists anywhere in the repo (`docs/architecture/06-deployment.md`
  §2a says so explicitly: *"single-user functional suite… says nothing about 40 operators at
  once"*). (High)
- Unknown/unconscious patient; patient merge mid-visit; price change between order and invoice;
  partial payment must not release the lab. (Medium)

**`docs/qa/README.md`** — short: what the tiers are, how to run each environment, where run
manifests land, and the rule that the doc is the source of truth and the runner follows it.

---

## Deliverable 2 — shared harness, environment awareness, traceable runner

### `eng/verify/_harness.py` (new)

Extract what all 13 scripts duplicate, keeping the existing idiom (stdlib only — `urllib`,
`http.cookiejar`, `re`; no new dependencies, per G15 and the RAM budget):

- `Session(user, password)` with `get` / `post` and antiforgery token scraping — lifted from
  `eng/verify/lifecycle-thread.py:12-30`, which is the cleanest copy.
- `BASE` resolved as `os.environ.get("BASE_URL", "http://localhost:5199")` — the idiom already
  used at `eng/verify/radiology-thread.py:23`.
- `step(n, text)` / `check(cond, msg)` / the `fail` accumulator and exit code — from
  `lifecycle-thread.py:36-43`.
- `open_counter(sess, kind, float_amt)` — from `lifecycle-thread.py:46-52`, duplicated in
  several scripts.
- `case(id, title)` emitting a machine-readable `LC-…` line so the runner and the agent can
  parse results.
- `guard()` — the safety interlock (below). Called at import time by every mutating script.

### Refactor the 13 scripts onto it

`discount-and-dues.py`, `edge-cases.py`, `emr-thread.py`, `frontdesk-check.py`,
`golden-thread.py`, `ipd-thread.py`, `lifecycle-thread.py`, `ot-thread.py`, `page-timings.py`,
`pharmacy-full.py`, `pharmacy-thread.py`, `radiology-thread.py`, `golive-rehearsal.py`.

**Filenames and CLI must not change** — `.github/workflows/ci.yml`'s `upgrade-path` job and
`eng/verify/upgrade/run.sh` invoke 9 of them by name. Each script keeps its own assertions; only
the plumbing moves. Existing case titles in `edge-cases.py` gain their `LC-` IDs.

### Safety tiers

| Tier | What runs | Writes? |
|---|---|---|
| **T0 probe** | `/health` · login as **all 12** demo users · for each, the sidebar equals its permissions ∩ entitlements (positive nav) · `nav-smoke.sh` over the 55 routes as each role, asserting 200 where entitled and 403 where not · the 19 denied pairs · the 2 anonymous public surfaces · `md`-only dashboard read · day-close report reads · `measure-rss.sh` | no |
| **T1 lifecycle** | `lifecycle-thread`, `edge-cases`, the per-module threads, `frontdesk-check` — all dirty-DB tolerant, each asserting only about records it created, and **each step driven by the role that owns it** | yes, own data |
| **T2 absolute** | `golden-thread`, `discount-and-dues`, `golive-rehearsal`, day-close absolute totals | yes, **fresh DB only** |

`golden-thread.py` asserts absolute money figures (e.g. "today's income is exactly ৳550"), which
is why it is T2 and fresh-DB-only; everything in T1 is already dirty-DB tolerant.

### Environment gate — `guard()`

| `--env` | T0 | T1 | T2 |
|---|---|---|---|
| `local` (BASE is localhost) | yes | yes | yes |
| `vm` (any non-localhost host) | yes | typed confirmation | refused |
| `prod` | yes | typed confirmation + tagging + manifest | **hard refuse** |

Fail-safe default: **any `BASE_URL` that is not localhost is treated as non-local** and requires
an explicit `--env` plus confirmation. Confirmation is both a `--i-understand-this-writes-to=<host>`
flag and an interactive typed `PRODUCTION` prompt; the flag alone is not enough in a TTY. T2
against `prod` is refused unconditionally — `golive-rehearsal.sh` already refuses to boot over
an occupied port, and this extends the same instinct.

### Production tagging and reversal

Rule 4 forbids financial hard deletes, so a production run is designed to be **identifiable and
reversible, never erasable**:

- Every patient created is named `QA-<runid> <name>`, greppable in `/registration` and the
  type-ahead. `runid` is the UTC timestamp of the run.
- A run manifest at `eng/verify/runs/<env>-<runid>.json` records every id created — patient,
  encounter, invoice, receipt, order, admission, folio, OT case — so finance can find them.
- The run ends with a reversal pass: refund receipts and cancel invoices so day-close nets out.
- The doc states plainly what **cannot** be undone: `kernel.audit_event` rows, `ipd.bed_day`
  rows, `pharm.stock_move` ledger entries, issued number-series values, and any SMS actually
  sent. A production T1 run permanently adds audit and ledger history.
- Operational advice in `docs/qa/README.md`: run production T1 **before counters open**, or
  accept a dashboard/day-close blip until the reversal pass completes.

### `eng/verify/lifecycle-suite.py` (new)

The runner. Takes `--env`, `--tier`, optional `--stage ROLE,REG,BIL,…`, resolves the tier list,
enforces `guard()`, executes the scripts in dependency order (`golden-thread` before
`discount-and-dues`; Playwright's documented prerequisite ordering respected), and emits a
per-case table keyed to `LC-` IDs plus a JSON summary the agent parses. Non-zero exit on any
red case.

**Role-completeness interlock.** `_harness.py` records every username a run logs in as. At the
end of a T1 run the suite asserts that **all 12 demo users appear**, and prints the role ×
module matrix actually achieved. A run that never logs in as `chowdhury`, `moinul`, `shaheen`,
`farhana` or `admin` — which is the status quo — fails as incomplete rather than passing
quietly. This is the mechanism that keeps the lifecycle honest about being role-driven.

The `ROLE` stage lives in a new `eng/verify/role-journeys.py` (T0, non-mutating): 12 logins, 12
sidebar assertions, 12 core-duty reads, and the refusal of each role's adjacent duty. The
mutating half of each role's duty is asserted inside the T1 threads where that work already
happens, so the two do not duplicate each other.

### `eng/check-lifecycle-traceability.sh` (new guard)

Three joins, all cheap greps:

1. Every `LC-` ID in `docs/qa/patient-lifecycle.md` is either marked `gap`/`manual` or is
   emitted by a T0–T2 script, and every `LC-` ID emitted by a script exists in the doc.
2. Every case row names a **performed by** user, and every named user exists in the `Cast`
   array of `src/Hms.Web/DevSeed.cs`.
3. Every role in `DevSeed.cs`'s `Roles` dictionary appears in the doc's role × module matrix,
   and every permission string in `src/Hms.Web/Perm.cs` is referenced by at least one case —
   positively or negatively. A newly added permission with no lifecycle case fails the build.

Check 3 is the one that kills the silent drift already affecting
`eng/verify/ui/helpers/users.ts`, whose permission and route tables are hand-synced with
`DevSeed.cs`, `Perm.cs` and `ModuleNav.cs`. Add all three to the existing guard block in
`.github/workflows/ci.yml` alongside `check-fkeys.sh` et al.

---

## Deliverable 3 — the QA agent and its trigger

### `.claude/agents/qa-lifecycle.md` (new)

Matching `.claude/agents/spec-auditor.md` exactly in shape: unquoted single-line `description`
with an em-dash and a "Use when…" trigger clause, `tools: Bash, Read, Grep, Glob`,
`model: sonnet`, H1 in sentence case, no heading deeper than `##`, no emoji, ~60 lines.

Body sections:

- **Mandate + boundary.** It runs the suite and reports; it **never edits application code or
  fixes defects**. The main session decides what to act on — same boundary as `spec-auditor`.
- **Environments and safety.** The tier table, the fail-safe non-localhost default, and the
  hard rule: *before any mutating run against a non-local target, state exactly what will be
  written and get the user's explicit go-ahead in chat — the CLI flag is never sufficient on
  its own.* T2 against production is refused, full stop.
- **Preflight.** Confirm the target answers `/health`; confirm which build is deployed; for
  local, warn about the two documented traps from spec 0023 notes — a leftover app instance on
  port 5199 silently serves the wrong database, and the Razor build server goes stale after
  `.cshtml` edits (`dotnet build-server shutdown`).
- **Method.** Cheap to expensive: T0 probe → T1 lifecycle → T2 only on a fresh local DB. Stop
  and report if T0 is red rather than burning time on T1. Never read
  `docs/project_manager.md` whole (123 KB) — grep to the §.
- **Report format.** One-line verdict (`All green — N cases` / `N failed`), then:

  | LC ID | Stage | Performed by | Case | Result | Evidence |
  |---|---|---|---|---|---|

  then the role × module matrix achieved by the run (any blank cell called out), then failures
  triaged by severity against the doc's rubric, then the run manifest path if anything was
  written, then what could **not** be run and why.
- **Closing rule.** Honesty about unrun cases; never report a pass that was not observed; never
  invent findings.

### `.claude/skills/qa-lifecycle/SKILL.md` (new)

So `/qa-lifecycle`, `/qa-lifecycle local`, `/qa-lifecycle prod` invoke the agent. Frontmatter is
`name` + one-line unquoted `description` only, matching the five existing project skills. Body:
argument grammar, the tier table, the production confirmation ritual, and a pointer to
`docs/qa/patient-lifecycle.md` as the source of truth.

---

## Files

**Create**

- `docs/qa/patient-lifecycle.md` — the canonical doc (primary deliverable)
- `docs/qa/README.md`
- `eng/verify/_harness.py`
- `eng/verify/role-journeys.py` — the `ROLE` stage, 12 roles, non-mutating
- `eng/verify/lifecycle-suite.py`
- `eng/check-lifecycle-traceability.sh`
- `.claude/agents/qa-lifecycle.md`
- `.claude/skills/qa-lifecycle/SKILL.md`
- `docs/specs/0028-qa-lifecycle-suite/{spec.md,plan.md,tasks.md,notes.md}`

**Modify**

- The 13 `eng/verify/*.py` scripts — same pattern in each: delete the local `Session`/`check`/
  `open_counter` definitions, `from _harness import …`, call `guard()`, add `LC-` IDs to case
  titles. Representative: `eng/verify/lifecycle-thread.py`, `eng/verify/edge-cases.py`,
  `eng/verify/pharmacy-full.py`.
- `eng/verify/nav-smoke.sh` — accept `BASE` from the environment instead of hardcoding line 4.
- `eng/verify/README.md` — point at the new doc and the tiers.
- `.github/workflows/ci.yml` — add the traceability guard to the existing guard block.
- `docs/specs/README.md` — the 0028 index row.

**Deliberately not touched:** the Playwright suite, the 4 xUnit projects, and
`.github/workflows/ci.yml`'s `upgrade-path` job beyond the guard addition. The doc references
their existing coverage; this pass does not restructure them.

---

## Verification

1. **Traceability holds.** `bash eng/check-lifecycle-traceability.sh` exits 0 — every `LC-` ID
   in the doc resolves to a script case, a `manual` marker, or a `gap` entry, and vice versa;
   every **performed by** user exists in `DevSeed.cs`'s `Cast`; every permission in `Perm.cs`
   is referenced by at least one case. Prove the guard bites by temporarily adding a fake
   permission constant and confirming a red build, then reverting.
2. **Every role is exercised.** `python3 eng/verify/role-journeys.py` → 12 logins green, each
   sidebar matching its grants. Then a full T1 run prints the role × module matrix with **all
   12 users present** and no blank row; deliberately skipping a thread makes the
   role-completeness interlock fail the run.
3. **Separation of duties actually holds.** Spot-check the four-eyes rules by hand against the
   running app: `ripon` enters a result then cannot verify it; `moinul` marks a study done then
   cannot open the report writer; `nasrin` can record vitals but is refused on note write;
   `shahid` is refused on `ipd.settle`; `/dashboard` 403s for all 11 non-`md` users.
4. **No regression in the existing apparatus.** On a fresh local DB
   (`docker exec hms-dev-db psql -U postgres -d postgres -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"`),
   start the app on `:5199`, then run each refactored script by its original name and confirm
   identical pass output to before. Then run `bash eng/verify/upgrade/run.sh` — it invokes 9 of
   them and is the real regression gate for the refactor.
5. **The suite runs and reports.** `python3 eng/verify/lifecycle-suite.py --env local --tier all`
   → green, with a case table whose IDs match the doc. Then `--tier t0` alone → green and
   provably writes nothing (diff `select count(*)` on `reg.patient`, `bill.invoice`,
   `bill.receipt`, `kernel.audit_event` before and after).
6. **The safety interlock actually blocks.**
   - `BASE_URL=https://hms.specshipper.com python3 eng/verify/lifecycle-suite.py --tier t1`
     with no `--env` → refuses (non-localhost fail-safe).
   - `--env prod --tier t2` → refuses unconditionally.
   - `--env prod --tier t1` without the confirmation flag → refuses.
   - `--env prod --tier t0` → runs, writes nothing.
7. **Environment awareness is real.** `BASE_URL=https://hms.specshipper.com` + `--tier t0` gets
   200s across the route list for all 12 roles, proving no script still hardcodes
   `localhost:5199`: `grep -rn 'localhost:5199' eng/verify/*.py eng/verify/*.sh` returns only
   the `_harness.py` default.
8. **A production T1 run is identifiable and reversed.** With explicit user go-ahead only: run
   it, then confirm the manifest file exists and lists every created id, that every created
   patient is findable by the `QA-` prefix in `/registration`, and that the day-close figure on
   `/billing/day-close` returns to its pre-run value after the reversal pass.
9. **The agent works end to end.** `/qa-lifecycle local` → the agent preflights, runs T0+T1,
   and returns the triaged table with the role matrix. `/qa-lifecycle prod` → it states what
   would be written and waits for confirmation rather than running.
10. **CI stays green.** `dotnet build -c Release` (warnings are errors) plus the 5 guard scripts
    plus the 4 test projects.
11. **Spec archive is clean.** The `Stop` hook reports no drift, and the `spec-auditor` agent
    reports 0028 compliant with its plan archived.

## Risks

- **The refactor touches 9 scripts the CI upgrade gate depends on.** Mitigated by keeping
  filenames and CLI identical and by running `eng/verify/upgrade/run.sh` locally as step 4
  above, before anything is pushed.
- **Driving every step as its real role will be slower and will fail more.** Threads that
  currently reuse one convenient session will need extra logins, and some steps may turn out to
  be impossible for the role that supposedly performs them — which is a genuine finding, not a
  harness bug. Per the agreed gap policy, such cases are recorded in the gap register for the
  follow-up spec rather than fixed in this pass.
- **A production T1 run permanently adds audit and ledger rows.** Accepted by the user's
  decision; mitigated by tagging, the manifest, the reversal pass, and the doc stating plainly
  what cannot be undone. The interlock exists so this can never happen by accident.
- **Production currently runs with `HMS_SEED=true` and the shared `Demo#1234` password live.**
  The suite depends on that demo cast. When the RUNBOOK §9 go-live switch is finally executed,
  production T1 stops working and the tier table must be revisited — record this in
  `notes.md` as an explicit follow-up.
- **The doc could become another hand-synced artefact that drifts.** That is exactly what
  `eng/check-lifecycle-traceability.sh` in CI prevents.
- **Case count.** ~130–160 cases is a large document. It stays useful only because coverage is
  a column, so it doubles as the gap register rather than being aspirational prose.
