# 0043 — notes

## Execution log (2026-08-03)

Built in plan order: nav → duplicates → doctor fee → OPD suggestion. All four work packages
landed; nothing was deferred except the two items the plan already listed as out of scope.

### Deviations from the plan

1. **Doctor edit was built after all.** `plan.md` WP3 said editing an existing doctor was out of
   scope and the *Add* form would carry the new field alone. That would have made AC3 false for
   the four seeded doctors and for every doctor created before this change — an administrator
   would have had to reach for SQL, which AC3 forbids in as many words. `OnPostDoctorFeeAsync`
   plus an in-row picker on the doctors table closes it. Small handler, no new permission
   (`admin.masters.manage` already fronts the page).

2. **One adjacent defect fixed, not in the spec.** The duplicate-override button posts
   `DuplicatesAcknowledged` and, because a submit button carries exactly one name/value pair,
   posted **no `action`** — so `action == "print"` was false and the ID card silently never
   printed for any patient who tripped the duplicate check. Registering on a shared phone is
   now a *common* path, so leaving it would have looked like a regression this spec caused.
   One line in `OnPostAsync`: acknowledging implies print. Recorded here rather than in `spec.md`,
   which is append-only after approval.

3. **`Ui.SexWord` and `Ui.Money` already existed.** A `SexLabel` helper written on the page model
   was removed in favour of the shell's.

### What the phone-only classification does and does not change

The **rule is untouched**. `FindDuplicatesAsync` returns exactly the same rows for exactly the
same reasons; `Same_phone_flags_duplicate_regardless_of_name` still passes and was deliberately
not edited. What changed is that each row now says *which* branch matched it, and the screen
renders the two differently. A phone-only match still pauses the operator once — it is shown, not
hidden — but under "Someone else already uses this number", with an ordinary confirm button
instead of the red override.

## Verification

**Tests:** `dotnet test hms-erp.slnx` → **513 passed / 0 failed** (Kernel 26, Web 195,
Architecture 74, Integration 217, PrintGolden 1).

New coverage, all negative-tested per G3:

- `NavComposerTests` — four cases on a synthetic registry shaped like the defect.
- `NavRegistryTests` (new file) — properties of the real `ModuleNav`/`HrNav` registries: no
  repeated heading, no declared label lost, ward screens under Indoor, no empty group.
- `RegistrationTests` — four cases: shared household phone classified `PhoneOnly`, phonetic name
  match classified `NameMatch`, both-at-once never demoted, and sex carried to the screen.

**Negative test, done properly.** Reverting `NavComposer.Compose` to group by module put
**6 tests red** across the two projects; restoring it put them green. The nav fix cannot pass
for the wrong reason.

**Live run** (`scratchpad/verify-0043.py`, fresh `hms`, both fixes driven as `jashim` → `rasel`
→ `admin`): **6 cases, 0 failed**, twice consecutively. `lifecycle-suite.py --tier t0` GREEN.
Guards: ui-tokens OK, no-hard-deletes OK, no-native-date OK, lifecycle-traceability OK,
additive-migrations OK against `dotnet ef migrations script --idempotent` for `ApptDbContext`.

### Two things the verification script taught, worth keeping

Both cost a debugging cycle and both are the *fixture's* fault, not the product's — but they are
the shape of trap that makes a green run meaningless:

- **`dmetaphone` encodes the start of a string.** A fixture named `"Jannatul Ferdous <stamp>"` is
  the same name as every previous run's, whatever the stamp — so the shared-phone case tripped
  the name-match branch and read as a failure of the fix. Random syllables have to lead the name.
  (Digits are ignored outright, so a numeric suffix is doubly useless.)
- **A page-wide money assertion passes on somebody else's price.** Searching the whole OPD
  response for `700` matched the catalogue below the cart. The assertion now reads the cart rows
  specifically and requires exactly `[('Doctor Consultation (General)', '700')]` — same class of
  weak assertion spec 0038 kept finding.

## Findings raised, not fixed

- **`check-no-external-hosts.sh` fails on `src/graphify-out/graph.html`**, which loads
  `vis-network` from unpkg. It is untracked output from the `graphify` developer tool, not
  product code, but it sits under `src/` where the guard looks. The guard should exclude it or
  the tool should write elsewhere — otherwise the check is permanently red and stops being read.
- **`/appointments` accepts `DoctorId = 0`.** The select's placeholder option posts `0`, and the
  verification script issued a serial against a doctor that does not exist before the fixture was
  corrected. `data-val-required` is client-side only. Not investigated further — it is an input
  -tier question of the kind spec 0039 WP1 was built for, and belongs in its own spec.
- **The doctors table on `/admin/people` is built from `appt.schedule`, not `appt.doctor`.** A
  doctor with no schedule row is invisible there, including for fee-setting. Pre-existing; the
  add form always writes both, so it only bites data created another way.

## Not committed

Everything above is uncommitted — committing is the user's call.
