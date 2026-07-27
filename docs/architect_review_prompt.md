# Handoff Prompt — Principal Software Architect: post-MVP review & next-phase plan

> **How to use this file:** paste everything below the line into a fresh agent session that has
> access to this repository. It is written to be self-contained and paste-ready. It supersedes
> nothing — `docs/architect_prompt.md` remains the record of the original design brief; this is
> the review handoff for what was actually built against it.

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

Two deliverables, in this order:

1. **An architectural review** of the delivered MVP: is it sound, is it safe to run a hospital
   on, and where is the debt that will hurt at Phase 2?
2. **A next-phase plan** — sequencing, risks and structural work — for the modules PRD §9A.3
   deliberately deferred, starting with the one the PRD itself names first.

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

## WHAT IS NOT BUILT — YOUR SCOPE

**14 modules deferred by PRD §9A.3** (PM decision; adding one is a PM call, not yours):
Front Desk (M2), EMR (M5), **IPD & folio (M6)**, OT (M7), Radiology/PACS (M10),
**Pharmacy (M11)**, Inventory (M12), Blood Bank (M13), Canteen (M14), Accounts (M15),
HR & Payroll (M16), Consultant Payments (M17), Corporate Billing (M18), Referral (M19).
Plus sub-modules **R2** (health/discount cards), **R3** (public queue display, patient
self-service status), **R4** (bill-block / due-control patient hold).

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
- **MVP scope is frozen** at §9A.2's 8 modules. Proposals to add one go to the PM as a question.
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
- Does the delivered system honour the ADRs, or has the implementation drifted? Name specific
  drift with file references.
- Is the money spine actually safe under concurrency? Read `BillingService`, the migrations'
  constraints and `ADR-0015`, then say whether you would run a hospital's cash on it.
- Does the folio seam (§9 binding rule) genuinely absorb M6 without migration pain? Prove it or
  refute it against `03-data-model.md` §4 and the `bill.charge_line` shape.
- Rank the debt above by what will hurt most at Phase 2, and say what you would fix before
  building anything new.

**B. Next-phase plan** (`docs/architecture/11-phase2-plan.md`, new)
- Sequencing for the deferred modules, starting from §9A.3's own "first module after MVP".
- For each: the structural work it forces on the existing spine, the migration risk, and what it
  breaks if sequenced wrongly.
- New ADRs for any decision this surfaces — one per decision, in `docs/architecture/01-adr/`.
- Anything you believe the PM must decide goes to `09-questions-for-pm.md`, not into your plan
  as an assumption.

**C. A spec** for whatever you propose to build first, per the spec-driven rule.

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
