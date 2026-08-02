# HMS-ERP — Schema Design Audit

**Scope:** the 15 schemas of the ERP + HRM monolith, audited from EF model configuration and
migration files (authoritative per the handoff brief). **No database was queried** — the user
declined DB access, so every statement below is derived from source, and anything I could not
confirm from source is marked as such.

**Date:** 2026-08-03 · **Basis:** commit `54a2fec` (main, clean)

**Relationship to existing work:** spec `0039-lifecycle-hardening` (Draft, dated today) already
names three of these themes at requirement level. This audit is the column-by-column evidence
underneath it, plus the indexing and concurrency dimensions 0039 does not cover.

---

## (a) Executive summary — the structural themes

### Theme 1 — The schema constrains *shape*, never *value*

All 12 ERP `HasCheckConstraint` declarations are structural: six `num_nonnulls(a,b) = 1`
polymorphic-parent guards, one arithmetic identity, one state-set check, one window ordering,
one identity-presence check, one `qty_on_hand >= 0`. **Not one bounds a money, quantity,
percentage or clinical value to its domain.** There are also **zero `[Range]` attributes in the
entire `src/` tree** (verified by grep — no hits), so there is no model-level tier either.

Every confirmed defect in the brief is the same failure at a different column:

| Defect | The missing constraint |
|---|---|
| qty 999999 → 299,999,700 Tk folio charge | `bill.charge_line.qty BETWEEN 1 AND <cap>` |
| SpO2 = 999, BP −1/−9 | `emr.vitals.*` clinical ranges |
| `valid_from = infinity` froze a price | `adm.rate_version.valid_from < 'infinity'` |
| `appt.appointment.state = 'deleted'` (12 rows) | `state IN (...)` |
| a service with 0 rate versions | a presence rule (needs a trigger, not a CHECK) |

These are not five bugs. They are one absent design decision, observed five times.

### Theme 2 — Correctness lives in C#, and only the C# path is safe

The service-layer discipline is genuinely good (ADR-0015: `FOR UPDATE` + state-guarded CAS +
unique indexes; 37 correct compare-and-swap sites). But it is a **convention, not an invariant**.
Any second write path — a data import, `DevSeed`, a new Razor page, a fixup script, a future
integration — bypasses all of it, because the database will accept anything the C# happened to
be checking. `Math.Clamp(Commission, 0, 100)` at `Pages/Admin/People.cshtml.cs:99` is the whole
enforcement of a percentage domain; `if (Price < 0)` at `Pages/Admin/Masters.cshtml.cs:104` is
the whole enforcement of a price domain.

### Theme 3 — The one schema-level enforcement artefact protects a role nobody uses

`InitBill.cs:289-290` revokes DELETE on `bill.*` and UPDATE/DELETE on `kernel.audit_event` —
**from `hms_app`**. The deployed application connects as **`hms_migrator`**, the owner of every
migrated object (`deploy/compose.yml:35`, with a comment at `:33-34` acknowledging it). At
runtime the app therefore holds full DELETE on every financial and audit table. Hard rule 4
("no financial hard deletes, ever") is convention-only in the running system.

### Theme 4 — Indexes were chosen per module by intuition, and the distribution is inverse to traffic

| Module | `HasIndex` count | Hot-path status |
|---|---|---|
| hr | **57** | batch/monthly |
| emr | 14 | per consultation |
| ipd | 11 | per admission |
| pharm / ot / kernel | 7 | mixed |
| bill | 6 | **every counter action** |
| radiology | 5 | low |
| reg / adm | 3 | high (search / pricing) |
| lis | 2 | per sample |
| notif / appt | 1 | **appt is every morning's queue** |
| **diag** | **0** | **every lab and imaging order** |

`bill.charge_line`, `bill.encounter`, `bill.invoice_line` and the **entire `diag` schema** carry
no index beyond their primary keys (verified: zero `CreateIndex` calls in
`Modules/Diagnostics/.../Migrations/`, and `bill` migrations create indexes only on
`day_close_summary`, `invoice`, `receipt`). `bill.charge_line` carries the single hottest
predicate in the product.

### Theme 5 — HR is the reference implementation of the discipline the ERP lacks

Same team, same repo, six days later (`InitHr`, 2026-07-31): 8 GiST exclusion constraints,
8 `effective_to >= effective_from` CHECKs, a payroll money identity, `payable_fraction_bp
BETWEEN 0 AND 10000`, a leave-balance non-negativity CHECK, and a real `xmin` row-version token.
**The ERP has one exclusion constraint, one trigger, one inert token.** This is not a knowledge
gap on the team — it is a gap in *when* the standard was set. The remedy is to backport HR's
schema posture to the ERP, not to invent one.

### Theme 5b (structural, and not in spec 0039) — there is no doctor master table

`doctor_id` appears on `bill.encounter`, `bill.charge_line`, `bill.invoice_line`,
`diag.test_order.ordering_doctor_id`, `ipd.admission.doctor_id`, `emr.note.doctor_id`,
`emr.template.doctor_id`, `emr.favourite.doctor_id`, `appt.appointment.doctor_id`. **No table
exists for it to reference.** The de-facto master is `appt.doctor_schedule.doctor_id` — a column
with **no index at all** (`ApptDbContext.cs:52`) — and identities are minted by
`MAX(DoctorId) + 1` at `Pages/Admin/People.cshtml.cs:69`, a read-then-write with no unique
constraint. Two concurrent doctor creations produce two doctors sharing one id, retroactively
merging their consultations, orders and payout attribution.

---

## (b) Findings by dimension

### 1. Value-domain constraints

**What exists:** exactly one value-domain CHECK in the ERP — `ck_batch_qty` (`qty_on_hand >= 0`)
at `Modules/Pharmacy/Hms.Pharmacy/Data/PharmDbContext.cs:302`. Everything below is absent.

#### 1a. Money

| Column | Missing constraint | Sev | Evidence |
|---|---|---|---|
| `bill.charge_line.unit_price`, `.amount` | `>= 0`; `amount = qty * unit_price` | **High** | `BillDbContext.cs:70,72` |
| `bill.invoice_line.unit_price`, `.amount` | `>= 0`; `amount = qty * unit_price` | **High** | `BillDbContext.cs:147-148` |
| `bill.invoice.gross`, `.discount`, `.net`, `.tax` | `>= 0` each, and `discount <= gross`. The identity CHECK is satisfied by `gross=-1000` and by a discount exceeding the bill (net goes negative and the identity still holds) | **High** | `BillDbContext.cs:119-124`; CHECK at `:222-223` |
| `adm.rate_version.price` | `>= 0` (app-only guard) | **High** | `AdmDbContext.cs:41`; guard `Pages/Admin/Masters.cshtml.cs:104,184` |
| `bill.due.balance` | `>= 0` | Medium | `BillDbContext.cs:174` |
| `ipd.folio.advance_applied` | `>= 0` | Medium | `IpdDbContext.cs:116` |
| `pharm.batch.cost`, `.mrp` | `>= 0` | Medium | `PharmDbContext.cs:132-133` |
| `pharm.purchase_order_line.expected_cost` | `>= 0` | Medium | `PharmDbContext.cs:120` |
| `pharm.sale_allocation.unit_mrp`, `.unit_cost` | `>= 0` | Medium | `PharmDbContext.cs:169-170` |
| `pharm.supplier_ledger.amount` | none needed — deliberately signed | — | `PharmDbContext.cs:237` |
| `bill.receipt.amount` | none needed — negative = refund by design | — | `BillDbContext.cs:162` |
| `bill.counter_session.opening_float`, `.counted_cash`, `.expected_cash` | `>= 0` | Low | `BillDbContext.cs:48,51-52` |
| `ot.case_team.amount_posted`, `ot.case_consumable.unit_price` | `>= 0` | Low | `OtDbContext.cs:83,95` |

#### 1b. Quantity

| Column | Missing constraint | Sev | Evidence |
|---|---|---|---|
| **`bill.charge_line.qty`** | **`> 0` and a sane upper bound** — this is the 999999 defect. `int`, unbounded, no CHECK; the only guard is `Qty <= 0` in one page (`Pages/Ipd/Folio.cshtml.cs:239`) | **High** | `BillDbContext.cs:69` |
| `bill.invoice_line.qty` | `> 0` (frozen copy of the above) | **High** | `BillDbContext.cs:146` |
| `pharm.sale_allocation.qty`, `.refunded_qty` | `qty > 0`, `refunded_qty BETWEEN 0 AND qty` | **High** | `PharmDbContext.cs:168,171` |
| `pharm.issue_allocation.qty`, `.returned_qty` | `returned_qty BETWEEN 0 AND qty` | **High** | `PharmDbContext.cs:186,188` |
| `ipd.indent_item.qty_requested/_issued/_returned` | `qty_issued <= qty_requested`, `qty_returned <= qty_issued`, all `>= 0` | **High** | `IpdDbContext.cs:158-161` |
| `pharm.purchase_order_line.qty`, `.received_qty` | `received_qty BETWEEN 0 AND qty` — the over-receipt TOCTOU at `PurchaseService.cs:85-92` has no backstop | **High** | `PharmDbContext.cs:118-119` |
| `pharm.transfer_line.requested_qty`, `.sent_qty` | `sent_qty BETWEEN 0 AND requested_qty` | Medium | `PharmDbContext.cs:210-211` |
| `pharm.stock_move.qty` | signed by design; `<> 0` except for the `quarantine` marker kind | Low | `PharmDbContext.cs:150` |
| `ot.case_consumable.qty` | `> 0` | Medium | `OtDbContext.cs:93` |
| `radiology.study.film_count` | `>= 0` | Low | `RadiologyDbContext.cs:57` |
| `appt.doctor_schedule.max_serials` | `> 0` | Low | `ApptDbContext.cs:17` |

#### 1c. Percentage

**No percentage column in the ERP is bounded at the database.** Contrast HR:
`ck_attendance_day_fraction CHECK (payable_fraction_bp BETWEEN 0 AND 10000)`
(`InitHr.cs:1378-1379`).

| Column | Missing constraint | Sev | Evidence |
|---|---|---|---|
| `ipd.admission.service_charge_pct` | `BETWEEN 0 AND 100`. Bound straight from a form (`Pages/Ipd/Admit.cshtml.cs:31,89`) with **no clamp at all**, then multiplied into money at `IpdBilling.cs:226` | **High** | `IpdDbContext.cs:65` |
| `ipd.admission_package.default_service_charge_pct` | `BETWEEN 0 AND 100` | Medium | `IpdDbContext.cs:128` |
| `adm.referrer.commission_percent` | `BETWEEN 0 AND 100`. App-clamped in exactly one place — the only `Math.Clamp` in `src/` | Medium | `AdmDbContext.cs:63`; clamp `Pages/Admin/People.cshtml.cs:99` |

#### 1d. Clinical measurement

All are `short?` (smallint, range −32768…32767) with no CHECK, no `[Range]`, and no HTML
`min`/`max` on the inputs (`Pages/Emr/Vitals.cshtml:31,35,39,51` are bare
`inputmode="numeric"`). This is the SpO2 = 999 / BP −1/−9 defect, precisely.

| Column | Suggested CHECK | Sev | Evidence |
|---|---|---|---|
| `emr.vitals.spo2` | `BETWEEN 50 AND 100` | **High** | `EmrDbContext.cs:89` |
| `emr.vitals.pulse` | `BETWEEN 20 AND 300` | **High** | `EmrDbContext.cs:86` |
| `emr.vitals.systolic` | `BETWEEN 40 AND 300` | **High** | `EmrDbContext.cs:84` |
| `emr.vitals.diastolic` | `BETWEEN 20 AND 200`, and `diastolic < systolic` | **High** | `EmrDbContext.cs:85` |
| `emr.vitals.temperature_tenths_c` | `BETWEEN 250 AND 450` (25.0–45.0 °C) | **High** | `EmrDbContext.cs:87` |
| `emr.vitals.weight_tenths_kg` | `BETWEEN 1 AND 4000` | Medium | `EmrDbContext.cs:88` |
| `emr.glucose_reading.glucose_tenths` | `BETWEEN 5 AND 500` (0.5–50.0 mmol/L) | **High** | `EmrDbContext.cs:145` |
| `emr.glucose_reading.insulin_units` | `BETWEEN 0 AND 200` | Medium | `EmrDbContext.cs:148` |
| `reg.patient.age_years` | `BETWEEN 0 AND 130` | Medium | `RegDbContext.cs:15` |
| `reg.patient.age_months` | `BETWEEN 0 AND 11` (it is the months *remainder* per spec 0032) | Medium | `RegDbContext.cs:16` |
| `reg.patient.sex` | `IN ('M','F','O')`. Non-nullable `char(1)`; C# default is `'\0'` | Medium | `RegDbContext.cs:13,73` |

#### 1e. Dates

| Column | Missing constraint | Sev | Evidence |
|---|---|---|---|
| **`adm.rate_version.valid_from`** | an upper bound. **This is the `infinity` defect, and I traced its exact route:** `FlexibleDate.TryParse` accepts `31/12/9999` → `DateOnly.MaxValue` (`Hms.Kernel/Time/FlexibleDate.cs:18-27`); `OnPostRepriceAsync` rejects only *past* dates (`Pages/Admin/Masters.cshtml.cs:197`), never a far-future one; the row is inserted at `:224-228` **and the previous open version's `valid_to` is set to the same value at `:221`**, so the old price never ends and the new one never starts — the price is permanently frozen, exactly as reported. *Caveat: the `DateOnly.MaxValue → 'infinity'` mapping is Npgsql's documented default and nothing in this repo disables it (no `DisableDateTimeInfinityConversions` switch anywhere in `src/`), but I could not verify it against a running database.* | **High** | above |
| `reg.patient.dob` | `<= current_date` and `> '1880-01-01'` | Medium | `RegDbContext.cs:14` |
| `pharm.batch.expiry` | a sanity window (`> '2000-01-01'`) | Low | `PharmDbContext.cs:130` |

#### 1f. Presence rules that a CHECK cannot express

| Rule | Sev | Note |
|---|---|---|
| **A non-provisional `adm.service` / `adm.test_catalog` row must have ≥ 1 rate version.** Origin is explicit: `Pages/Admin/Masters.cshtml.cs:146` — `if (Price > 0)` — so creating a catalogue item with price 0 inserts **zero** rate versions and the item is silently unsellable. This is the confirmed "service got 0 rate versions" defect. | **High** | Needs a deferred constraint trigger, or a `NOT VALID` CHECK on `provisional`, or an app invariant + a nightly probe |
| Text length: **0 `HasMaxLength` in the product**, so a 100 KB paste into a patient name is a valid `text` write (spec 0039 §Problem records the lockout this caused). | **High** | Add `HasMaxLength` per column; `text` + CHECK `length(col) <= n` is equivalent in PG |

---

### 2. Nullability discipline

Nullability is derived cleanly from C# nullable annotations and is **correct in the simple
cases** — verified against `InitBill.cs:25-219`: every money column is `nullable: false`, every
state column is `nullable: false`, `branch_id`/`patient_id` are `nullable: false`. That part is
sound and needs no work.

The problem is a category the model cannot express: **state-dependent nullability**. Columns are
nullable because they are empty *early* in a lifecycle, and nothing requires them to be filled
once the state that implies them is reached.

| Column(s) | Should be NOT NULL when… | Sev | Evidence |
|---|---|---|---|
| `ipd.admission.discharged_at` | `state IN ('discharged','death','absconded')` | **High** | `IpdDbContext.cs:72` |
| `bill.counter_session.closed_at`, `.counted_cash`, `.expected_cash`, `.variance` | `state = 'closed'` — a closed session with a null cash count is a hole in the day's money trail | **High** | `BillDbContext.cs:50-53` |
| `lis.result.verified_by`, `.verified_at`, `.verifier_role`, `.esign_hash` | the result is released | **High** | `LisDbContext.cs:61-64` |
| `lis.sample.collected_at`, `.collected_by` | `state <> 'pending_collection'` | Medium | `LisDbContext.cs:27-28` |
| `lis.sample.received_at`, `.received_by` | `state IN ('received',…)` | Medium | `LisDbContext.cs:29-30` |
| `lis.sample.rejected_reason` | `state = 'rejected'` | Medium | `LisDbContext.cs:31` |
| `ipd.indent.issued_by`, `.issued_at` | `state = 'issued'` | Medium | `IpdDbContext.cs:150-151` |
| `emr.note.finalised_at`, `.finalised_by` | `state IN ('final','superseded')` | Medium | `EmrDbContext.cs:51-52` |
| `emr.mar_dose.administered_at`, `.administered_by` | `state = 'given'` | Medium | `EmrDbContext.cs:133-134` |
| `emr.mar_dose.state_reason` | `state IN ('missed','refused')` | Low | `EmrDbContext.cs:132` |
| `ot.ot_case.started_at` / `.finished_at` | `state = 'in_theatre'` / `'completed'` | Medium | `OtDbContext.cs:60-61` |
| `ot.ot_case.cancel_reason` | `state = 'cancelled'` | Low | `OtDbContext.cs:62` |
| `radiology.study.performed_at`, `.performed_by` | `state IN ('done','reported')` | Medium | `RadiologyDbContext.cs:54-55` |
| `pharm.batch.state_reason` | `state IN ('quarantined','disposed')` | Low | `PharmDbContext.cs:135` |
| `pharm.transfer.sent_at` / `.received_at`, `.received_by` | `state = 'sent'` / `'received'` | Medium | `PharmDbContext.cs:200-202` |
| `pharm.stock_audit.posted_at` | `state = 'posted'` | Low | `PharmDbContext.cs:254` |
| `ipd.folio.settlement_invoice_id` | `state = 'locked'` | Medium | `IpdDbContext.cs:117` |
| `ipd.bed.state_reason` | `state = 'out_of_service'` | Low | `IpdDbContext.cs:38` |
| `notif.sms.fail_reason` | `state = 'failed'` | Low | `NotifDbContext.cs:37` |
| `bill.encounter.doctor_id` | `type = 'OPD'` (an OPD visit without a consultant breaks payout attribution) | Medium | `BillDbContext.cs:16` |

All of these are expressible as one CHECK per table, e.g.
`CHECK (state <> 'closed' OR (closed_at IS NOT NULL AND counted_cash IS NOT NULL))`.

**Nullable but semantically required regardless of state:**

| Column | Why | Sev | Evidence |
|---|---|---|---|
| `bill.charge_line.rate_version_id`, `bill.invoice_line.rate_version_id` | Named in the code as "proof of resolution" — the whole basis of hard rule 5 (a historical invoice reproduces its historical price). A NULL here is a line whose price provenance is gone forever, and nothing prevents it | **High** | `BillDbContext.cs:71,149` |
| `ipd.admission.doctor_id` | the admitting consultant | Medium | `IpdDbContext.cs:60` |

**Over-strict (NOT NULL where a sentinel is now doing the work):** `ipd.bed.tariff_service_id`,
`ot.ot_case.operation_service_id`, `ot.case_team.person_id`, `bill.charge_line.catalog_id`,
`radiology.study.modality_id` are all non-nullable `bigint` with **no FK**, so `0` is a valid
value that means "unset" and reads as a real id. Severity **Medium** — this is the mechanism by
which the confirmed orphan classes become silent rather than loud.

---

### 3. State / enum modelling

Every state column is `text`. **Exactly 1 of 22 is constrained.**

| # | Column | CHECK? | Legal-value source | Sev if unconstrained |
|---|---|---|---|---|
| 1 | `kernel.approval_request.state` | ✅ **`ck_approval_state`** | `KernelDbContext.cs:47-48` | — |
| 2 | `appt.appointment.state` | ❌ | `AppointmentState` (`ApptDbContext.cs:20-28`) | **High** — the confirmed `deleted` defect |
| 3 | `bill.invoice.state` | ❌ | `InvoiceState` (`BillDbContext.cs:99-107`) | **High** |
| 4 | `bill.counter_session.state` | ❌ | `SessionState` (`BillDbContext.cs:31-38`) | **High** — the partial unique index `one_open_session_per_counter` (`InitBill.cs:262-263`) *depends* on these literals; a typo'd state silently escapes the one-open-session rule |
| 5 | `bill.encounter.state` | ❌ | literal `"open"` — **no constants class at all** | **High** |
| 6 | `diag.test_order.state` | ❌ | `TestOrderState` (`DiagDbContext.cs:8-16`) | **High** |
| 7 | `diag.order_test.state` | ❌ | literal `"ordered"` — **no constants class** | **High** |
| 8 | `ipd.admission.state` | ❌ | `AdmissionState` (`IpdDbContext.cs:41-52`) | **High** |
| 9 | `ipd.folio.state` | ❌ | `FolioState` (`IpdDbContext.cs:100-106`) | **High** |
| 10 | `ipd.bed.state` | ❌ | `BedState` (`IpdDbContext.cs:10-17`) | **High** |
| 11 | `ipd.indent.state` | ❌ | `IndentState` (`IpdDbContext.cs:132-137`) | Medium |
| 12 | `lis.sample.state` | ❌ | `SampleState` (`LisDbContext.cs:8-18`) | **High** |
| 13 | `emr.note.state` | ❌ | `NoteState` (`EmrDbContext.cs:14-19`) | **High** |
| 14 | `emr.mar_dose.state` | ❌ | `DoseState` (`EmrDbContext.cs:21-27`) | Medium |
| 15 | `ot.ot_case.state` | ❌ | `CaseState` (`OtDbContext.cs:11-19`) | **High** |
| 16 | `radiology.study.state` | ❌ | `StudyState` (`RadiologyDbContext.cs:11-19`) | Medium |
| 17 | `pharm.batch.state` | ❌ | `BatchState` (`PharmDbContext.cs:21-27`) | **High** |
| 18 | `pharm.purchase_order.state` | ❌ | `PoState` (`PharmDbContext.cs:10-19`) | Medium |
| 19 | `pharm.transfer.state` | ❌ | `TransferState` (`PharmDbContext.cs:29-35`) | Medium |
| 20 | `pharm.stock_audit.state` | ❌ | `AuditState` (`PharmDbContext.cs:37-43`) | Medium |
| 21 | `notif.sms.state` | ❌ | `SmsState` (`NotifDbContext.cs:8-16`) | Low |
| 22 | `ipd.admission.blocked_from` | ❌ | a state-valued column; same set as #8 | Medium |

**The mechanism behind the `deleted` defect, exactly:** `AppointmentsService.AdvanceAsync`
(`AppointmentsService.cs:79-86`) executes
`UPDATE appt.appointment SET state = {to} WHERE id = {id} AND state = {from}`. The CAS guards
the **from** value; the **to** value is a free `string` parameter accepted from the caller. With
no CHECK, any caller can write any string, and the rows still satisfy every constraint the table
has. The same shape recurs at `OtService.MoveAsync` (`OtService.cs:222`) and
`PurchaseService.Transition` (`PurchaseService.cs:135`).

**Also unconstrained — enum-like text columns that are not called "state":**
`bill.receipt.tender` (**High** — `DayCloseService` groups the drawer by this exact string, and
the code comments say so at `BillDbContext.cs:81-85`; a stray `"Cash"` is a silent cash
variance), `bill.encounter.type` (OPD|ER), `bill.counter.kind`, `bill.charge_line.catalog_kind`,
`adm.rate_version.catalog_kind`, `adm.service.kind`, `adm.referrer.kind`, `ipd.ward.class`,
`ipd.admission.source`, `ipd.certificate.kind`, `pharm.stock_move.kind`,
`pharm.supplier_ledger.kind`, `pharm.outlet.kind`, `ot.case_team.role`, `lis.result.verifier_role`,
`kernel.approval_request.type`, `appt.appointment.source`.

---

### 4. Temporal / effective-dating

**The ERP has exactly one exclusion constraint, and it is correct.**

```sql
-- Modules/Admin/Hms.Admin/Data/Migrations/20260726125511_InitAdm.cs:95-101
CREATE EXTENSION IF NOT EXISTS btree_gist;
ALTER TABLE adm.rate_version ADD CONSTRAINT ex_rate_version_no_overlap
  EXCLUDE USING gist (
    catalog_kind WITH =, catalog_id WITH =, scope WITH =, branch_id WITH =,
    daterange(valid_from, valid_to) WITH &&);
```

I verified the **bound semantics agree with the resolver**, which is the part that is usually
wrong and here is right: `daterange(a, b)` defaults to `[a, b)`, and `RateResolver.cs:25-30`
queries `valid_from <= day AND (valid_to IS NULL OR day < valid_to)` — the same half-open
window. A `valid_to = NULL` yields an unbounded-above range, so an open-ended version correctly
blocks any later one. **No off-by-one.** This is good design and should be said so in the
handoff.

What is missing:

| Gap | Sev | Evidence |
|---|---|---|
| **No `valid_from` upper bound.** `ex_rate_version_no_overlap` is satisfied by an `infinity` lower bound (it overlaps nothing), so the constraint that exists does not catch the defect that occurred | **High** | see §1e |
| **No `ck_rate_version_effective_order`.** HR has `CHECK (effective_to IS NULL OR effective_to >= effective_from)` on all 8 of its effective-dated tables (`InitHr.cs:1349-1350`); `adm.rate_version` has none. Postgres does raise on `daterange(a,b)` with `a > b`, so this is *implicitly* caught — but as an opaque range error, not a named constraint | Medium | `AdmDbContext.cs:42-43` |
| **`kernel.approval_delegation` is effective-dated with nothing at all** — non-nullable `valid_from`/`valid_to`, no ordering CHECK, no overlap exclusion, no index. Two overlapping delegations for the same user mean an approval can be routed twice | Medium | `KernelEntities.cs:65-73` |
| **`ipd.bed_stay` is an interval with no interval constraint.** `(from_at, to_at)` per admission, one of which must be open. No exclusion constraint prevents two open stays for one admission, and no unique index either — `OpenStayAsync` uses `SingleOrDefaultAsync` (`IpdService.cs:418-419`), so a duplicate makes every subsequent transfer and discharge throw. An `EXCLUDE USING gist (admission_id WITH =, tstzrange(from_at, to_at) WITH &&)` would make it impossible. A second one on `(bed_id, tstzrange(...))` would make double-occupancy of a bed impossible | **High** | `IpdDbContext.cs:78-85, 207-212` |
| **`ot.ot_case` scheduling overlap is checked in code, not by constraint.** `ck_case_window` (`scheduled_to > scheduled_from`) exists, but theatre double-booking is a query under a `FOR UPDATE` on `ot.theatre` (`OtService.cs:188-194, 214`). That *is* correct as written — every scheduler takes the same row lock — but it is convention; an `EXCLUDE USING gist (theatre_id WITH =, tstzrange(scheduled_from, scheduled_to) WITH &&) WHERE (state IN (...))` would make it structural | Medium | `OtDbContext.cs:118-129` |
| `ipd.bed_day` uses date-keyed idempotency (`UNIQUE(admission_id, on_date)`) rather than a range — correct and simpler for this case | — | `IpdDbContext.cs:216` |

**Direct answer to the brief:** HR has 8 exclusion constraints (`InitHr.cs:1335-1358`, one per
`employee_pay_structure`, `employee_assignment`, `payroll_policy`, `pf_policy`, `gratuity_rule`,
`grace_time_rule`, `holiday_pay_policy`, `deduction_rule`). **The ERP has 1**, on
`adm.rate_version`. No PostgreSQL range *type* is used as a column anywhere in the product —
ranges appear only inside exclusion-constraint expressions.

---

### 5. Indexing vs query patterns

The distribution table is in Theme 4. Below are the misses that matter, ranked by lifecycle
impact. All query evidence is file:line from a full sweep of `Services` + `Pages/*.cshtml.cs`.

#### Missing — patient-lifecycle path

| Table | Predicate the code actually runs | Declared | Sev |
|---|---|---|---|
| **`bill.charge_line`** | `WHERE encounter_id = ? AND invoice_id IS NULL` — the OPD cart, re-run on every render and again at invoice creation (`BillingService.cs:302-303`, `Pages/Billing/Opd.cshtml.cs:121-122`); and `WHERE folio_id = ? AND invoice_id IS NULL` at 5 more sites (`BillingService.cs:198-199`, `IpdBilling.cs:215-216`, `Pages/Ipd/Discharge.cshtml.cs:74-75`, `Pages/FrontDesk.cshtml.cs:116-117`, `Pages/Ipd/Admissions.cshtml.cs:69-71`) | **none** | **High** |
| **`diag.test_order`** | `WHERE state IN (...)` (lab board, `LabBoard.cs:62-64`); `WHERE invoice_id = ? AND state = ?` (runs on **every payment**, `DiagnosticsRelease.cs:35-36`); `WHERE patient_id = ?`; `WHERE encounter_id = ?` | **none** | **High** |
| **`diag.order_test`** | `WHERE test_order_id IN (...)` at 8 sites (`LabBoard.cs:68-69`, `EmrRecord.cs:45-46`, `DiagnosticsRelease.cs:60-61`, …) | **none** | **High** |
| **`bill.encounter`** | `WHERE patient_id = ? AND on_date = ? AND counter_id = ? AND state = ?` — the get-or-create that runs on nearly every counter action (`CounterContext.cs:47-49`); `WHERE on_date = ? AND type = ?` (EMR queue, `Pages/Emr/Queue.cshtml.cs:29-31`) | **none** | **High** |
| **`bill.receipt.invoice_id`** | `WHERE invoice_id = ?` at 5 sites (`BillingService.cs:468,541`, `Pages/Billing/Invoice.cshtml.cs:73-75`, …). The table *is* indexed — on `counter_session_id` and `folio_id`, but not the most-used filter | **partial** | **High** |
| **`lis.sample_test.order_test_id`** | `WHERE order_test_id IN (...)` — the join every lab-board render does (`LabBoard.cs:81-82`, `Pages/Diagnostics/Slip.cshtml.cs:78-79`). The PK is `(sample_id, order_test_id)`, so this predicate has no usable prefix | **none usable** | **High** |
| **`appt.appointment.on_date`** | `WHERE on_date = ?` at 4 sites including the public queue board (`Pages/Appointments/Index.cshtml.cs:62-64`, `Pages/Public/Queue.cshtml.cs:28-29`, `Pages/FrontDesk.cshtml.cs:59-60`, `Pages/Dashboard.cshtml.cs:158-159`). The one index leads with `doctor_id`, so every "today's queue" scans | **none usable** | **High** |
| **`appt.doctor_schedule`** | full-table load at 7 sites + `WHERE doctor_id = ?` at 2 — and this table is the de-facto doctor master (Theme 5b) | **none at all** | **High** |
| **`adm.rate_version`** | `WHERE catalog_kind = ? AND catalog_id = ? AND scope IN (...) AND valid_from <= ? AND (valid_to IS NULL OR ? < valid_to)` — called **in a loop, once per catalogue item, on every picker render** (`RateResolver.cs:25-30` ← `Pages/Billing/Opd.cshtml.cs:86`, `Pages/Diagnostics/Order.cshtml.cs:74`, `Pages/Ipd/Admit.cshtml.cs:51,68`). Only the GiST index exists; there is **no btree** | **GiST only** | **High** |
| `bill.invoice` | `WHERE counter_session_id = ?` (day close, 3 sites); `WHERE created_at >= ? AND < ?` (all reports + dashboard); `WHERE patient_id = ?` | none of these | Medium |
| `ipd.bed_stay` | `WHERE admission_id = ? AND to_at IS NULL` — every board render (`Pages/Ipd/Board.cshtml.cs:42`, `IpdService.cs:419`, `FolioService.cs:71-72`). Index is `(admission_id)` alone | partial | Medium |
| `ipd.bed.state` | `WHERE state = 'free'` — every bed picker (3 sites) | none | Medium |
| `pharm.batch` without `outlet_id` | `Pages/Pharmacy/Products.cshtml.cs:56-59` and `Pages/Pharmacy/Reports.cshtml.cs:89-93` filter on `(product_id, state, expiry)`, skipping the index's leading column | unusable | Medium |
| `reg.patient` ILIKE on `uhid` / `phone_digits` | `PatientSearch.cs:35-48` runs `ILIKE '%x%'` against **btree** indexes, which cannot serve a leading wildcard. Only `full_name` has the GIN trigram index | partial | Medium |
| `notif.sms` | `WHERE event = ?` (`Pages/Notifications/Tray.cshtml.cs:31-35`), and state counts computed **in memory** after loading every row (`:40-45`) | wrong column | Low |

#### Declared but apparently unused

| Index | Evidence |
|---|---|
| `notif.sms (queued_at)` — the tray filters on `event` and sorts by `id` | `NotifDbContext.cs:50` vs `Pages/Notifications/Tray.cshtml.cs:31-35` |
| `radiology.study (modality_id, state)` — no code filters studies that way; the worklist routes `modality_test → diag.order_test → study.order_test_id` | `RadiologyDbContext.cs:90` vs `RadiologyReporting.cs:31-33,61-62` |
| `ot.ot_case (patient_id)` — no code filters cases by patient | `OtDbContext.cs:128` |
| `diag.test_order.promised_at` is stored but never filtered or sorted — the §9A.2 TAT-breach worklist does not exist yet (a scope observation, not an index defect) | `DiagDbContext.cs:28` |

**Disproportion, stated plainly:** Appointments 1 index, Notifications 1, LIS 2, Diagnostics 0 —
against HR's 57. Diagnostics and Billing's `charge_line` sit directly on the patient lifecycle
and carry the product's hottest predicates; HR runs monthly. On the 2 vCPU / 3 GB VM this is not
academic: `bill.charge_line` and `diag.order_test` grow monotonically and are seq-scanned on
every cart render.

---

### 6. Concurrency

**Baseline:** everything runs at PostgreSQL default `READ COMMITTED` — `HmsTx.cs:41` calls
`BeginTransactionAsync(ct)` with no isolation argument, and `IsolationLevel`, `Serializable`,
`RepeatableRead` and `SET TRANSACTION` have **zero occurrences in `src/`**. Every guarantee in
the product therefore rests on row locks, CAS and unique indexes, never on snapshot isolation.

**What is guarded well:** 11 `SELECT … FOR UPDATE` sites, 37 correct compare-and-swap sites with
`rowsAffected == 0` refusals, and `kernel.number_series` (`NumberSeriesService.cs:23-39`:
refuses to run outside an ambient transaction, `INSERT … ON CONFLICT DO NOTHING`, then
`UPDATE … RETURNING` holding the row lock to commit — gapless by construction). Deadlock
discipline is observed: the only two-row lock orders by id (`IpdService.cs:169`) and FEFO locks
order consistently (`StockService.cs:77`).

#### The single most consequential finding

**`Invoice.Version` — the product's only `IsConcurrencyToken` — is inert.** It is declared at
`BillDbContext.cs:230`, but **nothing in `src/` ever assigns or increments it** (verified: the
only `Version =` writes in Billing are `DayCloseSummary.Version = 1` at `DayCloseService.cs:92`).
EF emits `WHERE … AND version = @original`, but since the value never changes, **both racing
updates match and both succeed**. The invoice payment-state writes at `BillingService.cs:419`
and `:516-520` are last-write-wins. Severity **High** — and it is worse than having no token,
because the code and the ADR both read as though the aggregate is protected.

#### Aggregates by guard status

| Aggregate | Guard | Sev of gap |
|---|---|---|
| `ipd.Folio` | ✅ best in the codebase — `FOR UPDATE` (`FolioService.cs:26`) funnelled through `EnsurePostableAsync`, plus CAS on every settlement step, plus `UNIQUE(admission_id)` | — |
| `kernel.NumberSeries` | ✅ strongest — see above | — |
| `ot.OtCase` | ✅ theatre row lock + 5 CAS transitions + unique `case_no` | — |
| `ipd.Bed` | ✅ `FOR UPDATE` + CAS + ordered double-lock on transfer | — |
| `bill.CounterSession` | ✅ `FOR UPDATE` at 4 sites + partial unique index for the open-session rule | — |
| `lis.Sample` | ✅ all 4 transitions CAS | — |
| `appt.Appointment` | ✅ unique `(doctor_id, on_date, serial_no)` + retry loop + CAS on state — though the retry catch is a bare `catch (DbUpdateException)` (`AppointmentsService.cs:71`), so a NOT NULL failure retries 5× and reports "the queue is moving fast" | Low |
| **`bill.Invoice`** (payment state) | ⚠️ **inert token only**; indirectly serialized by the `bill.due` `FOR UPDATE` — except `CancelInvoiceAsync` writes `bill.due` at `:556` **without** taking that lock | **High** |
| **`bill.ChargeLine`** | ❌ **no guard of its own.** Folio-parented lines inherit the folio lock; **OPD/encounter lines have no equivalent** — `CreateInvoiceAsync` reads unbilled lines at `:302-305` and claims them at `:338` with nothing in between | **High** |
| **`ipd.Admission`** | ⚠️ all 12 transitions are correct CAS — but the "one open admission per patient" rule at `IpdService.cs:54-58` is a read-then-write `AnyAsync` with **no backing unique index**. Two concurrent admits of one patient into two beds both commit | **High** |
| **`ipd.BedStay`** | ❌ **nothing** — no lock, no CAS, no token, no unique index (`IpdDbContext.cs:207-212`, both indexes non-unique) | **High** |
| **`diag.TestOrder`** | ⚠️ two of three transitions CAS; **`Pages/Diagnostics/Delivery.cshtml.cs:64-65` is `UPDATE diag.test_order SET state='delivered' WHERE id=@id` with no state predicate and no rowsAffected check** — the only fully unguarded state write in the ERP | **High** |
| **`diag.OrderTest`** | ❌ **nothing at all** — insert-only, no unique index on `(test_order_id, test_catalog_id)`, no CHECK, no index (`DiagDbContext.cs:66` is a bare `ToTable`) | **High** |
| **`lis.Result`** | ❌ `VerifyAsync` (`LisService.cs:126-134`) reads `VerifiedAt is not null` then writes the verifier and e-sign hash via EF — **pure TOCTOU, no lock, no token**. Two verifiers can both sign; last write wins and the hash names only one. `EnterResultAsync`/`AmendAsync` rely on `UNIQUE(order_test_id, version)`, but the resulting `DbUpdateException` is **caught nowhere** — the loser gets a 500 | **High** |
| **`pharm.Batch`** | ⚠️ the **sale** path is guarded (FEFO `FOR UPDATE` + `ck_batch_qty`). Four other paths do read-modify-write on `qty_on_hand` with no lock: `RestockAsync` (`StockService.cs:124-125`), `RestockIndentAsync` (`:164-165`), `ReturnToSupplierAsync` (`:206-213`), `DisposeAsync` (`:246-251`) | **High** |
| **`pharm.StockAudit` / `StockAuditLine`** | ❌ `PostAuditAsync` reads `State != Approved` at `:335-337` then sets `Posted` via EF at `:356-357` — **not a CAS**. Two concurrent posts both pass and both apply the per-line deltas | **High** |
| `pharm.PurchaseOrder` | ⚠️ state CAS is correct; **quantity is not** — `line.ReceivedQty += qty` at `PurchaseService.cs:92` after an unlocked over-receipt check at `:85-87` is a TOCTOU, and the PO advance at `:106-109` is CAS-shaped with the result discarded | **High** |
| `ipd.Folio` R4 block/release | ⚠️ `IpdService.cs:233-236` and `:259-262` are CAS-shaped but **discard rowsAffected** — a lost update silently desynchronizes admission and folio | Medium |
| `emr.Note` | ⚠️ finalise/supersede are correct CAS with `ReloadAsync`; `OpenDraftAsync` (`:73-86`) is read-then-insert with **no unique index** on `(doctor_id, encounter_id, admission_id) WHERE state='draft'`, so two opens create two drafts | Medium |
| `kernel.ApprovalRequest` | ✅ CAS + `ck_approval_state`; `RaiseAsync` has no idempotency key (appears intentional) | Low |
| `notif.SmsMessage` | ❌ no guard — but the send path is not yet wired (`Simulated = true` default) | Low |

**`PayrollRun` (HR, for comparison):** guarded by `UNIQUE(branch_id, period, kind, sequence)`
(`HrDbContext.cs:233`) — the constraint, not a code check, is what stops March being generated
twice. This is the pattern the ERP aggregates above are missing.

**SQLSTATE handling:** `23505` is caught properly in exactly one place (`Submission.cs:24`,
matching on SQLSTATE *and* constraint name — the right way), used by 4 screens.
`one_open_session_per_counter` is caught by **exception-message substring** at
`BillingService.cs:47` — brittle. **`23514` (check_violation) is caught nowhere**, so every
CHECK the schema does have surfaces to the operator as a 500. Adding the domain CHECKs
recommended in §1 without adding a `23514` handler would convert silent corruption into visible
crashes — better, but it should be a deliberate pairing.

---

### 7. Referential integrity

**Confirmed: 6 foreign keys in the entire product, all in `Hms.Kernel/Auth/Migrations/20260726124128_InitAuth.cs:97,119,141,164,184,191`** — ASP.NET Identity's own, in the `adm`
schema. Zero elsewhere, including HR.

Cross-module FK absence is a deliberate ADR-0003 boundary and I am not challenging it. But
**intra-schema** parent/child pairs are inside one module's own bounded context, and their
absence is not a boundary decision — it is an omission. Every pair below can take a real FK
today without touching the modular-monolith rule.

| Schema | Child → parent | Orphan consequence | Sev |
|---|---|---|---|
| bill | `invoice_line → invoice` | an invoice line with no document — money on no bill | **High** |
| bill | `invoice_line → charge_line` | the frozen copy loses its source | Medium |
| bill | `receipt → invoice` | a payment against nothing | **High** |
| bill | `due → invoice` | PK is `invoice_id`; still no FK | **High** |
| bill | `charge_line → encounter` (nullable branch of the XOR) | an unbillable charge | **High** |
| bill | `day_close_summary → counter_session` | a day-close for no session | Medium |
| bill | `encounter → counter`, `counter_session → counter` | Medium |
| diag | `order_test → test_order` | **a test on no order** — named in the brief | **High** |
| diag | `delivery_log → test_order` | Medium |
| lis | `sample_test → sample`, `label_print → sample`, `sample.recollection_of → sample` | Medium |
| ipd | `bed_stay → admission`, `bed_stay → bed` | a stay in no bed / for no admission | **High** |
| ipd | `bed_day → admission`, `bed_day → bed` | a charged bed-day for no admission | **High** |
| ipd | `folio → admission` | Medium (already `UNIQUE`) |
| ipd | `indent → admission`, `indent → folio`, `indent_item → indent` | **High** |
| ipd | `bed → ward`, `certificate → admission` | Medium |
| emr | `note_drug → note`, `note.supersedes_id → note` | a prescription line on no note | **High** |
| pharm | `batch → product`, `batch → outlet`, `batch → purchase_order` | stock of no product — **the confirmed `product_id` orphan class** | **High** |
| pharm | `stock_move → batch`, `stock_move → product` | ledger entries pointing nowhere | **High** |
| pharm | `purchase_order → supplier`, `supplier_ledger → supplier` | **the confirmed `supplier_id` orphan class** | **High** |
| pharm | `purchase_order_line → purchase_order`, `sale_allocation → batch`, `issue_allocation → batch`, `transfer_line → transfer`, `transfer_batch → transfer`, `stock_audit_line → stock_audit`, `product → company` | Medium |
| ot | `case_team → ot_case`, `case_consumable → ot_case`, `ot_case → theatre` | **High** / Medium |
| radiology | `modality_test → modality`, `study → modality` | Medium |
| adm | `rate_version → service` / `→ test_catalog` | polymorphic on `catalog_kind` — **cannot** be a plain FK; needs two partial FKs or a trigger | **High** |
| kernel | `approval_policy`, `approval_delegation` — no parents to point at | — |
| hr | `payroll_line → payroll_run`, `payroll_component_line → payroll_line`, `payslip → payroll_line`, `loan_installment → loan`, `roster_entry → roster`, `holiday → holiday_calendar`, `employee_pay_component → employee_pay_structure` | **`payroll_line → payroll_run` is named in the brief** | **High** |

**The confirmed orphan classes and where they come from:**

| Class | Master | Why orphans occur |
|---|---|---|
| `doctor_id` | **none exists** | Theme 5b — no table, ids minted by `MAX+1` on an unindexed column (`Pages/Admin/People.cshtml.cs:69`) |
| `product_id` | `pharm.product` | intra-`pharm` for 6 tables (no FK); cross-module for `emr.note_drug`, `emr.favourite`, `ipd.indent_item`, `ot.case_consumable` |
| `supplier_id` | `pharm.supplier` | intra-`pharm`, no FK |
| `referrer_id` | `adm.referrer` | cross-module (`bill.charge_line`, `bill.invoice_line`, `diag.test_order`) — genuinely a boundary case; needs a validating contract call, not an FK |

Aggravating factor: because these columns are non-nullable `bigint` with no FK, **`0` is a valid
value that reads as a real id** — an orphan is silent rather than an error.

---

### 8. Audit and immutability

Hard rule 4 says financial and clinical writes are append-only and never hard-deleted. At the
**schema** level this is enforced by exactly three artefacts, and one of them is not in force.

| Artefact | Where | In force at runtime? |
|---|---|---|
| **Trigger `trg_receipt_immutable`** — rejects UPDATE or DELETE on a `bill.receipt` whose counter session is `closed` | `InitBill.cs:266-276` | ✅ **Yes.** Triggers are role-independent, so this applies to `hms_migrator` too. *(Note: it only fires once the session is closed — receipts in an open session remain mutable and deletable.)* |
| **`REVOKE DELETE ON ALL TABLES IN SCHEMA bill FROM hms_app`** | `InitBill.cs:289` | ❌ **No.** The app connects as `hms_migrator` (`deploy/compose.yml:35`) |
| **`REVOKE UPDATE, DELETE ON kernel.audit_event FROM hms_app`** | `InitBill.cs:290` | ❌ **No**, same reason. **The audit trail is rewritable by the running application.** |

Additional observations:

| Finding | Sev |
|---|---|
| **The grant block only ever mentions `bill` and `kernel.audit_event`.** `ipd`, `diag`, `lis`, `pharm`, `emr`, `ot`, `reg`, `appt`, `notif`, `adm`, `hr` have **no grants and no revokes at all** — which is also why `hms_app` cannot be switched on without the cross-schema grant migration the compose comment refers to. `pharm.stock_move` and `pharm.supplier_ledger` are documented as append-only in code (`PharmDbContext.cs:141`, `:229-230`) with **nothing enforcing it** | **High** |
| **Exactly one trigger exists in the entire product** (verified: one `CREATE TRIGGER`, one `CREATE OR REPLACE FUNCTION`, zero `CREATE RULE`) | **High** |
| **No insert-only table is structurally insert-only.** `kernel.audit_event`, `pharm.stock_move`, `pharm.supplier_ledger`, `lis.result`, `bill.day_close_summary` are all append-only by convention | **High** |
| **Attributability is by convention too.** `created_by` / `actor_id` are non-nullable `bigint` with no FK to `adm.AspNetUsers`, so `0` is an acceptable actor. Nothing defaults them and nothing checks them | Medium |
| Hard deletes in application code are, encouragingly, **almost absent** — 3 real sites, none financial or clinical: `radiology.modality_test` remap (`Pages/Radiology/Modalities.cshtml.cs:81`), a permission row (`Hms.Shell/Pages/Admin/Users.cshtml.cs:263`), and `emr.note_drug` for an unfinalised draft (`EmrService.cs:106`, with a comment explaining that a draft's drug list is scratch). The discipline is being kept — it is just not enforced | ✅ |
| Soft-delete/supersession patterns are used correctly where they matter: `reg.patient.merged_into` + `active`, `emr.note.supersedes_id`, `lis.result` versioning, `bill.day_close_summary.supersedes`, `kernel.import_batch.reversed_by_batch` | ✅ |

---

## (c) What is well designed

These are verified, not assumed, and should carry into the handoff as things **not** to disturb.

1. **The money identity CHECK.**
   `ck_invoice_identity: net = gross - discount + tax + rounding_adj` (`BillDbContext.cs:222-223`).
   Arithmetic that cannot be wrong beats arithmetic that is checked. HR copied the idea for
   `ck_payroll_line_net` (`InitHr.cs:1365-1367`), which is exactly the right kind of convergence.

2. **The polymorphic-parent discipline is uniform and complete.** Six `num_nonnulls(a, b) = 1`
   XOR checks, one per place a row can hang off an outdoor visit or an indoor folio:
   `ck_charge_parent`, `ck_invoice_parent`, `ck_receipt_parent` (`BillDbContext.cs:216,224,235`),
   `ck_test_order_parent` (`DiagDbContext.cs:64-65`), `ck_note_parent` + `ck_vitals_parent`
   (`EmrDbContext.cs:183,202`), `ck_case_parent` (`OtDbContext.cs:122`). Every module that
   acquired an indoor path added the same guard in the same shape. That consistency is rare and
   worth protecting.

3. **Gapless number series.** `NumberSeriesService.cs:23-39` — refuses to run outside the
   caller's ambient transaction, `INSERT … ON CONFLICT (branch_id, doc_type, fiscal_year) DO
   NOTHING` against a real unique index (`KernelDbContext.cs:40`), then `UPDATE … SET next_value
   = next_value + 1 … RETURNING next_value - 1`, holding the row lock to commit so a rollback
   rolls the counter back. Genuinely gapless, and the assertion about the ambient transaction is
   what makes it stay that way. **Best single piece of schema+service design in the product.**

4. **Unique filtered indexes used as concurrency primitives.** All verified present:
   - `invoice.submission_token` `UNIQUE … WHERE submission_token IS NOT NULL`
     (`BillDbContext.cs:228`) — and, unusually, it is *paired with correct client code*:
     `Submission.cs:24` matches on SQLSTATE `23505` **and** constraint name, used by 4 screens.
     This is the model the rest of the codebase should follow.
   - `emr.note.supersedes_id` `UNIQUE … WHERE supersedes_id IS NOT NULL`
     (`EmrDbContext.cs:191`) — two doctors cannot supersede the same note.
   - `one_open_session_per_counter` — `UNIQUE(counter_id) WHERE state IN ('opened','active','reopened')` (`InitBill.cs:262-263`).
   - `ipd.bed_day UNIQUE(admission_id, on_date)` (`IpdDbContext.cs:216`) — makes bed-day
     catch-up idempotent by constraint.
   - `hr.employee.user_ref` and `hr.attendance_day (employee_id, on_date)`.

5. **Price snapshotting and effective dating.** `charge_line` and `invoice_line` both carry
   `description_snapshot`, `unit_price` and `rate_version_id`; the resolver's half-open window
   `[valid_from, valid_to)` (`RateResolver.cs:25-30`) **exactly matches** the exclusion
   constraint's `daterange(valid_from, valid_to)` bounds (`InitAdm.cs:100`). I checked this
   specifically because mismatched bound semantics are the usual bug here, and it is right. The
   reprice flow closes the open version rather than editing it (`Masters.cshtml.cs:221-228`) —
   hard rule 5 is genuinely honoured.

6. **The `reg.patient` search stack.** A `STORED` generated column for `phone_digits`
   (`RegDbContext.cs:70-71`) and another for `name_phonetic` via `dmetaphone`
   (`InitReg.cs:92-93`), a GIN trigram index on `full_name`, and a partial index
   `ix_patient_live (branch_id, uhid) WHERE merged_into IS NULL` (`InitReg.cs:94-96`). Making
   the normalised form a generated column instead of a second write path is exactly right and
   the code comment says why.

7. **Money as `bigint` whole taka throughout.** No decimal, no float, product-wide. Clinical
   measurements stored as integer tenths for the same reason (`EmrDbContext.cs:73-76` explains
   it). Consistently applied.

8. **Nullability derives cleanly from C# annotations**, and the *simple* cases are all correct —
   money never nullable, state never nullable, `branch_id` and `patient_id` never nullable.

9. **The service-layer concurrency discipline itself.** 37 correct CAS sites with explicit
   `rowsAffected == 0` refusals, 11 `FOR UPDATE` row locks, consistent lock ordering, one
   ambient transaction across all 15 contexts (`HmsTx.cs:53-85`) with kernel flushed first. The
   *engineering* is sound; my criticism is only that it lives in one layer.

10. **HR shows the team already knows the answer.** The gaps in §1–§4 are not a capability
    problem: `InitHr.cs:1333-1388` does everything this report asks for, six days after the ERP
    schemas were frozen.

---

## (d) Prioritised schema changes for patient-lifecycle robustness

Ordered by how much each reduces the chance of a patient's journey producing a wrong record.
"Lifecycle path" means: register → appointment → encounter → vitals/consult → order →
sample/study → result → invoice → receipt → (admit → folio → discharge).

### Tier 1 — do these first (each closes a confirmed defect class, all are additive DDL)

| # | Change | Closes | Effort |
|---|---|---|---|
| 1 | **CHECK constraints on all 22 state columns.** Generate from the existing `*State` constants classes, which are already the single source of truth (`AdmissionState`, `InvoiceState`, `SampleState`, …). Add a build guard that fails when a constants class and its CHECK diverge. Note `diag.order_test.state` and `bill.encounter.state` need a constants class created first — they use bare literals | `state='deleted'`; spec 0039 **AC5** | S |
| 2 | **Value-domain CHECKs on the lifecycle money/qty columns**: `charge_line.qty`, `invoice_line.qty` (`> 0` + cap), `charge_line.unit_price`/`amount`, `invoice_line.unit_price`/`amount`, `invoice.gross/discount/net/tax` (`>= 0`, `discount <= gross`), `rate_version.price >= 0` | qty 999999 → 299,999,700 Tk | S |
| 3 | **Clinical range CHECKs on `emr.vitals` and `emr.glucose_reading`** (SpO2 50–100, pulse 20–300, systolic 40–300, diastolic 20–200 and `< systolic`, temp 250–450, weight 1–4000, glucose 5–500) | SpO2=999, BP −1/−9 | S |
| 4 | **Bound `adm.rate_version.valid_from`** (`< 'infinity'` and `<= current_date + interval '5 years'`), and add `ck_rate_version_effective_order`. Fix the app route at `Pages/Admin/Masters.cshtml.cs:197` at the same time | `valid_from = infinity` frozen price | XS |
| 5 | **Catch SQLSTATE `23514` and render it.** Items 1–4 convert silent corruption into `PostgresException`, which today is caught nowhere and becomes a 500. Extend the `Submission.cs:24` pattern (SQLSTATE + constraint name → operator message). **Ship this in the same change as 1–4, not after** | spec 0039 **AC7** | S |
| 6 | **`HasMaxLength` on every string column**, or the equivalent `length(col) <= n` CHECK | the 100 KB-paste lockout | M |

### Tier 2 — structural integrity on the lifecycle spine

| # | Change | Closes | Effort |
|---|---|---|---|
| 7 | **Intra-schema FKs on the lifecycle spine**, in this order: `invoice_line → invoice`, `receipt → invoice`, `due → invoice`, `order_test → test_order`, `bed_stay → admission`, `bed_day → admission`, `indent_item → indent`, `note_drug → note`, `case_team → ot_case`, `stock_move → batch`, `batch → product`, `payroll_line → payroll_run`. Add as `NOT VALID` first, clean the existing orphans, then `VALIDATE CONSTRAINT` — this keeps the migration non-blocking on the 2 vCPU VM | 4 confirmed orphan classes | M |
| 8 | **Create a doctor master** (`adm.doctor`), migrate `appt.doctor_schedule.doctor_id`, and make id allocation a sequence rather than `MAX+1` (`Pages/Admin/People.cshtml.cs:69`). Without this, item 7 cannot cover `doctor_id` at all, and two doctors can still merge into one identity | Theme 5b | M |
| 9 | **`EXCLUDE USING gist` on `ipd.bed_stay`**: `(admission_id WITH =, tstzrange(from_at, to_at) WITH &&)` and `(bed_id WITH =, tstzrange(from_at, to_at) WITH &&)`. Makes two open stays and double-bed-occupancy impossible, and closes the fully unguarded `BedStay` aggregate | §4, §6 | S |
| 10 | **`UNIQUE(patient_id) WHERE state NOT IN ('discharged','death','absconded')` on `ipd.admission`** — turns the read-then-write check at `IpdService.cs:54-58` into a constraint | §6 | XS |
| 11 | **`UNIQUE(test_order_id, test_catalog_id)` on `diag.order_test`** — the aggregate currently has no protection of any kind | §6 | XS |
| 12 | **State-dependent nullability CHECKs** on the terminal states that matter: `admission.discharged_at`, `counter_session` cash fields, `lis.result` verifier fields, `emr.note.finalised_*`, `ot.ot_case.started_at`/`finished_at` | §2 | S |

### Tier 3 — concurrency correctness

| # | Change | Closes | Effort |
|---|---|---|---|
| 13 | **Make `Invoice.Version` real or delete it.** Either increment it on every state write (`BillingService.cs:419`, `:516-520`) and handle `DbUpdateConcurrencyException`, or drop it and take a `FOR UPDATE` on the invoice. The present state is worse than either — the code reads as protected and is not | §6 | S |
| 14 | **Guard the encounter/OPD charge-line claim.** `CreateInvoiceAsync` (`BillingService.cs:302-338`) needs the encounter-path equivalent of the folio's `FOR UPDATE` — lock `bill.encounter` before reading unbilled lines | §6 | S |
| 15 | **Fix the three unguarded state writes:** the raw `UPDATE` at `Pages/Diagnostics/Delivery.cshtml.cs:64-65` (no state predicate at all), and the two discarded-`rowsAffected` folio CAS calls at `IpdService.cs:233`, `:259` | §6 | XS |
| 16 | **`lis.Result.VerifyAsync` TOCTOU** (`LisService.cs:126-134`) — make it a state-guarded UPDATE, so two verifiers cannot both sign one result | §6 | XS |
| 17 | **`pharm` unlocked quantity paths** — route `RestockAsync`, `RestockIndentAsync`, `ReturnToSupplierAsync`, `DisposeAsync` (`StockService.cs:124,164,206,246`) through the same `FOR UPDATE` the sale path uses; make `PostAuditAsync` (`:335-357`) a CAS; add `CHECK (received_qty BETWEEN 0 AND qty)` to `purchase_order_line` | §6 | M |

### Tier 4 — performance and enforcement posture

| # | Change | Closes | Effort |
|---|---|---|---|
| 18 | **The seven lifecycle indexes**, in impact order: `charge_line (encounter_id) WHERE invoice_id IS NULL`; `charge_line (folio_id) WHERE invoice_id IS NULL`; `charge_line (invoice_id)`; `test_order (state)` and `(invoice_id, state)`; `order_test (test_order_id)`; `encounter (patient_id, on_date)`; `receipt (invoice_id)`; `sample_test (order_test_id)`; `appointment (on_date)`; a **btree** on `rate_version (catalog_kind, catalog_id, scope, branch_id, valid_from)`. Most are `CREATE INDEX CONCURRENTLY` one-liners and together they cover the hot predicate of every screen on the lifecycle | §5 | S |
| 19 | **Drop the three unused indexes** (`sms(queued_at)`, `study(modality_id, state)`, `ot_case(patient_id)`) — or, better, fix the queries that should have been using them (`Tray.cshtml.cs:40-45` counts states in memory over a full table load) | §5 | XS |
| 20 | **Decide the enforcement posture for hard rule 4** and write it down as an ADR. Either (a) complete the `hms_app` cross-schema grant migration and switch the runtime connection off `hms_migrator`, or (b) accept that grants are not the mechanism and replace them with `BEFORE DELETE` reject-triggers on the append-only tables (`kernel.audit_event`, `pharm.stock_move`, `pharm.supplier_ledger`, `lis.result`, `bill.*`), which work regardless of role. **What must not persist is the current state: a rule documented as enforced, enforced against a role the app does not use** | §8 | M |
| 21 | **`EXCLUDE` on `kernel.approval_delegation`** and an ordering CHECK — an overlapping delegation can route one approval twice | §4 | XS |

### Sequencing note

Tier 1 items 1–5 must ship together. Items 1–4 add constraints that will start throwing `23514`,
and item 5 is what turns those throws into operator-readable refusals instead of 500s. Item 7's
FK work must be `NOT VALID` → clean → `VALIDATE` because existing orphans are known to be
present, and a blocking `ALTER TABLE … VALIDATE` on the 2 vCPU / 3 GB target is a real
availability risk. Item 8 (the doctor master) is a prerequisite for the `doctor_id` portion of
item 7 and cannot be deferred behind it.

---

## Confidence and limits

- **Verified by reading source:** every file:line citation, all constraint counts, the FK count
  (6, all Identity), the index counts per module, the raw-SQL inventory (9 `migrationBuilder.Sql`
  calls across 5 files), the absence of `[Range]` anywhere, the absence of a doctor master, the
  inertness of `Invoice.Version`, the runtime connection running as `hms_migrator`, and the
  half-open-bound agreement between `RateResolver` and `ex_rate_version_no_overlap`.
- **Not verified — stated as inference:** the `DateOnly.MaxValue → 'infinity'` mapping is
  Npgsql's documented default (Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3) and nothing in this
  repo disables it, but I could not confirm it against a live database. The rest of that defect
  chain (the parser accepting `31/12/9999`, the missing upper bound, the same value being written
  to the prior version's `valid_to`) **is** verified from source.
- **Not checked:** actual row counts, real query plans, index bloat, and whether the deployed
  database's schema matches these migrations. All of that needs DB access, which was declined.
  In particular, the recommended indexes are derived from query *shape*, not from measured plans.
