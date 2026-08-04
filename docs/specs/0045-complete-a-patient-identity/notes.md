# 0045 — notes

## Execution log (2026-08-03)

Built as planned: `UpdateAsync` on the service, `/registration/{id}/edit`, links from the card
and the directory. No schema change — every column it writes already existed on `reg.patient`.
No deviations from `plan.md`.

## Verification

**Tests:** 7 new integration cases in `RegistrationTests` — casualty named on the same record
with the same UHID and the unknown flag cleared; no second patient row; the audit event carrying
the **previous** value; the UHID unreachable; an identified patient's name not removable; an
unknown record still saveable while nameless (the ER path is not a one-shot); DOB superseding an
age. **`dotnet test hms-erp.slnx`: 554 passed / 0 failed** (was 547).

**Negative test (G3):** commenting out `patient.UnknownIdentity = false` and deleting the audit
write turned exactly the two tests that assert them red — `An_unknown_casualty_can_be_given_his_
name_on_the_same_record` and `A_correction_records_what_the_value_used_to_be`. Restored, green.

**Live, through the UI** (`scratchpad/emergency-story.py`, 7 cases, 0 failed): unidentified
casualty → unpaid urgent bloods → admission from Emergency → bedside investigation → **identity
completed** → R4 block raised, approved, applied, released → settled, discharged, bed returned.

**Regression:** patient story 25/0 (12/12 roles), spec 0044 probe 4/0, spec 0043 probe 6/0,
`lifecycle-suite --tier t0` GREEN. Guards: ui-tokens, no-hard-deletes, no-native-date OK.

### The traceability guard earned its keep

`check-lifecycle-traceability.sh` went red the moment the new route existed:

```
FAIL   route table has drifted from the [Authorize] attributes:
       /registration/1/edit protected by registration.create is absent from the table
```

A new authorised route that no role-journey exercises is exactly the drift that guard is for.
Registered in `role-journeys.py`; now `187 cases, 175 covered, 12 gaps`.

### A test fixture that broke a neighbour

The first draft named its patient "Rafiqul Bepari", which shares a dmetaphone with spec 0043's
"Rafiqul Islam" fixture at an adjacent age — so `A_name_and_phone_match_together_is_never_
demoted_to_a_shared_phone` got two candidates where it asserts one, and failed. These tests share
one database and dmetaphone encodes the **start** of a name. Renamed, with the reason recorded in
the test. Same class of trap as spec 0043's notes; it will happen again.

## Decisions

- **Permission is `registration.create`, not a new `registration.update`.** Completing an
  identity is the front desk's own job and the Receptionist already holds it; a new permission
  would mean a grant change on every deployment — and grant drift is a known problem on this
  product (spec 0039 notes) — for no separation-of-duties gain.
- **The command has no UHID, branch or creation-stamp fields at all.** Refusing to carry them is
  stronger than validating them: there is no posted field that could reach them (AC4).
- **The audit stores a diff, not the new row.** `{field: {from, to}}` for changed fields only.
  An audit that records only the new value cannot answer "what did it say before?", which is the
  only question anyone asks of a correction.
- **Duplicate detection re-runs on the new name and is non-blocking**, reusing spec 0043's
  classification so a shared household phone does not read as a duplicate here either. Renaming
  `UNKNOWN-14` to a name the hospital already holds is a functional duplicate that merge (US1.3,
  `[S]`) does not yet exist to repair — so it is surfaced, with wording that says these two
  records need merging rather than one of them renamed.

## Two findings from the same emergency run, NOT fixed here

Both were observed live and both are deliberate behaviour rather than defects, so neither was
changed. Recorded because they are the questions an ER will ask.

**1. An unpaid emergency order raises no tube.** Confirmed: an order saved with `PaidNow = 0`
is accepted and recorded, but `DiagnosticsRelease.ReleasePaidOrdersAsync` returns early on
`balance > 0` and the bench sees nothing. This is the §9A.2 seam working as designed — *payment
releases the lab* — and the toast says so ("labels print once the balance is paid").

The workaround the product already provides is the right one for a genuine emergency admission:
**admit first, order from the folio**, which bypasses the gate entirely (proved in the same run —
"the tube IS raised, with nobody paying first"). The gap is the ER patient who is treated and
sent home *without* being admitted: their urgent bloods wait on money.

Whether an ER should be subject to the payment seam at all — a credit/emergency-override that
releases the lab and leaves a due — is a **PM decision**, not an engineering one. Routing it to
`docs/architecture/09-questions-for-pm.md` is the correct next step; it is new scope under
hard rule 2, not a defect against §5 M9.

**2. R4 blocks admission with no emergency override.** `IpdBilling.EnsureNotBlockedAsync` is
called from `/ipd/admit`, so a patient with an applied bill-block cannot be admitted at all —
including into an emergency bed. The designed escape hatch is release-with-supervisor-approval,
which the same run exercised end to end and which works. Whether that round trip is fast enough
at 02:00 is, again, the PM's call. Left exactly as found.

## Still unbuilt in M1

**Patient merge and deactivation** (`[S]`, US1.3). `MergedInto` and `Active` are read by
`PatientSearch.Searchable`, `/registration` and `FindDuplicatesAsync`, and **written by nothing**
— unchanged since spec 0032 first recorded it and spec 0039 deferred WP5.2. This spec makes merge
*less often necessary* (the main source of duplicates was having no way to name a casualty) but
does not implement it. Photo capture (`[S]`) and fingerprint (`[C]`) also remain unbuilt, both by
plan — the registration screen already carries their placeholders.

## Not committed

Everything above is uncommitted — committing is the user's call.
