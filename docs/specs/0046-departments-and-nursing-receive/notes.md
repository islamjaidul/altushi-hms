# 0046 — Notes

- 2026-08-04 — Spec created with plan pre-approved (demo-day burst, five specs 0046–0050
  planned together). Notes appended as implementation departs from plan.
- Implementation followed the plan. `emr-thread` already asserts "the receive note records
  the handover", so the receive write path has live coverage beyond the station UI.
- Deferred, recorded: a department CRUD screen (departments are seed-managed this round);
  department scoping on `/emr/charts`, `/emr/tasks`, `/ipd/board` (permission-gated as
  before — row-level scoping there is a follow-up spec); `Vitals` still has no respiratory
  rate / GCS fields (entity unchanged, out of scope).
- The bed-transfer semantics called out in the spec risk section held: a transferred patient
  appears as NEW to the receiving department until its nurse receives them — reviewed and
  kept, since the receiving ward genuinely has not accepted the patient yet.
