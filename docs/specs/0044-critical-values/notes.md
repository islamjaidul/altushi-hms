# 0044 — notes

## Execution log (2026-08-03)

Built in plan order. No deviations from `plan.md`; nothing deferred that the plan did not
already list as out of scope.

## Verification

**Unit:** 34 new cases in `tests/Hms.Web.Tests/CriticalValueTests.cs` — critical-before-abnormal,
inclusive bounds at exactly 7.0 and 20.0, a band with only one critical bound (creatinine), a
band with none (ESR must stay quiet), the shipped templates carrying criticals where they should
and none where they should not, the real 3.1-on-an-adult-male path through `BandFor`, and the
pre-0044 template JSON parsing rather than throwing.

**`dotnet test hms-erp.slnx`: 547 passed / 0 failed** (was 513 before this spec).

**Live, through the UI** (`scratchpad/verify-0044.py`, 4 cases, 0 failed): a haemoglobin of
3.1 g/dL entered by `ripon`, then `farhana` refused a release, then permitted one after ticking
the acknowledgement, then the printed report checked for the words.

**AC5 proved the way it actually matters.** The upgrade was tested by rebooting on the
*existing* database rather than a fresh one — before boot all four templates read
`Version = (none)`, `CriticalLow` absent; after boot, `Version = 1` with the bounds present. A
fresh-database test would have proved nothing about a hospital that already has data.

**AC3 in the data:**

```
 id | version | critical_ack_by |  at   | critical_ack_note
  4 |       1 |               4 | 17:12 | Complete Blood Count: Haemoglobin 3.1 g/dL — CRITICAL LOW (ref 13.0 – 17.0 (male)) | …
```

**Negative test (G3), done properly.** The gate was disabled (`if (false && …)`), the host
rebuilt and rebooted, and the probe run: **5 checks went red**, including "the report was NOT
released". Restored, rebuilt, green again. The gate is a control, not a decoration.

Guards: additive-migrations OK against `dotnet ef migrations script --idempotent` for
`LisDbContext`; ui-tokens, no-hard-deletes, no-native-date, lifecycle-traceability all OK.
`lifecycle-suite --tier t0` GREEN. Spec 0043's probe still 6/0.

## The full patient story caught the change, correctly

`scratchpad/patient-story.py` went red on the LIS verification step the moment this shipped, and
it was **right to**. Its fixture typed `1` into every parameter it did not care about — and a
haematocrit of 1% is a critical value, so the pathologist was refused a one-click release exactly
as designed. The fixture was corrected to clinically plausible values (raised white count,
everything else normal) and now asserts the other half of the rule too: a WBC of 15,200 is High,
**not** critical, and must not demand an acknowledgement. 25 cases, 0 failed, 12/12 roles.

A second fixture lesson worth keeping: the first version of that check searched the whole
verification page for the word "CRITICAL" and failed, because the sidebar worklist lists *other
patients'* orders. A page-wide string search is not an assertion about the order under test.

## What is still missing in M9, deliberately

Recorded so nobody reads this spec as "pathology is finished":

| Gap | PRD tag | Why not here |
|---|---|---|
| Analyzer integration + auto-match, exception queue | `[M]` | Phased to Phase 2 by §9A.4 ("LIS-lite … **No analyzer integration**"). Absent by plan, not by oversight. |
| QC management — control runs, QC log, out-of-control flags | `[S]` | Genuinely unbuilt. Its own spec. |
| Delta check against the patient's previous result | `[S]` | Genuinely unbuilt. Cheap once results are queryable by patient; a good next step. |
| Reflex test rules | `[C]` | Unbuilt. |
| Outsourced / referred-out test tracking | `[C]` | Unbuilt. |
| Per-modality template engine — microbiology C&S grids, histopathology | 5A-10 `Must` | Narrative reporting exists; the structured per-discipline templates do not. Large, and its own body of work. |

Also **not** built, and worth a PM conversation rather than an engineering decision: a
**critical-value call log** — telephoning the ordering clinician with a logged read-back. §5 M9
asks only for acknowledgement, which is what this spec delivers, but a call log is a real
accreditation expectation for a lab and the `[S]` "notification to the ordering clinician" in
this spec's requirements is the seam it would hang from.

## Thresholds are defaults, not clinical authority

Stated in `spec.md`, repeated here because it is the thing most likely to be forgotten: the
critical bounds shipped (Hb 7/20 g/dL, WBC 2,000/30,000, platelets 50,000/1,000,000, Hct 20/60%,
glucose 2.8/22.2 mmol/L, creatinine >5.0 mg/dL) are widely-used defaults so the mechanism is live
and reviewable. **A lab's critical list is part of its own accreditation.** Before any go-live the
hospital's pathologist must review them, and they arrive with the masters import like every other
reference range.

## Two defects found in the same review, not fixed here

Both belong to the diagnostics counter rather than the lab, and both were found by running the
full patient story through the UI:

1. **`/diagnostics/order` shows a Gross that excludes carried charges.** `Gross => Cart.Sum(...)`
   (`Order.cshtml.cs:50`), but `BillingService.CreateInvoiceAsync` bills *every* unbilled charge
   on the encounter — including the test the consultant ordered upstairs. The screen said
   ৳2,050, the invoice was ৳2,450. Because full payment is what releases the lab, the patient
   paid what he was asked and **no tube was raised**. `/billing/opd` gets this right
   (`Gross => CartTotal + UnbilledTotal`, with an Unbilled section); the diagnostics counter does
   not. Recovery works — collecting the balance at `/billing/dues` releases through the same seam.
2. **A consultant's own test order is stranded.** Its charge line is swept onto the counter's
   invoice, so the patient pays for it, but `diag.test_order` keeps `state = ordered` with no
   invoice forever — paid, and never worked.

Both are pathology *data-flow* breaks even though they live outside `lis.*`, which is why they
are recorded here as well as in the review notes.

## Not committed

Everything above is uncommitted — committing is the user's call.
