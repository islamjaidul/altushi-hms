# 03 — Data Model

- **Status:** Draft for PM review · **Date:** 2026-07-26 · **Spec:** `docs/specs/0003-mvp-architecture/`
- PostgreSQL (ADR-0002). Schema-per-module (`reg`, `appt`, `bill`, `diag`, `lis`, `adm`, `notif`, `kernel`), EF Core migrations per module (ADR-0003). Types below are illustrative DDL — the migration source is authoritative once code exists.

Conventions: PK `id bigint generated always as identity`; every business table carries `branch_id` (ADR-0007), `created_at timestamptz`, `created_by`; money is **`bigint` whole taka** (C3 — no decimals anywhere in operator-entered money); soft state never deletes rows. App role has **no DELETE grant** on any `bill.*`, `kernel.audit_event`, `lis.result*` table (C5).

## 1. Kernel

```sql
kernel.branch(id, code, name, active)                      -- MVP: 1 row
kernel.number_series(                                       -- ADR-0004: gap-free
  id, branch_id, doc_type text, fiscal_year text,
  next_value bigint, display_format text,
  unique (branch_id, doc_type, fiscal_year))
-- issuance: SELECT ... FOR UPDATE; increment; use inside the same tx

kernel.approval_request(
  id, branch_id, type text,                                 -- discount|refund|edit|reset|reopen|late_post|rate_change|merge
  source_table text, source_id bigint,
  requested_by, reason text, requested_at,
  state text check (state in ('pending','approved','rejected','expired')),
  decided_by, decided_at, decision_note,
  amount bigint null, threshold_snapshot jsonb)             -- what rule routed it
kernel.approval_policy(type, tier, role, threshold_min bigint, escalation_minutes int)
kernel.approval_delegation(from_user, to_user, valid_from, valid_to, types text[])

kernel.audit_event(                                         -- ADR-0011, append-only
  id, branch_id, at timestamptz, actor_id, actor_name_snapshot,
  action text, entity text, entity_id bigint,
  before jsonb, after jsonb, correlation_id uuid, tier smallint)
  -- monthly partitions; no UPDATE/DELETE grants; nightly hash-chain job

kernel.job(                                                 -- queue (ADR-0002)
  id, type text, payload jsonb, run_after timestamptz,
  attempts int, locked_by, locked_at, done_at, error text)
  -- drain: FOR UPDATE SKIP LOCKED
kernel.outbox(id, event_type, payload jsonb, created_at, dispatched_at) -- 02 §4

kernel.entitlement(module text, enabled bool, meta jsonb)   -- ADR-0016 (from signed file)
kernel.setting(key, value jsonb)                            -- hospital identity, fiscal config,
                                                            -- business-day boundary, thresholds
kernel.import_batch(id, kind, source_file, sha256, mapping jsonb,
                    committed_by, committed_at, reversed_by_batch bigint null) -- ADR-0010
```

## 2. Registration (`reg`)

```sql
reg.patient(
  id, branch_id, uhid text unique,                          -- ADR-0004
  full_name text, name_phonetic text generated,             -- dmetaphone for dup-warning
  sex char(1), dob date null,
  age_years smallint null, age_months smallint null,
  age_estimated bool default false, age_as_of date null,
  check (dob is not null or age_years is not null or unknown_identity),
  phone text null, guardian text, area text, address text,
  nid text null, blood_group text null,
  patient_type text default 'general',                      -- general|corporate|package|staff (M18 seam)
  unknown_identity bool default false,                      -- edge 25
  provisional bool default false,                           -- edge 11
  merged_into bigint null references reg.patient,           -- edge 23
  active bool default true, photo_ref text null)
-- indexes: trgm on full_name; btree phone; (branch_id, uhid);
--   partial index where merged_into is null (search excludes merged)

reg.patient_merge(id, survivor_id, merged_id, approval_id, at, by, repointed jsonb)
```

Duplicate warning at save: same phone OR (phonetic-name match AND age band ±2y) → non-blocking modal listing candidates (§9A.2).

## 3. Appointments (`appt`)

```sql
appt.doctor_schedule(id, doctor_id, weekday, slot_from, slot_to, room, max_serials)
appt.appointment(
  id, branch_id, patient_id, doctor_id, on_date date, serial_no int,
  state text,                                               -- §11 Appointment machine
  source text, created_by, ...,
  unique (doctor_id, on_date, serial_no))                   -- ADR-0015 #1
```

## 4. Billing (`bill`) — the money core

```sql
bill.encounter(id, branch_id, patient_id, on_date, type,    -- OPD|ER
               doctor_id null, counter_id, state)
bill.counter(id, branch_id, name, kind)                     -- front-desk|diagnostics|er
bill.counter_session(
  id, branch_id, counter_id, operator_id,
  business_day date,                                        -- ADR-0004 boundary rule
  opened_at, opening_float bigint,
  state text,                                               -- §11 Counter machine
  closed_at, counted_cash bigint, expected_cash bigint, variance bigint,
  close_approved_by null, carry_closed bool default false)  -- edge 17
create unique index one_open_session_per_counter
  on bill.counter_session(counter_id) where state in ('opened','active');

bill.charge_line(                                           -- the C2 spine (02 §2.3)
  id, branch_id,
  encounter_id bigint null, folio_id bigint null,           -- folio: post-MVP parent
  check (num_nonnulls(encounter_id, folio_id) = 1),
  source_module text, catalog_kind text, catalog_id bigint,
  description_snapshot text, qty int, unit_price bigint,    -- resolved price (C6)
  rate_version_id bigint,                                   -- proof of resolution
  amount bigint,                                            -- qty*unit_price, whole taka
  doctor_id null, referrer_id null,                         -- payout attribution (ADR-0017)
  test_order_id null,                                       -- diagnostics join
  invoice_id null)                                          -- null = unbilled (appears at counter)

bill.invoice(
  id, branch_id, invoice_no text unique, fiscal_year text,
  patient_id, encounter_id, counter_session_id,
  gross bigint, discount bigint, discount_approval_id null,
  tax bigint default 0, tax_code text null,                 -- dormant (ADR-0018)
  net bigint, rounding_adj smallint,                        -- §6 rule, audit of the rounding step
  state text,                                               -- §11 Invoice machine
  version int,                                              -- optimistic (ADR-0015 #3)
  check (net = gross - discount + tax + rounding_adj))
bill.invoice_line(...)                                      -- frozen copy of charge lines at billing

bill.receipt(
  id, branch_id, receipt_no text unique, invoice_id,
  counter_session_id, amount bigint,                        -- negative = refund (edge 20)
  tender text,                                              -- cash|card|bkash|nagad|corporate
  tender_ref text null,                                     -- card/wallet ref (§13 I13 manual)
  operator_id, at, refund_of_receipt bigint null, approval_id null)
-- receipts immutable once session closed: trigger rejects UPDATE when session.state='closed'

bill.due(invoice_id pk, balance bigint, last_followup jsonb) -- row-locked on collect (ADR-0015)
bill.day_close_summary(                                     -- §6.6 holding structure
  id, branch_id, counter_session_id unique, business_day,
  dept_split jsonb, tender_totals jsonb,
  gross bigint, discount bigint, net bigint, due_created bigint,
  due_collected bigint, refunds bigint, variance bigint,
  version int, supersedes bigint null)                      -- reopen appends (02 §2.6)
```

## 5. Rate plans & catalog (`adm`)

```sql
adm.service(id, code, name, dept, kind, provisional bool, active)
adm.test_catalog(id, code, name, dept, sample_types text[], tat_minutes int,
                 template jsonb, provisional bool, active)
adm.ref_range(id, test_param, sex char(1) null, age_from_days int, age_to_days int,
              low numeric, high numeric, unit text)          -- lab values stay numeric
adm.rate_version(
  id, branch_id, scope text,                                 -- standard|corporate:<id>|package:<id>
  catalog_kind text, catalog_id bigint,
  price bigint, valid_from date, valid_to date null,
  author_id, approval_id,                                    -- MD-approved (§12)
  exclude using gist (catalog_kind with =, catalog_id with =, scope with =,
                      daterange(valid_from, valid_to) with &&))  -- one active version (C6)
adm.bed(id, branch_id, ward, class, bed_no, tariff_service_id,
        status text)                                         -- incl. not_yet_available (edge 14)
adm.doctor(id, name, bmdc_no null, specialty, room, provisional bool, ...)
adm.bank_account(id, owner_kind, owner_id, account_name, account_no,
                 bank, branch_name, routing_no, valid_from, valid_to) -- ADR-0017
adm.referrer(id, code, name, kind, territory null, active)   -- access-restricted (§8 N5)
```

Price resolution at billing: most specific scope (package > corporate > standard) where `valid_from ≤ business_day < valid_to`; resolved `rate_version_id` stored on the charge line. Changing a price = inserting a new version (edge 13); history immutable.

## 6. Rounding rule (edge 30) — normative

1. Unit prices and quantities are whole taka / integers ⇒ line `amount` is exact.
2. `gross = Σ line amounts` (exact). Percentage discounts (and future % VAT) are computed on the total **once**, rounded **half-up to whole taka**, stored in `discount` (/`tax`).
3. `net = gross − discount + tax + rounding_adj` where `rounding_adj ∈ {−0,…}` exists only if a future rule (e.g., cash rounding) requires it; MVP keeps it 0. The CHECK constraint makes the identity structural.
4. Payments/dues/day-close only ever see stored whole-taka totals ⇒ reconciliation is exact by construction. Money-path tests assert: for all invoices, `Σ receipts + due.balance = net`, and `Σ day-close tender totals = Σ session receipts`.

## 7. Diagnostics & LIS (`diag`, `lis`)

```sql
diag.test_order(id, branch_id, patient_id, encounter_id,
  ordering_doctor_id null, referrer_id null,
  state text,                                               -- §11 TestOrder machine
  promised_at timestamptz,                                  -- TAT promise (§9A.2)
  invoice_id null, cancel_approval_id null)
diag.order_test(id, test_order_id, test_catalog_id, charge_line_id,
  state text, refunded bool default false)                  -- edge 21 partial refunds
diag.delivery_log(id, test_order_id, report_version int, delivered_at,
  collector_note text, delivered_by)                        -- edge 22: version delivered

lis.sample(
  id, branch_id, barcode text unique,                       -- single identity (edge 27/33)
  sample_type text, state text,                             -- §11 Sample machine
  collected_at/by, received_at/by,
  rejected_reason text null, recollection_of bigint null,   -- child chain (02 §2.8)
  disposal_note jsonb null)                                 -- edge 21
lis.sample_test(sample_id, order_test_id, primary key(sample_id, order_test_id)) -- M:N (edge 33)
lis.label_print(id, sample_id, printed_at, printed_by, reprint bool) -- edge 27 audit
lis.result(
  id, order_test_id, version int,                           -- v1, v2… all retained (edge 22)
  values jsonb,                                             -- {param: {value, unit, flag, ref_used, age_precision}}
  narrative text null,
  entered_by, entered_at,
  verified_by null, verified_at, verifier_role text,        -- treating|pathologist|reporting_consultant (edge 34)
  esign_hash text, signature_image_ref text null,
  amend_approval_id null, supersedes_version int null,
  unique (order_test_id, version))
```

State transitions guarded by `UPDATE … WHERE state='…'` (ADR-0015). `TestOrderPaid` outbox event → label print jobs + LIS worklist row; unpaid orders are visible to billing as unbilled charge lines (the §9A.2 seam).

## 8. Notifications (`notif`)

```sql
notif.template(id, event, body text, lang_hint, active)      -- Bangla-capable (ADR-0014)
notif.sms(id, branch_id, template_id, recipient text, body text,
  trigger_ref jsonb, state text,                             -- §11 SMS machine
  segments int, simulated bool,                              -- edge 3
  queued_at, sent_at, delivered_at, fail_reason, attempts)
```

## 9. Users & permissions (`adm`)

```sql
adm.app_user(id, username unique, pw_hash, display_name, role_ids int[],
  totp_secret null, must_2fa bool, active, employee_ref null)
adm.role(id, name, system bool)                              -- templates per §12
adm.permission(role_id, module text, action text, scope text null)
adm.user_session(id, user_id, created_at, last_seen, locked bool, revoked_at) -- ADR-0019
```

Menu tree = f(permissions ∩ entitlements) (ADR-0016/0019). Approval thresholds live in `kernel.approval_policy`, not in roles.

## 10. Read models (Dashboard, M22)

Materialized-lite: plain views over `day_close_summary`, open sessions, dues, discount log, TAT timestamps — refreshed by `SessionClosed`/`ResultVerified` events bumping a cache key (no extra infrastructure). Dashboard shows **today so far** (live from receipts) + closed-day summaries; both derive from the same tables the future M15 will consume, so numbers never shift when Accounts arrives (§6.6).

## 11. Sizing & retention notes

§14 typical/day: ~350 invoices, ~1,000 samples ⇒ row growth is trivial for Postgres on this box (estimate: < 2 GB data/year excluding PDFs; PDFs dominate disk — retention & watermarks in `06-deployment.md`, edge 32). 5-year patient volume (150k–1M rows) sits comfortably under the trigram indexes' capability for ≤ 1 s type-ahead (verify in the demo-load test; §8 N1).

## 12. Migration policy

EF Core migrations per module schema, applied by the app at startup under an advisory lock (single-flight on multi-worker scale-up); destructive migration operations (drop/narrow) are forbidden by CI check on generated SQL — additive evolution only, matching the append-only posture of the product.
