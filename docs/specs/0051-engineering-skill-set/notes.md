# 0051 — Notes

## Drift measured during the audit, deliberately not fixed here

Per the approved plan, this spec delivers skills only. Everything below is recorded so a follow-up
spec can be written without repeating the investigation. Line numbers are as of 2026-08-06 and will
drift; the file and the symptom will not.

### 1. Five substring searches with no supporting index

`ILIKE '%term%'` cannot use a btree index — the leading wildcard forces a sequential scan of the
whole table, on every keystroke of a type-ahead. Exactly one column in the product has the trigram
GIN index that would serve it: `reg.patient.full_name` (`ix_patient_name_trgm`, created in
`20260726124814_InitReg.cs:94`).

| Surface | File | Columns matched |
|---|---|---|
| Pharmacy product search | `src/Hms.Web/Pages/Pharmacy/Products.cshtml.cs:49` | brand, generic |
| HR employee search | `src/Modules/Hr/Hms.Hr.Screens/Pages/Hr/Employees.cshtml.cs:36` | three columns |
| Billing dues filter | `src/Hms.Web/Pages/Billing/Dues.cshtml.cs:62` | payer |
| Refund payer filter | `src/Hms.Web/Pages/Billing/Refund.cshtml.cs:69` | payer |
| Admin audit search | `src/Hms.Web/Pages/Admin/Audit.cshtml.cs:40-43` | `actor_name_snapshot`, `entity`, `action`, **`after::text`** |

The audit one is the most exposed: four `ILIKE`s including a cast of a `jsonb` column to text, over
a table that grows with every Tier-1/2 write and is never pruned. On the §16 box (2 vCPU / 3 GB) at
§14 volumes this is the first search that will become unusable.

Recommended follow-up: one spec, one additive migration per affected schema, `pg_trgm` GIN indexes
(the extension already exists — `InitReg` creates it), plus an `EXPLAIN ANALYZE` at realistic
volume as the acceptance evidence. Consider whether the audit search wants a `tsvector` column
instead of four trigram indexes.

### 2. Seven of fifteen DbContexts are outside the CI additive-migration gate

`.github/workflows/ci.yml` scripts and checks `kernel`, `auth`, `reg`, `hr`, `diag`, `notif`, `emr`
and `ipd`. Not gated: **`bill`**, `lis`, `appt`, `adm`, `pharm`, `ot`, `radiology`. A destructive
migration in the money core would pass CI today. Cheap to fix — each is two more lines in the same
loop — but it is a CI change and belongs in its own small spec.

### 3. Index density varies with no written rationale

`HasIndex` declarations per context: `hr` 57, `ipd` 20, `emr` 17, `bill` 16, `pharm` 8, `kernel` 7,
`diag` 7, `ot` 7, `radiology` 5, `adm` 4, `reg` 3 (plus three hand-written in raw SQL), `lis` 3,
`appt` 3, `notif` 2, `auth` 1. Some of the spread is legitimate — `hr` genuinely has more tables.
Some of it is a module written before anyone thought about the read path. Not actionable as a number;
actionable per screen, via the `EXPLAIN ANALYZE` rule now in `schema-and-indexing`.

### 4. Two factual errors found in `code-conventions`, fixed in this spec

- The guard table claimed `eng/check-fkeys.sh` fails when *"a foreign key crosses a module schema
  boundary"*. It does not — the script checks that screens do not rebind reserved **function** keys
  (F2/F3/F9/F10). The cross-schema-FK rule is real but **unenforced**; the table now says so.
- The row implied the guard set covers the FK rule, which meant an engineer could reasonably
  believe the build would catch a cross-schema foreign key. It would not.

## Enforcement tests agreed for a follow-up spec

Decision taken 2026-08-06: the index and CRUD rules become machine-checkable, with the tests written
alongside the migrations they police rather than in this spec. Three candidates, in value order:

1. **Unindexed substring search.** An `Hms.Architecture.Tests` sweep that finds every
   `EF.Functions.ILike` call site, resolves the entity property, and fails unless the model or a
   migration declares a trigram index on that column. Hardest of the three — needs the call site to
   resolve to a mapped property — but it is the rule with live violations.
2. **Unindexed foreign key.** Walk each `DbContext` model; every FK property must appear as the
   leading column of some index. Purely model-side, no source parsing, and it closes the gap left by
   `check-fkeys.sh` not being what its name suggests.
3. **Soft-delete filter coverage.** Every query against an entity carrying `Active` or `MergedInto`
   must go through the shared predicate. Weakest of the three as a static check; may be better served
   by making `Searchable()`-style helpers the only public way to reach those sets.

Follow `InputGateCoverageTests` for the shape — a reflective sweep plus an explicit allowlist whose
entries are documented decisions.

## Deviations from the plan

- The plan said "rename and update the four referencing files". A fifth edit was needed:
  `code-conventions` carried the two factual errors in §4 above, found while grounding
  `schema-and-indexing`. Corrected in place rather than left for a later spec, because a guard table
  that names a guard which does not exist is worse than no table.
