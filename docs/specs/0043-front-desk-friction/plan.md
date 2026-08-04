# 0043 — plan

Approved 2026-08-03. Three defects, four changes, one migration. Ordered so the schema change
lands before the screen that depends on it.

## Decisions taken before planning

Put to the operator who found the defects; all three answered:

| Fork | Chosen | Rejected |
|---|---|---|
| OPD pre-fill depth | **Suggest, one click to accept** — banner + *Add to bill* | auto-add to cart; banner with no action |
| Phone-only duplicate | **Label the reason** — own heading, not "possible duplicate" | suppress when sex differs; reword only |
| Serial before billing | **Optional, as today** | warn-but-allow; require a serial |

"Suggest, don't auto-add" is the load-bearing one: a charge that appears without an operator
act is a charge nobody chose, and §7 puts the operator in control of money.

## WP1 — Nav grouping (`NavComposer`)

`Compose` groups by `Module`; `NavGroup.Label` reads `Items[0].GroupLabel`. Any module whose
first registry item declares a different label swallows the rest.

Group by **the label itself** — `i.GroupLabel ?? i.Module` — so a group is what the operator
reads, not what the assembly is called. Two modules deliberately sharing a heading (Registration's
New Patient / Patient Directory and Ipd's Help Desk) then merge into one **Front Desk**, and the
IPD screens get their **Indoor (IPD)** heading back.

Order: first appearance in registry order, which keeps Front Desk at the top and leaves every
other group where it is today.

`NavGroup.Module` stays on the record (`_Layout` reads `.Label` only) but is now the label of the
first contributing module — no consumer depends on it.

**Risk:** entitlement is filtered *before* grouping (`modules.Contains`), so merging groups cannot
leak an unentitled item. Verified by reading `Compose`, not assumed.

## WP2 — Duplicate candidates carry their match reason

`FindDuplicatesAsync` returns `DuplicateCandidate(Id, Uhid, FullName, Phone, AgeYears)` — the
caller cannot tell *why* a row came back, so the screen calls everything a duplicate.

- Add `Sex` and a `MatchedOn` discriminator to the record. The SQL already knows: the two
  branches of the `OR` become two boolean projections, so no second query and no new index.
- `NameMatch` (phonetic + age band) → the existing warning, unchanged wording.
- `PhoneOnly` → a separate, calmer block: *"Someone else already uses this number"*, with the
  family-sharing sentence and no assertion of duplication.
- Both stay listed and both stay clickable. Nothing is hidden, per the chosen option.

The `[M]` requirement is presentation-only, so **`Same_phone_flags_duplicate_regardless_of_name`
must keep passing** — if it goes red the rule was changed, which this spec does not do.

`New.cshtml.cs` splits `Duplicates` into the two collections; `New.cshtml` renders whichever are
non-empty. The red **"Not a duplicate — register anyway"** button keeps its meaning: it appears
when a name match exists. A phone-only match alone should not require an override at all.

## WP3 — The doctor master carries a consultation service

`appt.doctor` is `{ Id, Name, Active }`. Add:

```csharp
public long? ConsultationServiceId { get; set; }   // adm.service id; null = not set
```

**A reference, not a price** — hard rule 5. The amount always comes from `RateResolver.ResolveAsync`
at the transaction date, so a fee change re-prices future consultations and never rewrites a
historical invoice.

- Nullable, because 5 doctors already exist and a NOT NULL column would need a fabricated default.
- **No foreign key** — `adm.service` is another module's schema, and ADR-0028 fixed intra-schema
  keys only. Same posture as every other cross-module id in the product.
- Migration is additive (nullable column, no default, no data move) — passes
  `eng/check-additive-migrations.sh`.
- `/admin/people`: the *Add doctor* form gains a **Consultation fee** dropdown listing active
  `OPD`-department services with today's rate shown. The doctor list gains the column, so an
  administrator can see at a glance which doctors are unset.
- `DevSeed` points the seeded doctors at `CON-GEN`, except one specialist at `CON-SPC` so the
  distinction is exercised rather than theoretical.

**Editing an existing doctor** is out of scope for the *Add* form — the page has no doctor edit
handler today and adding one is its own change. Seed covers the demo; the dropdown covers new
doctors. Recorded as a follow-up in `notes.md`.

## WP4 — The OPD counter reads today's appointment

In `OpdModel.LoadAsync`, inside the existing `if (PatientId is { } pid and > 0)` block, after the
unbilled-charges query:

1. Find today's appointment for the patient in a live state (`booked` / `arrived` / `in_chamber`
   — **not** `done`, `cancelled` or `no_show`).
2. Read the doctor from `appt.doctor`; if `ConsultationServiceId` is set, resolve today's rate.
3. Expose `Suggestion { SerialNo, DoctorName, ServiceId, ServiceName, Price }`.

The view renders a banner above the cart. **Add to bill** posts to a new
`OnPostAddSuggestionAsync(long catalogId)` — which is `OnPostAddAsync` with a different name so
the audit trail can tell an accepted suggestion from a manual pick.

Suppressed when the same service is already in the cart, or already appears in `Unbilled` — the
cashier must not be offered a line the patient is already being charged for.

Degrades quietly and in this order: no appointment → no banner. Appointment but no
`ConsultationServiceId` → banner names the doctor and serial, no button (this is also the
"banner only" option, reached automatically for unconfigured doctors). No rate today →
`RateResolutionException` is swallowed exactly as the catalogue loop already does at `:89-95`.

## What is deliberately not built

- **Serial enforcement.** Rejected above.
- **A no-serial warning at the counter** (`[S]` in the spec). It is the same query as WP4 and
  nearly free, but it puts a caution on every legitimate walk-in — the more common case. Better
  judged once the banner has been used for a week.
- **Doctor edit on `/admin/people`.** WP3 note.
- **Backfilling `ConsultationServiceId` for doctors created before this change** beyond the
  demo seed. There are five, all seeded.

## Verification

1. `dotnet build hms-erp.slnx` clean.
2. `dotnet test hms-erp.slnx` — baseline 407 passing. `Same_phone_flags_duplicate_regardless_of_name`
   specifically must still pass (WP2 is presentation-only).
3. New tests: nav labels unique + `"Indoor (IPD)"` present; a phone-only candidate classified
   `PhoneOnly` and a name match classified `NameMatch`. Both negative-tested (G3) — revert the
   fix, watch them go red.
4. Reset `hms`, reseed, then drive by hand as `jashim` → `rasel`: register on a shared phone,
   read the sidebar, issue a serial, open the counter, accept the suggestion.
5. `python3 eng/verify/lifecycle-suite.py --tier t0` green; `bash eng/check-additive-migrations.sh`
   and `eng/check-ui-tokens.sh` OK.

## Traps

- **`dotnet run` in `src/Hms.Web` binds :5034, not :5199** unless `--no-launch-profile` is passed.
- The demo VM does **not** have spec 0041/0042 — the nav fix will change more headings locally
  than it does there, because Nursing Station and Ward Duty exist here.
- `dotnet test` rewrites `eng/spike-artifacts/bangla-sample.pdf`, dirtying the tree.
- `DevSeed` runs only on an empty database; changing seed values requires a reset to observe.
