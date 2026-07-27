# 0017 — Plan

## Approved: 2026-07-27

## 1. Traceability matrix (PRD → surface → proof)

| # | PRD requirement | MoSCoW | Surface / enforcement | Proof |
|---|---|---|---|---|
| 1 | M6 Admission (OPD/ER/direct, consultant, provisional dx) | M | `/ipd/admit` — typeahead patient, doctor, source, dx, ward/bed, package, advance-at-admission | thread step 1; Playwright |
| 2 | M6 Bed allocation/transfer/reserve/cancel + time-stamped history; §11 Bed machine | M | `/ipd/board` (reserve/clean/out-of-service), `/ipd/folio` transfer; `ipd.bed_stay` history; bed row lock + state-guarded UPDATE | concurrency test (two admissions, one bed); Playwright board |
| 3 | M6 Bed charge auto-calc by day/class + proration | M | `ipd.bed_day` (UNIQUE admission+date) catch-up posting; tariff via `RateResolver` on bed's catalog service; rule per spec (P18) | integration test: idempotent catch-up, transfer-day split |
| 4 | M6 Patient folio; §11 Folio machine incl. post-lock⚿ | M | `ipd.folio` + `BillingService.PostFolioChargeAsync` (folio-parented `charge_line`); `IpdPosting` guards state under FOR UPDATE | **first test**: folio line through the spine; late-post approval test |
| 5 | M6 Advances; balance = charges − advances − payments | M | folio-parented `bill.receipt` (XOR with invoice), taken at any open counter session; `/ipd/folio` advance form | settlement math test; day-close AC 11 |
| 6 | M6 Consultation/visit entry → M17 accrual | M | service posting with doctor select → `charge_line.doctor_id` | integration assert on doctor_id |
| 7 | M6 Oxygen/unit service consumption (US6.1) | M | `/ipd/folio` post-service: service typeahead + qty, price from rate plan, poster identity on line | Playwright as nurse |
| 8 | M6 Discharge: summary, settlement, gate pass (US6.2) | M | `/ipd/discharge/{id}`: initiate(summary) → clinically clear → settlement draft (service-charge % posts) → invoice+collect → gate pass print | thread steps; settlement integration test |
| 9 | M6 Certificates (discharge/death/birth), sequential no, reprint audit | M | `ipd.certificate` + kernel number series `cert-dis/cert-dth/cert-bir`; reprint bumps count + audit row | integration + Playwright |
| 10 | M6 Reports: admissions/discharges today, occupancy, census | M | `/ipd/reports` | Playwright |
| 11 | US6.4 MD live occupancy | M | `/ipd/board` readable by MD (`ipd.read`) | Playwright route matrix |
| 12 | 5A-9 Admission Package & Admission Fee masters; effective-dated prices | M | `ipd.admission_package` → catalog service + `RateVersion` (hard rule 5 kept); admission fee = seeded catalog service posted at admission | admission test |
| 13 | 5A-9 Service-charge % at settlement | M | pct snapshot on admission (package default, editable ⚿ n/a); posts one computed folio line at settlement draft, rounded half-up once | settlement math test |
| 14 | 5A-9 Medicine Indent → FEFO issue at MRP to folio (closes 0016 #4) | M | `ipd.indent(+item)` requested by nurse → `/pharmacy/indents` issue (composition-root `IndoorIssue` = StockService FEFO + folio lines per batch + `pharm.issue_allocation`) | integration FEFO/expired test; thread |
| 15 | Discharge-time medicine return restocks exact batches (closes 0016 #11) | M | `/ipd/folio` return: walks `issue_allocation`, restocks, negative folio line at same MRP | integration test |
| 16 | 5A-9 Investigation Indent → folio (§11 indoor branch, no Invoiced state) | M | `/ipd/folio` order tests: `diag.test_order` gains folio parent (XOR), charges folio-parented, LIS flow unchanged | integration + thread through LIS |
| 17 | 5A-8 Extra bed / visitor card fee | S | seeded catalog services ("Extra Bed (Attendant)", "Visitor Card Fee") via service posting | seed + Playwright smoke |
| 18 | R4 Blocked⚿ ⇄ Released⚿; block list; service+discharge bar | S | admission+folio `blocked` set/cleared together (approval-gated); guards in `IpdPosting`, indents, discharge, **and OPD billing screen**; `/ipd/admissions` block tab | blocked-refusal tests; thread block/release |
| 19 | ICU/CCU/HDU/NICU acuity bundles | S | **Deferred** — classes+tariffs ship; bundle composition needs clinical input (spec §out-of-scope) | — |
| 20 | Duty assignment views | S | **Deferred to M16** (no staff registry) | — |
| 21 | Pre-admission quotation | C | **Deferred** (presentation-only over rate plan) | — |

## 2. Technical approach

**Bill-spine seam (the only mutation of MVP tables — everything else is a new `ipd` schema):**

- `bill.invoice`: `encounter_id` → nullable, add `folio_id` (nullable) +
  `ck_invoice_parent num_nonnulls(encounter_id, folio_id) = 1`.
- `bill.receipt`: `invoice_id` → nullable, add `folio_id` (nullable) +
  `ck_receipt_parent num_nonnulls(invoice_id, folio_id) = 1`. A folio-parented receipt **is**
  an advance (positive) or advance-refund (negative) — it rides counter sessions, so tender
  totals and expected cash stay honest with no day-close special-casing.
- `bill.day_close_summary`: add `advances_taken` (bigint default 0). `DayCloseService`
  computes `dueCollected` over invoice-parented receipts only; advances get their own figure.
- `BillingService` additions (same file, same idioms):
  `PostFolioChargeAsync` (folio-parented line; negative qty allowed only via the return path),
  `CollectAdvanceAsync` (folio receipt, session-locked, no due row),
  `RefundAdvanceExcessAsync` (negative folio receipt, approval-free at settlement — it is the
  documented change-giving of settlement, audited),
  `CreateFolioInvoiceAsync` (freezes folio's unbilled lines; `due.balance = net − advancesApplied`;
  advances applied recorded on the folio row + audit).
- `diag.test_order`: `encounter_id` → nullable, add `folio_id` + XOR check; order-creation
  service takes either parent; indoor orders skip `Invoiced`.

**New module `src/Modules/Ipd/Hms.Ipd` (schema `ipd`), entities:**
`Ward` (name, class) · `Bed` (ward, code, class, tariff catalog id, state; §11) ·
`Admission` (no. `ADM-{fy}-{n:D5}`, patient, doctor, source, dx, package?, svc-charge %,
state §11, blocked flag, summary, timestamps) · `BedStay` (admission, bed, from/to) ·
`BedDay` (admission, date, bed, charge line id; UNIQUE(admission,date)) ·
`Folio` (admission 1:1, state §11, advance_applied, settlement invoice id) ·
`AdmissionPackage` (name, catalog service id, default svc-charge %) ·
`Indent` + `IndentItem` (kind medicine|investigation, state requested→issued/cancelled) ·
`Certificate` (admission, kind, no., body jsonb, print_count).

**Services:** `IpdService` (admission/bed machines under row locks), `FolioService`
(state-guarded posting gate, bed-day catch-up, settlement draft + service-charge line,
settlement, late-post approval verification), certificates. Composition-root pieces (modules
never reference each other, ADR-0003): `IndoorIssue` (pharm × ipd × bill), `IndoorOrder`
(diag × ipd × bill), OPD block guard (reads `ipd` blocked set by patient).

**Screens** (all §7-compliant: 44px targets, typeahead, `hms-date`, one primary action):
`/ipd/board` · `/ipd/admit` · `/ipd/admissions` (census + block tab) · `/ipd/folio/{id}` ·
`/ipd/discharge/{id}` · `/ipd/certificates` · `/ipd/reports` · `/pharmacy/indents`.

**Permissions as data:** `ipd.read` (front desk, billing, MD, nurse), `ipd.manage`
(front desk: admit/beds/transfer/block-raise), `ipd.service.post` (nurse; + indent request),
`ipd.settle` (billing operator: advances, settlement, certificates). Pharmacist issues
indents under existing `pharmacy.stock.manage`. Approval policies seeded:
`folio-late-post` (T1 Billing Supervisor), `patient-block` / `patient-release`
(T1 Billing Supervisor, T2 MD). New cast member: `nasrin` (Nurse).

**Seed:** wards (General ×2, Cabin, ICU), 12 beds with class tariff services + rates,
admission-fee/extra-bed/visitor-card/service-charge services, one package
("Normal Delivery Package") with rate, one open demo admission with posted days.

**Bed-day rule (P18 default):** one bed day per calendar date, admission date through last
admitted date; the date's charge goes to the bed occupied at posting time; transfers
re-point charging from the first unposted date. Catch-up runs on folio view (posting
permission holders), transfer, and settlement — idempotent by the UNIQUE constraint, so no
scheduler exists to fail during power cuts (PRD §8 N2).

## 3. Verification

1. Integration: folio-line seam proof; concurrent bed claim; idempotent bed days + transfer;
   settlement math (advances under/over net, service-charge rounding); post-lock approval;
   blocked refusals; indoor FEFO issue/expired-invisible/return-restock; certificate series.
2. Playwright `spec-0017.spec.ts`: role matrix additions (nasrin), board/admit/folio/
   discharge/certificates flows, blocked-state UI.
3. `eng/verify/ipd-thread.py` (dirty-DB tolerant): admit→beds→services→indents→advance→
   block/release→LIS-flow→discharge-settle→certificates→day-close figures.
4. Upgrade gate over the pre-IPD dump — proves the bill/diag migrations on old data; then
   full suite + CI greps.

## 4. Risks

Bill-table migration (mitigated: XOR mirrors proven pattern; upgrade gate); settlement money
math (mitigated: integration tests before screens); LIS board queries assuming encounter
parent (checked and adjusted with in-memory joins per ADR-0003 rule).
