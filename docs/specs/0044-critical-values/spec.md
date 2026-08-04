# 0044 — A haemoglobin of 3.1 and one of 11.9 both print "L"

- **Status:** Done
- **Date:** 2026-08-03
- **PRD ref:** §5 M9 (sub-feature 5, US9.3), §5 M10, §7 U12
- **MVP:** in scope — closes an unmet `[M]` acceptance criterion in a shipped module
- **Found by:** pathology data-flow review, 2026-08-03

## Problem

The lab pipeline works end to end — order, payment, tube, collection, accession, result entry,
verification, e-signature, report, delivery — and the rejection/recollection flow is properly
built. What is missing is the part that exists to stop someone dying.

**§5 M9 requires, as `[M]`:** *"Manual result entry with reference ranges by age/sex; **abnormal
& critical flags**"*. **US9.3's acceptance criterion:** *"**Critical values require explicit
acknowledgment**; verification stamps name+time and releases the report to delivery & SMS."*

Neither is implemented. `ResultTemplates.Flag` has exactly three outcomes:

```csharp
if (value < band.Low)  return "L";
if (value > band.High) return "H";
return "";
```

`ReferenceBand` carries `Low` and `High` and nothing else. So:

- A haemoglobin of **3.1 g/dL** (transfuse now) and one of **11.9** (mild anaemia, review in
  clinic) are both rendered `L`, in the same colour, on the same row.
- A platelet count of **9,000** and one of **140,000** are both `L`.
- A random blood sugar of **1.8 mmol/L** — a patient who will seize — is `L`, like a 3.9.
- Verification is a single POST with no acknowledgement step of any kind
  (`Verify.cshtml.cs:93-143`), so a pathologist can one-click release a panic result exactly as
  fast as a normal one. US9.3's whole point is that these two should not feel the same.

The pathologist is the last human between a critical result and a patient who goes home. Today
the screen gives them nothing to notice.

This is a **defect against a written `[M]` requirement**, not new scope.

## Requirements

- [M] **A reference band can carry critical bounds** in addition to its normal ones, banded by
      age and sex exactly as the normal range already is.
- [M] **A critical value is flagged distinctly from a merely abnormal one** — at result entry, on
      the verification screen, on the worklist, and on the printed report.
- [M] **Verification is refused while any critical value on the order is unacknowledged.** The
      acknowledgement is explicit, names the values being acknowledged, and cannot be given by
      simply pressing the same button twice.
- [M] **The acknowledgement is attributable and permanent** — who, when, and what was
      acknowledged, retained with the result version (hard rule 4; consistent with the e-sign
      hash covering the values).
- [M] **An existing database picks the critical bands up** without a manual reload — the flag a
      value was judged by is stored with the value (§7 U12), so only *new* results are affected;
      already-released reports keep the flag they were signed with.
- [S] The critical value reaches the *ordering* clinician, not only the lab — the notification
      path that already exists for "report ready".

## Non-goals

- **Analyzer integration** (§5 M9 sub-feature 4). Deliberately phased to Phase 2 by §9A.4, which
  specifies "LIS-lite … **manual** result entry … **No analyzer integration**". Absent by plan.
- **QC management** and **delta check** — both `[S]`, both genuinely unbuilt, both out of scope
  here. Recorded in `notes.md` as remaining gaps rather than silently folded in.
- **Reflex test rules** and **outsourced test tracking** — `[C]`, unbuilt.
- **Microbiology culture & sensitivity grids** and **histopathology narrative templates** —
  these belong to 5A-10's per-modality template engine, which is its own body of work.
- Changing who may verify, or the e-signature mechanism.
- Setting *clinically authoritative* thresholds. The values this spec ships are widely-used
  defaults, labelled as such, for a hospital's own pathologist to review — a lab's critical list
  is its own accredited policy, not a vendor's.

## Acceptance criteria

**AC1** — A haemoglobin of 3.1 renders a *critical* flag; one of 11.9 renders a *low* flag; the
two are visually distinct at entry, at verification, and on the report.
**AC2** — Verifying an order carrying an unacknowledged critical value is **refused**, with a
message naming the parameter and its value.
**AC3** — After acknowledgement, the same verification succeeds, and the result row records the
acknowledging user, the time, and the values acknowledged.
**AC4** — A result verified *before* this spec keeps the flags it was signed with; nothing
re-flags a released report.
**AC5** — A database seeded before this spec gains the critical bands on its next boot.
**AC6** — `dotnet test` green, negative-tested: removing the critical gate makes AC2's test fail.

## Notes

The pathology flow was reviewed as a whole. What is built is in good shape: rejection spawns a
recollection child on the same tests, amendment keeps both versions behind a supervisor approval,
the verifier is a first-class signatory, reference bands are genuinely age- and sex-matched
most-specific-first, and TAT is tracked per stage. This spec fixes the one place where the
requirement is written down and the code does not meet it.

Two defects found in the same review are **not** fixed here because they belong to the
diagnostics counter rather than the lab, and are recorded in
`docs/specs/0043-front-desk-friction/notes.md` and this spec's `notes.md`: `/diagnostics/order`
displays a Gross that excludes carried charges (so the operator collects less than the invoice
and no tube is raised), and a consultant's own test order is left stranded in `ordered` when its
charge is swept onto the counter's invoice.
