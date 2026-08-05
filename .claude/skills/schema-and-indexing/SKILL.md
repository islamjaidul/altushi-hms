---
name: schema-and-indexing
description: How to shape a table and choose its indexes in this codebase — entity and column conventions, where an invariant becomes a CHECK constraint, soft-delete and effective-dating shape, and the index rules for foreign keys, list filters, sort orders, uniqueness and substring search. Use before writing any entity, DbContext configuration or migration, when adding a filter or search box to a screen, and when a list or type-ahead is slow.
---

# Schema and indexing — HMS ERP

Read `code-conventions` first for the layout and the `HmsTx` rule; this skill is only about the
shape of the data and how it is reached.

The design constraint that decides most calls: **§16's single VM, 2 vCPU / 3 GB**, holding five
years of a 50–300 bed hospital at §14 volumes (~350 invoices and ~1,000 samples a day). There is no
read replica, no cache tier and no horizontal escape. A sequential scan you can afford in year one
is a support ticket in year four.

## Table shape

**One schema per module** (`reg`, `bill`, `diag`, `lis`, `appt`, `adm`/`adm_data`, `notif`,
`pharm`, `ipd`, `emr`, `ot`, `radiology`, `hr`, `kernel`, auth). A module owns its schema; nothing
else writes to it. Naming is snake_case, applied by `UseSnakeCaseNamingConvention()` — never
hand-name a column.

**No foreign key may cross a schema boundary.** A cross-module reference is a plain id column plus
a `.Contracts` lookup. Note that nothing currently enforces this automatically — `eng/check-fkeys.sh`
is about *function keys* (F2/F3/F9/F10), not foreign keys, despite what the guard table in
`code-conventions` says. It is on you and on review.

**Money is `int`, whole taka.** No decimal, no float, ever. **Time is `DateTimeOffset` in UTC** —
Npgsql binds nothing else — converted at the edge with `Ui.Local` / `Ui.DhakaMidnightUtc`. A
business day is not a UTC date; use `BusinessDayCalendar`.

**Widths are declared.** `HasMaxLength` on every string column. An unbounded `text` for a name or a
code is a defect waiting for a paste.

## Invariants belong in the database

This codebase carries **379 `HasCheckConstraint` declarations** plus hand-written `CHECK` SQL. That
is deliberate and it is the standard to meet: a rule enforced only in a page model is enforced only
on the path that goes through that page model — and spec 0037 is what happens when the write path
you trusted turns out not to run.

Put it in the database when the rule is about the row: a state must be one of a known set, an
amount must be non-negative, a closed record must carry its closing stamp, a discharge date cannot
precede admission. Put it in a service when the rule spans rows or needs a lock.

**A CHECK constraint without a test that watches the database refuse the bad row is not done**
(`tdd-loop`). Name the test for the refusal:
`The_database_refuses_a_closed_task_with_no_closing_stamp`.

**Migrations are additive** (03 §12) — no `DROP TABLE`, `DROP COLUMN`, `TRUNCATE`. Widening a CHECK
is additive; dropping and re-adding one to widen it is fine.

⚠️ The CI additive gate in `.github/workflows/ci.yml` currently scripts **8 of the 15 contexts** —
`kernel`, `auth`, `reg`, `hr`, `diag`, `notif`, `emr`, `ipd`. `bill`, `lis`, `appt`, `adm`, `pharm`,
`ot` and `radiology` are **outside the gate**, including the money core. If you touch one of those,
add it to the loop in the same change.

## Deletion, correction and effective dating

**Never hard-delete financial or clinical rows** (hard rule 4). The three shapes in use:

| Intent | Shape |
|---|---|
| Retire a master (drug, service, ward, designation) | `Active` boolean, default true. 164 sites use this name — match it. |
| Correct a financial fact | A reversal row pointing at what it reverses. Never an update, never a delete. |
| Supersede a clinical record | A new row with `SupersedesId` at the old one; the old row stays readable. |
| Merge a duplicate identity | `MergedInto` on the loser; every search excludes it. |

**Every query against a soft-deletable table must filter it**, and the filter belongs in one named
place, not copy-pasted per screen — `PatientSearch.Searchable()` is the pattern:
`p.Active && p.MergedInto == null`. A screen that forgets it offers a merged patient for billing.

**Prices are effective-dated** (hard rule 5). Resolve through `RateResolver` by *service date*. A
historical invoice must reproduce its historical price, so never mutate a rate row — supersede it.

## Indexes

An index is not an optimisation to add later. Adding one to a live table costs a migration, a
maintenance window and a review; getting it right in the migration that creates the table costs
nothing.

### The rules

1. **Every foreign-key column gets an index.** Postgres does not create one for you. Without it,
   every parent delete or existence check scans the child.
2. **Every column a list page filters on gets an index** — status, date, branch, counter, ward,
   patient. If a screen has a dropdown over it, it is a filter.
3. **Every uniqueness invariant is a real unique index**, not a service-level check. A
   `SELECT`-then-`INSERT` uniqueness test is a race, and the race is the bug. Unique index plus a
   bounded retry is the house pattern for appointment serials and number series.
4. **Composite index column order is equality first, then range, then sort.** An index on
   `(branch_id, invoice_date)` serves `branch_id = ? AND invoice_date >= ?`; the reverse order does
   not.
5. **Partial index when the predicate is always present.** If every query says `WHERE active`, the
   index should too — it is smaller and stays in the box's limited page cache. In use:
   `IpdDbContext.cs:326` `(department_id, user_id) WHERE active`, `HrDbContext.cs:142`
   `user_ref WHERE user_ref IS NOT NULL`, `EmrDbContext.cs:247` `supersedes_id WHERE supersedes_id
   IS NOT NULL`, and `ix_patient_live` `(branch_id, uhid) WHERE merged_into IS NULL`.
6. **A generated stored column beats normalising at read time.** `reg.patient.phone_digits` is
   `regexp_replace(coalesce(phone,''), '\D', '', 'g')` STORED, and it is indexed — so `01712-345999`
   and `+8801712345999` match the same index. Do the same for any column whose display form differs
   from its search form.
7. **Never fetch-then-filter.** The predicate goes to SQL (ADR-0020 §2). `ToListAsync()` followed by
   a `.Where()` in C# reads the whole table into a 3 GB box.

### Substring search — the rule most often missed

`ILIKE '%term%'` **cannot use a btree index**. A leading wildcard defeats it entirely, so the plan
is a sequential scan of every row, on every keystroke of a type-ahead.

Any column reached by `ILIKE '%…%'` needs a **trigram GIN index**:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX ix_<table>_<col>_trgm ON <schema>.<table> USING gin (<col> gin_trgm_ops);
```

`reg.patient.full_name` has one (`ix_patient_name_trgm`, in `InitReg`). **It is currently the only
one in the product**, and five other substring searches run without support:

| Surface | File |
|---|---|
| Pharmacy product search | `src/Hms.Web/Pages/Pharmacy/Products.cshtml.cs:49` |
| HR employee search | `src/Modules/Hr/Hms.Hr.Screens/Pages/Hr/Employees.cshtml.cs:36` |
| Billing dues filter | `src/Hms.Web/Pages/Billing/Dues.cshtml.cs:62` |
| Refund payer filter | `src/Hms.Web/Pages/Billing/Refund.cshtml.cs:69` |
| Audit search (4 columns incl. `after::text`) | `src/Hms.Web/Pages/Admin/Audit.cshtml.cs:40` |

Known-open, recorded in `docs/specs/0051-engineering-skill-set/notes.md`. **Do not read this list as
permission** — a new substring search ships with its index.

Prefix-only search (`term%`) is different: a plain btree with `varchar_pattern_ops` serves it, and
if a prefix match is all the operator needs, prefer it.

### Before you claim a list screen is fast

```sql
EXPLAIN (ANALYZE, BUFFERS) <the query the screen actually runs>;
```

A `Seq Scan` on a table that grows per patient, per invoice or per sample is a defect on this box.
Test it against realistic volume, not against a seed of forty rows — §14 has the numbers.

## Concurrency, at the schema level

`code-conventions` owns the locking discipline. Two schema-side pieces belong here:

- **`xmin` is the optimistic token.** `e.Property<uint>("xmin").IsRowVersion()` — Postgres maintains
  it on every write, so it is a token nobody can forget to bump. Declared as a shadow property in
  `bill`, `lis`, `pharm` and `ipd`; HR maps it onto an explicit `Version` property instead
  (`HrEntities.Leave.cs:67`, ADR-0015). Both are acceptable — prefer the shadow form for new work,
  since a CLR property invites someone to set it.
- **Idempotency is a column, not a hope.** A POS or order screen mints a `SubmissionToken` per visit
  and the write checks `Submission.ExistingAsync` inside the transaction — so a double-submit or a
  browser retry over a flaky link (§8 N2) returns the first result instead of billing twice. Any new
  screen that takes money or creates a clinical order needs one.

## Checklist for a new table

- [ ] Lives in its module's schema; no FK crosses out of it
- [ ] Money `int`, timestamps `DateTimeOffset` UTC, strings have `HasMaxLength`
- [ ] Every row-level rule is a CHECK constraint, each with a test proving the refusal
- [ ] Retire path chosen: `Active`, reversal, `SupersedesId`, or `MergedInto` — never delete
- [ ] Index on every FK column
- [ ] Index on every filter and sort column the screens use, composite order equality→range→sort
- [ ] Every uniqueness invariant is a unique index
- [ ] Partial index where the query predicate is constant
- [ ] Trigram GIN on every `ILIKE '%…%'` column
- [ ] `xmin` row version if two users can edit the row
- [ ] `SubmissionToken` if the write takes money or creates an order
- [ ] Migration is additive, and its context is in the CI gate loop
- [ ] `EXPLAIN ANALYZE` run against realistic volume, not the seed
