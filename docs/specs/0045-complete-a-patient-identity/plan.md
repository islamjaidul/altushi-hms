# 0045 — plan

Approved 2026-08-03. One service method, one screen, two links. No schema change — every column
this writes already exists on `reg.patient`.

## WP1 — `RegistrationService.UpdateAsync`

The service can only insert today. Add an update alongside `RegisterAsync`, in the same shape:
takes the caller's transaction, returns the patient, writes its own audit event.

```csharp
public async Task<Patient> UpdateAsync(
    RegDbContext reg, KernelDbContext kernel, UpdatePatientCommand cmd, CancellationToken ct)
```

Rules it enforces, rather than trusting the page:

- **UHID, BranchId, CreatedAt, CreatedBy are never touched.** They are not on the command at all,
  which is stronger than validating them (AC4).
- **A blank name is refused unless the record is still `UnknownIdentity`** — the same asymmetry
  registration already has, for the same reason: the ER path legitimately has no name, ordinary
  correction never does.
- **A real name clears `UnknownIdentity`** and, with it, `AgeEstimated` when a real age or DOB
  arrives. Leaving the flag set after the family has given a name would keep the record looking
  like a casualty forever.
- **The audit event carries the diff**, not the new row: `{field: {from, to}}` for the fields
  that actually changed. An audit that records only the new value cannot answer "what did it say
  before?", which is the only question anyone asks of a correction (hard rule 4, append-only).
- Nothing is deleted and no history is rewritten — the previous values live in the audit event.

`AuditWriter` is already injected into the service for `RegisterAsync`; the entity and action
names follow it (`reg.patient` / `update`).

## WP2 — `/registration/{id}/edit`

A page model that mirrors `New.cshtml.cs` deliberately — same field names, same `ParseAge`, same
`NormalizePhone`, same bounds attributes — so an operator who can use one can use the other, and
so the two cannot drift on what a valid age is. `NewModel.ParseAge` and `NormalizePhone` are
already `public static`; reuse them rather than copying.

- Policy: `perm:registration.create`. Completing an identity is the front desk's job and the
  Receptionist already holds it; inventing `registration.update` would mean a grant change on
  every deployment for no separation-of-duties gain. Recorded as a decision, not an oversight.
- The UHID renders as text, never as an input.
- An unknown record opens with a visible banner saying so, because that is the case this exists
  for and the operator should see they are completing rather than correcting.
- On save: `Toast`, redirect to the card. The card is where the operator was going anyway — the
  name has changed, so the card is reprinted.
- **Duplicate detection re-runs** on the new name (`[S]`). Shown, never blocking — same
  non-blocking contract as registration (edge 23), and reusing the classification spec 0043 added
  so a shared household phone does not read as a duplicate here either.

## WP3 — Getting to it

Two links, both where the operator already is:

- **The patient card** (`/registration/{id}/card`) — "Correct these details".
- **The patient directory** row — an edit action next to the existing ones.

No new nav entry: this is reached from a patient, never from a menu.

## WP4 — Tests

- Unknown → named: UHID unchanged, `UnknownIdentity` false, findable by the new name, **same id**
  (AC1, AC2).
- The audit event exists and names the changed fields with their previous values (AC3).
- A posted `Uhid` field is ignored (AC4).
- Blank name refused on an identified patient; permitted on an unknown one (AC5).
- Age parsing rejects what registration rejects — one shared implementation, so one test that
  they agree.
- **Negative (G3):** remove the `UnknownIdentity` clear → the "unknown becomes known" test fails;
  remove the audit write → the audit test fails.

## Traps

- **`FullName` must stay nullable with no `[Required]`**, exactly as `New.cshtml.cs` documents at
  length: a non-nullable string carries MVC's implicit required rule, which would refuse the ER
  path at the input gate before the handler's conditional rule could allow it. This page has the
  same asymmetry and will hit the same wall.
- **`PhoneDigits` is a generated column** — never assign it; the database maintains it.
- Sex and age feed the lab's reference-band matching (§5 M9). Correcting them changes how
  *future* results are flagged; already-stored results keep the band they were judged by (§7 U12,
  spec 0044 AC4). That is correct and must not be "fixed".
- `dotnet run` in `src/Hms.Web` binds :5034 without `--no-launch-profile`.

## Deliberately not doing

- Merge and deactivation (`[S]`, US1.3) — unchanged, still deferred.
- An edit history *screen*. The audit trail holds it and `/admin/audit` already searches it.
- Editing allergies-only from the clinical screens; this page carries the field, and a clinical
  surface for it is spec 0042's territory.
