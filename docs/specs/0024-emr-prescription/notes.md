# 0024 — Notes

## Two defects, both found by testing rather than by reading

**Reopening a signed visit started a second prescription.** The consultation screen called
`OpenDraftAsync` unconditionally, so every return visit to the URL created a fresh draft beside
the signed note. Nothing complained: the new draft was legal, the old note was intact, and the
screen looked normal. The end-to-end thread caught it only because it asserted the *absence* of
the editing form after signing. Fixed by loading the visit's latest non-superseded note and
refusing the write path when it is final.

**A state-guarded UPDATE left EF's tracked entity stale.** `FinaliseAsync` flips the state with
raw SQL — correct, and the pattern the whole codebase uses (ADR-0015). But the context still
held the note with `state = draft`, so a `SaveDraftAsync` later *in the same transaction* read
the stale value and sailed straight past the immutability check. In the web app each request has
its own context, which is exactly why this would have sat undetected until some future screen
did both in one transaction. Both `FinaliseAsync` and `SupersedeAsync` now `ReloadAsync` the
entity after the guard.

That second one generalises: **anywhere the codebase mixes a raw-SQL state guard with a tracked
entity in the same unit of work, the tracked copy is stale.** I swept the existing services for
the shape. `IpdService` has it: `GetAdmissionAsync` returns a tracked entity and every transition
updates by raw SQL, so two transitions in one transaction leave the second reading a stale
`admission.State`.

**It is not currently harmful, and I checked rather than assuming.** Every IPD transition is
guarded in SQL (`WHERE state = …`), and the SQL guard — not the C# read — is what decides. The
history generator drove 671 discharges through initiate → clear → settle → discharge in single
transactions without a wrong outcome. The exposure is a *future* C#-only precondition added to
one of those methods, which would silently read the wrong state. Recorded here rather than fixed
under this spec, because changing M6 belongs in an M6 spec — it is on the list for the next one
that touches IPD.

## Decisions worth recording

- **A prescription cannot be written before the visit is billed**, because the encounter is born
  at the counter. This matches how these hospitals work (payment then chamber) and keeps M5 out
  of the business of creating financial objects. Stated in the spec so it is a decision.
- **Correction is supersession, never editing** — the clinical mirror of hard rule 4. The
  original keeps its text, its signature and its printed form; the correction names what it
  replaces. Raised to the PM as **P22** in case they want an edit window instead.
- **Clinical numbers are stored in tenths as integers.** 37.4 °C has to come back as 37.4, and
  binary floating point is not the tool for a number a nurse typed and a doctor will act on.
- **Only verified results reach the prescriber.** An unverified result is a working number inside
  the lab; showing it on a consultation screen invites acting on it.
- **Templates and favourites belong to the doctor**, not the hospital — one consultant's "URTI
  adult" is not another's.

## Cut, explicitly

[C] allergy/alert flags and [C] ICD-code tagging are not built (spec §Out of scope, matrix rows
marked **cut**). Both are additive later; neither is a Must.

## Follow-ups

- US5.1's three-minute target is **not measured**. Templates, favourites and the one-page layout
  are built for it, but nobody has timed a real consultant. That needs a person, not a script —
  the same gap §9A.4's timed UI tests have always had.
- The consultation screen shows the first 40 catalogue tests as tick-boxes. That is fine at the
  demo's catalogue size and wrong at a real one; it wants the same type-ahead the drug picker
  uses when a customer's catalogue arrives.
- A second doctor opening the same visit gets their own draft (the dedup is per doctor). That is
  deliberate — two consultants can see one patient — but the queue shows only the latest note,
  so a visit with two prescriptions reads as one. Revisit if it ever happens in practice.
- The prescription print carries a registration line only when the doctor exists in the
  reporting-consultant master. A printed BMDC number nobody verified would be worse than none,
  so absent is rendered as absent.
