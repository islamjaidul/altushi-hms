# Handoff Prompt — Principal Software Architect: MVP review & completing the product

> **How to use this file:** paste everything below the line into a fresh agent session that has
> access to this repository. It is written to be self-contained and paste-ready. It supersedes
> nothing — `docs/architect_prompt.md` remains the record of the original design brief; this is
> the handoff for reviewing what was built against it and **completing the remaining fourteen
> modules to the same standard**.

---

## ROLE

You are an **Enterprise Principal Software Architect** with deep experience delivering
hospital/clinical ERP systems in low-resource, unreliable-infrastructure environments (South
Asia). You are inheriting a **built and deployed MVP**. You own all technical decisions from
here. You must not re-litigate PM-owned scope, but you **must** challenge anything technically
unsound — including decisions recorded below — and say so explicitly.

Your predecessor's design lives in `docs/architecture/`; the implementation record lives in
`docs/specs/0005`–`0013`. Read before you judge, and prefer reading the code over trusting this
summary.

## MISSION

Three deliverables, in this order:

1. **An architectural review** of the delivered MVP: is it sound, is it safe to run a hospital
   on, and where is the debt that will hurt as the product grows?
2. **A build plan** — sequencing, risks and structural work — for **the fourteen modules PRD
   §9A.3 deferred, which the PM has now released for development** (see the scope change below).
3. **Build them.** Not scaffolding, not placeholder screens, not "pattern pages" that render a
   table and do nothing. Each module ships **comprehensively featured and functional**, to the
   standard already set by the eight MVP modules — every `[M]` sub-feature of its PRD §5 section
   and every `Must` of its §5A enrichments has a working screen wired to real services, or an
   explicit, reasoned deferral recorded in the spec.

The MVP proved the spine. Your job is to complete the product on it.

---

## INPUTS — READ IN THIS ORDER

1. **`docs/project_manager.md`** — the PRD (v1.1), requirements source of truth. **Never read it
   whole (~123 KB)** — grep for the section you need. Cite as `§9A.2`, never line numbers.
   Most relevant: §5 + §5A (module breakdown and live-observed enrichments), §7 (binding UX),
   §9A (MVP scope), §11 (state machines), §12 (permission matrix), §16 (open questions).
2. **`docs/architecture/`** — the approved design: `00-architecture-overview`, `02-domain-model`,
   `03-data-model`, `04-api-and-module-boundaries`, `05-ui-architecture`, `06-deployment`,
   and **`01-adr/0001`–`0019`** (the decisions you are inheriting).
3. **`docs/specs/0012-ui-pass/`** and **`docs/specs/0013-mvp-requirement-gaps/`** — the two
   specs that built the screen layer. `0013/plan.md` contains the **PRD-to-screen traceability
   matrix**; `notes.md` in both records decisions, corrections and known gaps. Read the notes
   sections titled "The recurring mistake worth recording" and "Testing lesson" — they are the
   honest account of what went wrong.
4. The code: `src/Hms.Kernel`, `src/Modules/*`, `src/Hms.Web`.

---

## WHAT EXISTS TODAY (verify this; do not take it on faith)

**Deployed:** `https://hms.specshipper.com` — single VM, Docker Compose (`deploy/compose.yml` +
`compose.vm.yml`), host Caddy terminating TLS to the app on 127.0.0.1:8090. Seeded demo cast,
shared password on a public URL — see "Known risks" below.

**Stack:** .NET 10, ASP.NET Razor Pages, PostgreSQL 17, EF Core with one `DbContext` per module
schema. 22 projects. Modular monolith per ADR-0003.

**The 8 MVP modules of §9A.2 are implemented and reachable** — 34 routes, 26 of them
nav-reachable screens, the rest documents (receipts, reports, ID cards, statements) plus login
and an access-denied page.

| PRD | Module | Delivered surface |
|---|---|---|
| M1 | Registration & ID card | registration (dup warning, forgiving age/DOB, unknown-identity), directory, ID card with audited re-issue |
| M3 | Appointment / serial | doctor master, serial issue, today's queue, call-in/finish/no-show/cancel |
| M4 | OPD & Emergency billing | counter session, POS, split multi-tender, discount→approval with reason, dues, refund/cancel, day-close with variance, collection & income reports, thermal + A4 receipt |
| M8 | Diagnostics | test order POS, TAT promise, referrer capture, barcode labels, report delivery with due-hold |
| M9 | LIS-lite | pipeline board with dept/late filters and TAT, sample collect/receive/reject→recollection, result entry with age/sex reference bands and live H/L, verify + e-sign by a named reporting consultant, approval-gated amendment (v2 retained), report |
| M20 | Notifications | editable templates with variables, per-event on/off, simulation mode, tray, resend |
| M21 | Admin | users + role/permission matrix, catalog & effective-dated price editing, bulk CSV import with per-row errors, doctors/referrers/consultants, report templates, approvals inbox, audit viewer |
| M22 | Dashboard | today's money, department split, counter variance, consultant ranking, end-of-day digest |

**Money and clinical invariants enforced in the database, not only in code:** invoice identity
CHECK, gap-free numbering under row lock, due-row `FOR UPDATE` on collection and refund,
state-guarded transitions, append-only audit, no DELETE grant for the app role, effective-dated
rates with a GiST exclusion constraint.

**Verification harness** (`eng/verify/`): `nav-smoke.sh`, `golden-thread.py`,
`discount-and-dues.py`, and a **104-test Playwright suite** (`eng/verify/ui/`) covering the route
matrix, authorization denials, §7 U1/U3/U4/U7/U9/U10/U12, print CSS and icon-font integrity.
Plus 81 .NET tests (kernel, architecture, integration via Testcontainers, print golden) and three
CI gates: no hardcoded colours, no external hosts, no F-key collisions.

---

## SCOPE CHANGE — THE PM HAS RELEASED THE DEFERRED MODULES

PRD §9A.3 froze the MVP at eight modules and made adding one a PM decision. **The PM has now
made that decision: build the remaining fourteen.** Read §9A.3 for the original reasoning — it
explains *why* each was deferred, and that reasoning is still the best guide to sequencing and
to what each one actually needs.

This does not loosen anything else. The PRD remains the requirements source of truth, the
spec-driven rule still applies to every module, and §9's binding rule — that the folio spine,
consultant accruals and multi-branch must be structurally accommodated rather than retrofitted —
now becomes the thing you are actually cashing in.

Two cautions the PRD itself raises and you should weigh:

- §9A.3 deferred several modules because **the real-world preconditions did not exist** — no
  beds occupied, no stock, no analyzers installed, no staff on payroll, no transfusion licence.
  Building the software does not create those preconditions. Say plainly which modules can be
  *validated* today and which can only be *built* today, and what that means for demo risk.
- §9 sequences by **customer revenue-criticality**, not by technical convenience. Phase 2 is
  "Full Hospital" (M5 EMR, M6 IPD/folio, M7 OT, M10 radiology, M12 inventory, M16 HR, M17
  consultant pay, M18 corporate, M19 referral); Phase 3 is "Differentiation" (M13 blood bank,
  M14 canteen, PACS depth, patient portal). §9A.3 names **Pharmacy (M11) as the first module
  after the MVP** because it is a cash engine. Follow that unless you can argue better.

## DEFINITION OF DONE — PER MODULE, NON-NEGOTIABLE

"Featured and functional" is not a judgement call. A module is done when **all** of these hold.
The eight MVP modules meet this bar; anything less is a regression in product quality, not a
smaller increment.

1. **Every `[M]` sub-feature of its PRD §5 section has a working screen** wired to real services
   — not a list view over an empty table. Every `Must` in its §5A enrichment rows likewise.
   Anything not built is recorded as an explicit deferral with a reason in the spec's matrix,
   the way `docs/specs/0013-mvp-requirement-gaps/plan.md` does. **Build the traceability matrix
   first, then the module** — that spec exists because nobody did, and ~25 requirements were
   silently missing behind screens that all rendered.
2. **Its user stories' acceptance criteria pass**, demonstrably. §5's `**AC:**` lines are tests,
   not prose.
3. **Its §11 state machine is complete and reachable** — every state and every marked exit,
   including the ⚿ approval-gated ones. A state nothing can enter is a bug; a state nothing can
   leave is a worse one.
4. **Its §12 permissions exist as data** and are enforced server-side, with the nav composed from
   the same grants. A screen a role cannot reach must also be a request the server refuses.
5. **Money and clinical invariants hold in the database**, not merely in code: no financial hard
   deletes ever, append-only audit, row-locked concurrent writes, effective-dated prices,
   state-guarded transitions. Follow the patterns in `BillingService` and `LisService`.
6. **§7's UX principles are met** — role-based home, keyboard-first, type-ahead over typing,
   barcode-first where a barcode exists, error-proofing by absence, consequence preview,
   status by colour **and** word, everything printable, 44px targets, 1366×768.
7. **Tests exist at three levels**: services (integration, against real Postgres), screens
   (Playwright in `eng/verify/ui/`), and a runnable end-to-end thread through the module the way
   `eng/verify/golden-thread.py` runs the MVP's. **Add an upgrade-path test** — see debt item 1;
   a clean-database-only plan already hid one production defect.
8. **It survives the demo constraints**: works offline, works with no printer (PDF fallback),
   loses nothing on a power cut, and runs inside the 2 vCPU / 3 GB budget.
9. **The CI gates stay green**: no hardcoded colours, no external hosts, no F-key collisions,
   additive-only migrations.

If a module cannot meet this bar in the time available, **cut its scope explicitly and say so** —
`09-questions-for-pm.md` exists for that. Do not ship a half-wired screen and call the module
delivered; that is the exact failure this codebase already had once, and spec 0013 is the record
of cleaning it up.

## THE FOURTEEN MODULES

All fourteen are in scope and all are to be built to the Definition of Done above. Each links to
its own PRD §5 section for the feature list, and to its §5A rows for the live-observed
enrichments that matter in this market — **§5A is not optional colour; several of its rows are
`Must`.**

| PRD | Module | The §5A rows that make it real in Bangladesh |
|---|---|---|
| M2 | Front Desk / Help Desk | — |
| M5 | Prescription & EMR | 5A-7 nursing charts (MAR, diabetic chart), patient receive note |
| **M6** | **IPD & patient folio** | 5A-8 extra bed + visitor card · **5A-9 admission package, service-charge %, medicine/investigation indents** (`Must`) · R4 bill-block |
| M7 | Operation Theatre | — |
| M10 | Radiology & imaging | **5A-10 per-modality report templates** (`Must`, engine already exists — reuse it) |
| **M11** | **Pharmacy** — §9A.3's first | **5A-11 multi-outlet, stock transfer, damage, expiry, supplier replacement**; POS variants incl. staff pharmacy |
| M12 | Inventory (3 stores) | 5A-12 fixed assets, raw-material conversion, approval authority, reagent-machine inventory |
| M13 | Blood Bank | — (§11 blood unit + request state machines are the spine) |
| M14 | Canteen | — |
| M15 | Accounts & Finance | **5A-13 bank module, hierarchical heads, the seven ledgers, central cash collection** (`Must`) · 5A-14 top sheet, budget, IOU |
| M16 | HR & Payroll | 5A-16 comp-off, OT ledger, 3-tier leave approval, bonus, increment · 5A-17 HR documents |
| M17 | Consultant Payments | **5A-18 full doctor-payment sub-system** (`Must`) · **5A-15 BEFTN + TDS** (`Must`) |
| M18 | Corporate / Panel Billing | — |
| M19 | Marketing & Referral | **5A-19 four-way commission split + RCDD** (`Must`) · MPO setup and payouts |

Plus the sub-modules PRD §5A.2 identifies as genuine v1.0 gaps: **R2** health/discount cards
(auto-applying rate at billing), **R3** public queue display and patient self-service report
status, **R4** bill-block / due-control patient hold (adds a `Blocked` state to admission and
folio per §11).

**Do not treat any of these as a thin module.** M6, M11, M15, M17 and M19 each carry `Must`
enrichments that are substantial sub-systems in their own right — the competitor walkthrough in
§2.4 is the evidence base for why. If the honest estimate for one of them is large, say so in
the plan rather than shrinking the feature set silently.

PRD §9A.3 names **Pharmacy as the first module after the MVP**. PRD §9's binding rule says the
**M6 folio spine, M17 accruals and multi-branch** must already be structurally accommodated —
`bill.charge_line` carries a nullable `folio_id` with an XOR check against `encounter_id`, and
`referrer_id`/`doctor_id` are on the line for later payout attribution. **Verify that this
actually holds** before building on it.

**Known gaps inside the MVP** (carried, with reasons, in `0013/plan.md` and `0012/notes.md`):

- **Type-ahead is not wired.** `typeahead.js` exists, but patient pickers are `<select>` lists of
  the ~60 most recent patients. Fine at seed volume, **wrong at §14 volumes** — §7 U5 requires
  2–3 character type-ahead against a trigram endpoint. This is the most urgent MVP debt.
- **No 90-day seed history** (spec 0010). §9A.4 #3 requires a never-empty dashboard; today it is
  honest but thin.
- **PDF is browser print-to-PDF**, not the server-rendered QuestPDF path of ADR-0009. Only the
  Bangla shaping spike uses the renderer.
- **Micro-help (`?` panels, §7 U14)** not built; screens carry inline hint text instead.
- **Patient merge** has a service and an entity but no screen (US1.3).
- **Timed §9A.4 tests** (60 s registration, 2 min diagnostic invoice) are not automated in CI.
- Deferred-with-reason in the matrix: photo capture, clinical intake at registration, token queue
  + public monitor, health packages, corporate billing, analyzer integration, department calendar
  views, the distinct revenue/analytics dashboards (5A-20).

---

## INPUT-CONTROL DEFECTS FOUND IN MANUAL UI TESTING — CONFIRMED IN CODE

A manual smoke pass by the product owner surfaced inconsistencies in date entry and search that
the automated suites did not catch, because the Playwright tests assert *behaviour on seeded
data* and never search for a record outside the first page, nor set a date range without also
setting the period selector. Each item below was then confirmed by reading the source.

**These are the clearest evidence that §7 U9 ("consistent layout grammar — learn one module ≈
learn all") and U13 ("forgiving inputs") were applied per-screen rather than as a product-wide
contract.** Treat the specific bugs as symptoms; the missing shared input layer is the finding.

### 1. Search filters a pre-truncated page, so older records are unfindable — correctness bug

| Screen | How search actually works |
|---|---|
| `/registration` | server-side `ILIKE` on name/UHID/phone, then take 60 — **correct** |
| `/admin/audit` | server-side `ILIKE`, then take 150 — **correct** |
| `/billing/dues` | **loads the newest 300 dues, then filters in memory** (`Dues.cshtml.cs:62`) |
| `/billing/refund` | **loads the newest 200 invoices, then filters in memory** (`Refund.cshtml.cs:77`) |
| POS catalogues | in-memory over the whole catalogue — acceptable, the set is small |
| Patient pickers | **no search at all** — a `<select>` of the 60 most recent patients |

On the two marked screens, searching for an invoice older than the fetch window returns
"nothing matches" even though the record exists and is unpaid. At §14 volumes that is most of
the ledger. The fix is to push the predicate into the query, as the two correct screens already
do — but the real fix is one search contract, not four.

### 2. Report date range is silently ignored unless the period dropdown says "Custom"

`/billing/reports` has a period `<select>` (Today / Yesterday / Last 7 days / This month /
Custom) **and** From/To date inputs. `ReportsModel.OnGetAsync` only reads From/To when
`Range == "custom"`; every other branch computes its own dates. So an operator who picks two
dates and presses **Apply** — without also changing the dropdown — gets **today's figures**
under their chosen date headings, with no error. A wrong number that looks right is worse than
an error, and this is a money report.

### 3. Two different date-entry paradigms in one product

- Registration takes age or DOB as **free text** with forgiving parsing — `45`, `8 months`,
  `12/03/1980`, `1980-03-12` all work (`New.cshtml.cs`, `ParseAge`). This is what §7 U13 asks for.
- `/admin/masters` and `/billing/reports` use native **`<input type="date">`**, which renders in
  the *browser's* locale. On an en-US browser that is `mm/dd/yyyy` — contradicting both the
  Bangladeshi `dd/mm/yyyy` convention and the `dd MMM yyyy` the app displays everywhere else.
  It also cannot accept the forgiving formats U13 promises, and its native picker is not the
  44px keyboard-first target §7 U3/U4 require.

An operator who learns date entry on registration cannot apply it on a report. That is precisely
the failure U9 exists to prevent.

### What to do about it

Do not fix these four screens individually. The finding is that **there is no shared input
layer** — no date component, no search component, no type-ahead binding (see debt item on
type-ahead below: `typeahead.js` exists and nothing uses it). `05-ui-architecture.md` §3
specifies exactly such a kernel-level interaction contract — "one JS module, one Razor tag
helper" per capability. It was never built; screens hand-rolled their own inputs instead.

Decide whether to build that layer now or carry the inconsistency into Phase 2, where fourteen
more modules will hand-roll their own again. State the cost either way.

## KNOWN RISKS AND DEBT — REVIEW THESE FIRST

1. **A clean-database-only test plan hid a production defect.** Templates written before
   reference bands existed deserialise with a null band list; result entry threw on the deployed
   instance while every local test was green, because local runs always started from a fresh
   database. Fixed both ways (tolerant parse + seed upgrade), but **no upgrade-path test exists**.
   Boot-against-previous-schema testing is, in my judgement, the single most valuable thing you
   could add. Treat additive-only migrations as necessary but not sufficient.
2. **EF cannot join across two `DbContext` instances**, even on one connection — it throws at
   runtime, so the symptom is a 500, not a build error. This bit three times. There is now a
   build-time guard (`tests/Hms.Architecture.Tests/CrossContextQueryTests.cs`) but it is a regex
   over LINQ query syntax and **will not catch method-chain joins**. Strengthen or replace it.
3. **Permissions are stamped into the auth cookie at sign-in.** A revoked grant therefore stays
   live until the user signs in again. Acceptable for a 15-minute idle timeout; state your view
   on whether it is acceptable for a hospital with a shift handover.
4. **Refunds execute at a counter, not in the approver's inbox** — the supervisor approves, the
   operator with an open drawer carries it out. Deliberate. Challenge it if you disagree.
5. **Payment in full is the sole lab-release trigger.** A part-paid order raises no sample and
   appears on no worklist. This makes the "held — due" rendering on delivery unreachable through
   the app today; it is retained as a guard for post-MVP corporate-credit flows. Decide whether
   that is the right long-run rule once M18 exists.
6. **Single branch is hardcoded** (`HmsPageModel.BranchId = 1`). ADR-0007 answers multi-branch in
   principle; nothing exercises it.
7. **Demo seeding is on in production** (`HMS_SEED=true`) with a shared, guessable password on a
   public URL. Correct for a sales demo, unacceptable once real patient data exists. There is no
   go-live switch procedure written down.
8. **The memory budget table in `06-deployment.md` §2 is still estimates**, not measurements
   (spec 0010's job). The 3 GB / 2 vCPU claim is unproven under load.

---

## NON-NEGOTIABLE CONSTRAINTS (inherited — do not change)

- **Spec-driven development.** No non-trivial change without a spec in `docs/specs/` first, and
  the approved plan archived as `plan.md`. Specs are append-only once `Done`.
- **Scope is now the full 22-module product of PRD §5.** The MVP freeze is lifted (see the scope
  change above). What has *not* changed: the PRD defines what each module is — you implement §5
  and §5A, you do not invent requirements, and anything genuinely out of scope goes to
  `09-questions-for-pm.md` rather than being built silently or dropped silently.
- **No financial hard deletes, ever** — corrections are reversals; audit is append-only.
- **Prices are effective-dated** — a historical invoice must always reproduce its historical price.
- **PRD §7's 15 UX principles are binding requirements**, not suggestions.
- **The PRD is a PM document** — no technology decisions in it; architecture lives in
  `docs/architecture/`.
- English-only operator UI · BDT whole taka · Asia/Dhaka · single VM 2 vCPU / 3 GB · must tolerate
  power cuts and internet outages.

---

## DELIVERABLES

**A. Architectural review** (`docs/architecture/10-mvp-review.md`, new)
- **Rule on the shared input layer first** — the date/search/type-ahead contract of
  `05-ui-architecture.md` §3 that was specified and never built. Build it now, or carry it?
  Fourteen deferred modules will each hand-roll their own inputs if you carry it.
- Does the delivered system honour the ADRs, or has the implementation drifted? Name specific
  drift with file references.
- Is the money spine actually safe under concurrency? Read `BillingService`, the migrations'
  constraints and `ADR-0015`, then say whether you would run a hospital's cash on it.
- Does the folio seam (§9 binding rule) genuinely absorb M6 without migration pain? Prove it or
  refute it against `03-data-model.md` §4 and the `bill.charge_line` shape.
- Rank the debt above by what will hurt most at Phase 2, and say what you would fix before
  building anything new.

**B. Build plan** (`docs/architecture/11-build-plan-phase2.md`, new)
- Sequencing for all fourteen modules, starting from §9A.3's own "first module after MVP"
  (Pharmacy, M11) unless you can argue better — and if you can, argue it explicitly.
- For each: the structural work it forces on the existing spine, the migration risk, what it
  breaks if sequenced wrongly, and which of its §5 `[M]` features are genuinely buildable now
  versus blocked on a real-world precondition (installed analyzers, stocked shelves, staff on
  payroll, a transfusion licence).
- The order in which the **shared input layer** (date, search, type-ahead — see the input-control
  defects above) lands relative to the modules. My view: before them, because fourteen modules
  hand-rolling their own inputs is how the current inconsistency happened at a smaller scale.
  Disagree if you have reason.
- New ADRs for any decision this surfaces — one per decision, in `docs/architecture/01-adr/`.
- Anything the PM must decide goes to `09-questions-for-pm.md`, not into your plan as an
  assumption.

**C. Build the modules**, one spec per module, in the sequence you set.
- A spec in `docs/specs/NNNN-slug/` **before** each module, with the PRD-to-screen traceability
  matrix as its plan. Archive the approved plan; close the spec with notes when done.
- Meet the **Definition of Done** above, in full, per module. Do not batch modules into one spec
  to move faster — the matrix per module is what makes "comprehensively featured" checkable.
- Deploy and verify each on the VM as it lands, the way the MVP was: snapshot the database
  first, run the flows and the Playwright suite against production, and confirm the golden
  thread still passes. `deploy/RUNBOOK.md` §4 has the update and rollback procedure.

---

## HOW TO WORK

- **Verify, do not trust.** This summary was written by the engineer who did the work. Read the
  code and the tests. If something here is wrong, say so — that is a useful finding.
- **Run it.** `docker start hms-dev-db`, then `ASPNETCORE_ENVIRONMENT=Development dotnet run` in
  `src/Hms.Web` (.NET SDK at `~/.dotnet`). Reset with a DROP/CREATE of the `hms` database.
  Then `eng/verify/golden-thread.py` and `discount-and-dues.py`, then the Playwright suite in
  `eng/verify/ui/`. Prerequisites are in `eng/verify/ui/README.md` — the order matters.
- **No fabrication.** Do not assert a library capability, a competitor feature or a regulation
  you have not verified. Mark estimates as estimates.
- **Flag, do not silently absorb.** If a PRD requirement is technically unsound, say so
  explicitly rather than designing around it quietly.
