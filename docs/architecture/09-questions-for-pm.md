# 09 — Questions for the PM

- **Status:** Open · **Date:** 2026-07-26 · **Spec:** `docs/specs/0003-mvp-architecture/`
- Genuine business decisions surfaced by the architecture work. Each has a **recommended default** so nothing blocks — silence = default applies at build time, revisitable until the sprint that consumes it.

| # | Question | Why it surfaced | Recommended default |
|---|---|---|---|
| P1 | **Fiscal-year convention** for number resets (ADR-0004): Bangladesh FY (July–June), calendar year, or per-hospital choice? | Invoice numbering & day-close reporting | Per-hospital config, **default July–June**; demo uses July–June |
| P2 | **Business-day boundary** for 24/7 day-close and "today" (ADR-0004, edge 16) | Night-shift attribution | Configurable; default **00:00 Asia/Dhaka**, sessions attributed to opening day |
| P3 | **UHID scope** when branches arrive (ADR-0007): hospital-wide or per-branch? | Patient identity across branches | **Hospital-wide UHID**; branch appears in visit numbers, not patient identity |
| P4 | **Approval delegation policy** (edge 19): who may delegate, max window, night-shift escalation chain and timeout? | Engine is built; policy is data | Supervisors delegate ≤ 14 days to same-or-higher role; escalation after **10 min** to next tier; MD is terminal tier |
| P5 | **Partial-refund rule for cancelled-after-collection tests** (edge 21): full refund, minus collection fee, or per-test policy? | Cancellation flow | Per-test-department policy table, default **full refund before processing starts, none after result entry**, always approval-gated |
| P6 | **Entitlement expiry behaviour** (ADR-0016): grace period then read-only, or hard stop? | Licensing enforcement | **30-day grace with banner → read-only** (never lock clinical data); commercial wording is sales' |
| P7 | **Demo dataset naming**: fictional "Altushi General Hospital" acceptable, or brand it to the prospect per meeting? | Seed kit build | Fictional default + settings screen re-brand live in the demo (it's a selling moment — identity applies everywhere instantly) |
| P8 | **Provisional-record billing**: during construction-config, may a `provisional` price be used on a (test) invoice, or is billing blocked until confirmed? | Edge 11 tighten-before-go-live | Allowed **pre-go-live only**; the go-live switch requires zero provisional prices |
| P9 | **SMS sender & consent posture**: masked sender ID string, and do report-ready SMS go to every patient with a phone by default? | I7 integration + §8 N5 privacy | Opt-out model (SMS on unless declined at registration), sender ID per hospital config |
| P10 | **Whole-taka display convention**: show `৳ 1,500` or `1,500/-` on prints? Bangla numerals anywhere? | Print layouts (§7 U15 vocabulary) | `৳ 1,500` on screen, `Tk 1,500/-` + words-in-English on money documents (matches market samples); Western numerals everywhere |
| P11 | **Demo date + team size**: S1–S7 assumes ~14 weeks with 2–3 engineers (08 §0). Confirm or give the real constraints so the cut list activates now, not in panic later | Build plan | — (needs an answer; plan holds as drafted) |
| P12 | **Counter hardware baseline** to certify: cheapest acceptable thermal printer models + scanner models we test against (§13 I2–I6) | Print/scan spikes (S1) | Vendor proposes a 2-printer + 1-scanner reference set after S1 spike; PM approves the "supported hardware" sales line |

## Phase-2 questions (2026-07-27 · spec `docs/specs/0014-phase2-review-and-plan/` · `11-build-plan-phase2.md`)

| # | Question | Why it surfaced | Recommended default |
|---|---|---|---|
| P13 | **Transfusion licence timeline** — is an application in flight? | M13 Blood Bank is sequenced last (Wave 6) because it cannot be validated without a licence; a real timeline would move it up | Keep Wave 6; revisit on licence news |
| P14 | **BEFTN + TDS source documents** for M17 — which partner bank, and can we obtain its BEFTN file spec and current NBR TDS rates from verifiable sources? | 5A-15 is `Must`; we will not fabricate a bank file format or tax rates | M17 payout export ships behind a config flag until the bank spec is in hand; accrual ledger builds regardless |
| P15 | **R3 public display content** — what may appear on a public queue screen: full patient name, masked name, or serial only? | Privacy posture of an unauthenticated surface (§8 N5) | Serial + doctor + masked name (`Rahim U.`) |
| P16 | **Go-live cutover owner and date** — who executes the seed-off/credential-rotation runbook, and is a target date set? | Demo seeding + shared password on the public URL is unacceptable once real patient data exists (review §4.4) | Runbook lands in Wave 0; vendor executes on PM's written go-live instruction |
| P17 | **Staff-pharmacy pricing policy** (5A-11 staff POS variant): flat % off MRP, cost-plus, or per-item list? Who approves over-limit staff discounts? | M11 ships staff sales as tagged sales; the price rule is business policy | Tagged sale + existing approval-gated discount until answered; policy becomes data when set |
| P18 | **Bed-day proration rule** (M6): the PRD demands "correct per-day charging from transfer time" (US6.3) but defines no counting rule. Confirm: one bed day per calendar date from admission date through the last admitted date (same-day discharge = 1 day); on a transfer day the **new** bed's rate applies from the first not-yet-posted date; posted charges are never reversed by a later transfer | Spec 0017 needs a deterministic, never-reversing rule for the money spine | The stated rule — it matches BD practice (admission day always charged), is idempotent, and honours "corrections are reversals" |
| P19 | **Reservation deposits** (M2 [S] "booking & reservation with advance receipt"): should a bed reservation take money before admission? If yes, what happens to the deposit when a reservation is cancelled? | 0017's folio (the advance ledger) is born at admission; a pre-admission deposit needs its own refund rule | Reservations hold no money (deposit taken at admission as the first advance) — no cancelled-reservation refund path needed |
| P20 | **Discharge with an outstanding due**: is an attributable reason enough, or must releasing a patient who still owes money require a supervisor approval? | Spec 0020 found a patient could leave the gate silently on an unpaid settlement invoice; the fix demands a stated reason (tier-2 audit) rather than blocking, so the discharge queue never stalls (§8 N1) | Reason + audit as built; R4's block stays the tool for a deliberate hold. Approval-gating is a policy row if you want it stricter |
| P21 | **Refund of a settled IPD bill**: should refunding the settlement invoice reopen the folio for correction, or is a reversal on the invoice enough? | Spec 0021 confirmed refunds work on settlement invoices; what they should do to the (locked) folio is a business call | A refund is a reversal on the invoice; a correction is a new posting under the existing post-lock approval |

| P22 | **Correcting a signed prescription** (M5): is supersede-only right, or may a doctor edit within some window (say, until the patient leaves the chamber)? | §10 calls a prescription immutable after the visit closes; spec 0024 implements supersede-only, which is safe but costs the doctor a step for a mistyped dose | Supersede-only as built — the original keeps its signature and its print, the correction names what it replaces. An edit window is a policy row if the PM wants one |

| P23 | **Surgical team fees** (M7): are surgeon/assistant/anaesthetist fees per *role* (one rate for the operation) or per *person*? | Spec 0025 prices per role from the catalogue and records who filled it; per-person rates would be a rate-plan scope question for M17 | Per role as built, with the named person recorded on the case. Per-person rates become a rate-plan row if the PM wants them |

| P25 | **Serial capacity and the waitlist** (M3): US3.1's **AC** says *"capacity limit enforced with waitlist option"*. `DoctorSchedule.MaxSerials` (default 40) is carried onto the queue board and **displayed**, and `IssueSerialAsync` never consults it — serial 41, 60 and 200 all issue. Nor does the handler check the doctor **has a session that day**: it takes `doctorId` on trust, with no lookup against `Schedules` and no FK, so the dropdown constrains the browser and nothing constrains the server (the same shape spec 0030 closed for `appointments.create`). Three decisions are needed: (a) at serial 41, refuse, or overbook with a warning, or offer a waitlist? (b) what **is** a waitlist here — a queue position with no serial number, auto-promoted when someone cancels, or a callback list the desk works by phone? (c) may a supervisor override the cap, and does an override need a reason? | Spec 0032's module sweep (M3-R1). This is an **[M] acceptance criterion enforced nowhere**, and the waitlist is product behaviour with its own states, SMS and screen — hard rule 2 puts that with the PM rather than inventing it in the build. Related: M3 was scoped "deliberately lite" under the §9A.2 freeze the PM lifted on 2026-07-27 and has not been revisited since, so §5 M3's postpone/transfer, cancel-with-reason, printable booking slip, calendar views, doctor-arrival SMS and no-show tracking are all still absent; M3 appears in no wave of `11-build-plan-phase2.md` | **Refuse past `MaxSerials`** with a plain-English message naming the doctor and the cap, with a supervisor override through the existing approval engine (reason required, tier-2 audit) — that much is enforcement of a rule the PRD already states and needs no new product surface. **Also validate the doctor has a schedule row for that weekday**, same treatment. The **waitlist stays unbuilt** until (b) is answered: a queue the operator cannot see the rules of is worse than no queue |

**Explicit disagreements/flags (ground rule 6):** none yet with PRD substance. One caution: §14's 150-operator design ceiling coexists with the 3 GB mandate only via the scale-up path in `06-deployment.md` §5 — sales material must not promise design-ceiling load on MVP hardware.

---

## P24 — Reopening a settlement: what the code actually does (2026-07-28, spec 0031)

**This entry was first raised on a wrong reading and is corrected here.** The original claim —
that reopening a confirmed settlement is ungated — is false. Probed against a running app:

| Folio state | Reopen | A late charge |
|---|---|---|
| `settlement_draft` (Prepared, no invoice yet) | **allowed** for `ipd.settle`, no approval | n/a |
| `locked` (Confirmed, invoice issued) | **impossible** — *"The folio is not in settlement draft."* | **refused at the handler** — *"This folio is locked (settled). Post-lock entries need a Billing Supervisor approval."* |

So a confirmed settlement cannot be reopened at all, and money after the lock is
approval-gated (`folio-late-post`). The control is at the money, which is where it belongs.
Reopening a **draft** — a bill assembled but not yet issued — is deliberately ungated, and
gating it would put a supervisor in the path of every corrected discharge.

**No product change is proposed.** What is wrong is `docs/qa/patient-lifecycle.md`'s LC-DIS-07,
which reads *"Settlement reopened, approval-gated"* and describes something the product does not
permit in the first place. The document has been corrected to describe the two states above.

### The one residual worth a decision — and it is small

`FolioService.ReopenDraftAsync` takes no `KernelDbContext` and writes **no audit event**, so
`settlement_draft → open` leaves no trace that a bill was assembled and then rescinded. The money
stays traceable either way: every charge line carries the identity of whoever posted it, and the
invoice is audited on confirm. What is not recoverable is *"this discharge bill was prepared,
withdrawn, and prepared again"* — the shape of a discount dispute at a busy counter.

**Done** (2026-07-28): `ipd.settlement.reopen` is written on every `settlement_draft → open`
transition, with the actor, the folio, and a before/after image — **tier 1**, matching
`ipd.folio.lock` immediately beside it. ADR-0011 puts money on tier 1, and a pair of transitions
that undo each other belongs in one tier or the timeline reads as if only half of it happened.
Proven by `SettlementReopenAuditTests` (2 tests, real Postgres) and end to end in
`money-and-controls.py` LC-DIS-07, which also proves the refused case writes nothing — a refused
action that logs looks exactly like one that happened.

### A tiering inconsistency this exposed, for the PM/architect

ADR-0011 §Decision assigns **tier 1 to money and clinical documents** and tier 2 to *masters and
config*. Parts of the code read it the other way round, as though tier 2 meant "more serious":

| Event | Tier in code | ADR-0011 would say |
|---|---|---|
| `ipd.folio.lock` (settlement issues an invoice) | 1 | 1 ✓ |
| `ipd.settlement.reopen` (new) | 1 | 1 ✓ |
| `ipd.discharge` **with an outstanding due** | 2 | 1 — it is money |
| `ipd.discharge` without a due | 1 | 1 ✓ |
| `role.grant` / `role.revoke`, `user.password.reset` | 2 | 1 — ADR-0011 names permission changes in the tier-1 list |
| `ipd.block` / `ipd.release` / `ipd.death` | 2 | arguably 1 (clinical) |

Nothing is *lost* — every one of these is written and queryable — but the tier is what decides
retention, partition compression and what an auditor filters on, so a wrong tier is a real cost
later. **Not changed here:** re-tiering existing events rewrites the meaning of history already
recorded, and the tier list is a living document in `03-data-model.md` that §3.2 makes the PM's
call. Worth one decision rather than six ad-hoc ones.

---

## HRM product-line questions (2026-07-31 · spec `docs/specs/0034-hrm-product-line/` · ADR-0025/0026/0027)

- **Status:** Open · **Date:** 2026-07-31 · **Spec:** 0034

A customer wants HR & Payroll without the hospital. Building M16 as a module that also ships as a
standalone product raises four business decisions. Defaults below were taken with the product owner
on 2026-07-31 so the build is unblocked; each is revisitable until the wave that consumes it.

| # | Question | Why it surfaced | Recommended default |
|---|---|---|---|
| P26 | **Bangladesh statutory payroll rules** — NBR salary tax slabs, Labour Act 2006 leave entitlements, gratuity, festival bonus, PF rules. Who owns sourcing them, and do we ship defaults? | None of these appear anywhere in the PRD or architecture docs. Rule 3 forbids asserting an unverified regulation; the build plan says unverifiable BD statutory rules go to the PM, not into code. Precedent is P14 (BEFTN/TDS: no fabricated formats or rates). | **Ship the engine configurable and empty** (ADR-0027): every rate, slab and entitlement is an effective-dated row the customer enters. No seeded statutory values. A verified BD default policy pack becomes its own spec once dated, authoritative sources are supplied and someone owns keeping them current. |
| P27 | **Is HRM a product line beyond hospitals?** The named customer is a hospital, but a standalone HRM is sellable to any 50–500-staff Bangladeshi employer. | CLAUDE.md rule 2 scopes the product to the 22-module hospital PRD. A general-business SKU is genuinely new scope and must be recorded, not absorbed. Competitor PiHR (`mypihr.com`) sells exactly this, cross-industry. | **Yes.** HR carries no clinical vocabulary from day one — org structure is customer-entered masters (`org_unit`, `designation`, `grade`, `location`), never wards or clinical departments. Costs nothing now; a rewrite later. The PRD stays a hospital document; the HRM SKU is recorded here and in spec 0034. |
| P28 | **Multi-branch / multi-location for the HRM SKU.** ADR-0007 fixed one hospital per install and `BranchId` is a compile-time constant (`HmsPageModel.BranchId = 1`). A 300-staff employer with three sites is an ordinary HRM customer. | Standalone HR hits the constant immediately; rosters, attendance devices and payroll approval all differ per site. `AppUser` has no branch column. | **Resolve branch per user in the HRM host; leave the ERP host on the constant.** HR tables carry `branch_id` per ADR-0007 regardless. This is an ADR-0007 amendment in scope for the HRM SKU only — cross-*customer* sharing remains forbidden. |
| P29 | **Per-module pricing and what an HRM-only licence includes.** Administration/Security is a dependency of every SKU — is it bundled or priced? And does mobile/GPS attendance belong in the roadmap? | ADR-0016 assumed module-wise selling without defining bundles. ADR-0026 now makes the boundary enforceable, so what a licence *contains* becomes a real commercial input. PiHR sells remote/GPS attendance and face recognition as headline features; we deferred both. | **Bundle Administration/Security into every SKU** (a product with no user management is not a product). Price HR as one module. **Mobile/GPS attendance and face recognition stay deferred** — they need a mobile app or PWA with camera and location APIs, a different platform investment from server-rendered Razor Pages. Revisit as its own spec if the market demands it. |
