# Patient lifecycle — the QA reference

One patient's whole journey through Altushi HMS, stage by stage, with every edge case that
touches a built module. This is the document QA works from: a regression run follows it, and
anything not listed here is not being tested.

It exists because module-level tests share a blind spot. Spec 0020's notes put it exactly:
*"Each module's tests exercised that module with data the test itself created. All three defects
live between modules."* Roles are the second axis of the same blind spot — a step performed by a
convenient privileged session proves nothing about the operator who actually performs it. Every
case here therefore names **who does it**.

- **Scope:** the thirteen modules with shipped code (M1–M11, M20–M22, R3). M12–M19 have no code
  and are out of scope until they do.
- **Companion:** `docs/qa/README.md` — how to run, safety tiers, what a production run leaves.
- **Traceability:** `eng/check-lifecycle-traceability.sh` fails the build when a case here has
  no runnable counterpart, or a permission exists with no case.

## Case ids

`LC-<STAGE>-<nn>`. Ids are stable and append-only: never renumber, never reuse. A case that
stops being relevant is struck through, not deleted.

| | | | |
|---|---|---|---|
| `ROLE` day in the life | `REG` registration | `QUE` queue | `FD` front desk |
| `EMR` consult & orders | `DX` diagnostics gate | `BIL` billing & cash | `LAB` laboratory |
| `RAD` radiology | `PHA` pharmacy | `ADM` admission | `NUR` ward nursing |
| `OT` theatre | `DIS` discharge | `EXIT` terminal exits | `BLK` R4 bill-block |
| `XCUT` cross-cutting | | | |

## Coverage legend

| Marker | Meaning |
|---|---|
| `auto` | Asserted by a script in `eng/verify/`, named in the row |
| `ui` | Asserted by a Playwright spec in `eng/verify/ui/tests/` |
| `xunit` | Asserted by a test project under `tests/` |
| `manual` | Judgement or physical step; a human follows the row |
| **`gap`** | **Nothing asserts this.** Every `gap` appears in the register at the end |

## The cast

Twelve seeded roles (`src/Hms.Web/DevSeed.cs`), thirty-eight permissions
(`src/Hms.Web/Perm.cs`), password `Demo#1234`. Separation of duties is real and deliberate —
several cases below exist only to prove a role *cannot* do the adjacent job.

| User | Role | Owns |
|---|---|---|
| `jashim` | Receptionist | registration, queue, front desk, **beds and admissions** (`ipd.manage`) |
| `rasel` | Billing Operator | billing, cash, order creation, **discharge settlement** (`ipd.settle`) |
| `ripon` | Lab Technologist | sample collection, result entry — **cannot verify** |
| `farhana` | Pathologist | result verification **and** radiology reporting — the e-sign |
| `moinul` | Radiology Technician | performs studies — **cannot report** |
| `shahid` | Billing Supervisor | approvals, session close — **has no `ipd.settle`** |
| `nasrin` | Nurse | vitals, charts, ward service posting — **cannot write a note** |
| `chowdhury` | OPD Consultant | prescriptions, test ordering — nothing financial |
| `shaheen` | OT In-charge | theatre schedule and record — **cannot bill** |
| `parvin` | Pharmacist | sale, purchase, stock, own counter session |
| `admin` | Admin | users, audit, masters — **the only role that can reprice** |
| `md` | MD | **the only holder of `dashboard.read`**, plus approvals and audit |

---

## LC-ROLE — a day in the life

For each role: log in, land, reach every granted route, be refused every other route, perform
the core duty, be refused the adjacent one. Twelve roles across sixty-four protected routes.

| ID | Case | By | Coverage |
|---|---|---|---|
| LC-ROLE-01 | Receptionist registers, queues, manages beds — cannot bill | `jashim` | `auto` role-journeys |
| LC-ROLE-02 | Billing Operator bills and settles — cannot prescribe | `rasel` | `auto` role-journeys |
| LC-ROLE-03 | Lab Technologist collects and enters — **cannot verify its own result** | `ripon` | `auto` role-journeys |
| LC-ROLE-04 | Pathologist verifies and signs, lab and imaging alike — cannot bill | `farhana` | `auto` role-journeys |
| LC-ROLE-05 | Billing Supervisor approves — **cannot settle a discharge** | `shahid` | `auto` role-journeys |
| LC-ROLE-06 | Nurse charts and posts ward services — **cannot prescribe** | `nasrin` | `auto` role-journeys |
| LC-ROLE-07 | OPD Consultant prescribes and orders — cannot bill for it | `chowdhury` | `auto` role-journeys |
| LC-ROLE-08 | OT In-charge schedules and records — cannot bill the case | `shaheen` | `auto` role-journeys |
| LC-ROLE-09 | Pharmacist sells, purchases, opens its own counter — cannot prescribe | `parvin` | `auto` role-journeys |
| LC-ROLE-10 | Admin manages users, masters, audit — cannot bill | `admin` | `auto` role-journeys |
| LC-ROLE-11 | MD reads the dashboard and approves — cannot bill | `md` | `auto` role-journeys |
| LC-ROLE-12 | Radiology Technician performs — **cannot write the report** | `moinul` | `auto` role-journeys |
| LC-ROLE-13 | Every role's sidebar equals permissions ∩ entitled modules | all | `gap` |
| LC-ROLE-14 | A permission revoked mid-shift takes effect within 5 min (security stamp) | `admin` | `auto` money-and-controls 8 |

---

## LC-REG — arrival and identity

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-REG-01 | New walk-in registered | UHID issued, patient findable | `jashim` | `auto` golden-thread 1 |
| LC-REG-02 | Found by name | type-ahead returns the patient | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-03 | Found by phone typed as plain digits | matches (spec 0020 gap 1) | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-04 | Found by the tail of the number | matches on last 6 digits | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-05 | Phone typed `+880…`, with dashes or spaces | `phone_digits` normalises, still matches | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-06 | Directory search accepts digits | same rows as type-ahead | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-07 | Near-duplicate name and age | guard fires, needs acknowledgement (edge 23) | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-08 | Duplicate acknowledged | save proceeds, both records exist | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-09 | Patient with **no phone** | registration still completes | `jashim` | `xunit` RegistrationTests |
| LC-REG-10 | Unknown / unconscious patient, no name or age | registerable under a placeholder identity | `jashim` | `xunit` RegistrationTests |
| LC-REG-11 | Age entered instead of date of birth | both accepted, stored consistently — DOB wins over both age columns | `jashim` | `xunit` RegistrationTests |
| LC-REG-12 | Minor with a guardian | guardian captured | `jashim` | `xunit` RegistrationTests |
| LC-REG-13 | UHID is unique under parallel registration | no collision | — | `xunit` NumberSeriesTests |
| LC-REG-14 | Patient-type general vs corporate | type recorded (nothing reads it yet — see the register) | `jashim` | `xunit` RegistrationTests |
| LC-REG-15 | ID card prints, and reprints | print sheet renders | `jashim` | `ui` documents.spec |
| LC-REG-16 | Patient merged after records exist | history from both survives, no orphan money | `admin` | `gap` |
| LC-REG-17 | Returning patient's history is visible | prior visits listed | `jashim` | `auto` lifecycle-thread 9 |
| LC-REG-18 | Infant aged in **months** registers | `8 months` stored as months, not rounded to a year or a false DOB (M1-D1) | `jashim` | `xunit` RegistrationTests |
| LC-REG-19 | Receptionist completes a **no-phone** registration on the screen | the form saves; LC-REG-09 proves the rule, this proves the path | `jashim` | `auto` lifecycle-thread 1 |
| LC-REG-20 | Receptionist completes an **unknown-identity** registration on the screen | the ER path: name blank, box ticked, UHID becomes the name. Returned **HTTP 500** until spec 0032 — see the register | `jashim` | `auto` lifecycle-thread 1 |

---

## LC-QUE — queue and appointments

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-QUE-01 | Serial issued for a doctor's session | number allocated in order | `jashim` | `auto` golden-thread 2 |
| LC-QUE-02 | Queue advances, and a stale second click loses safely | state-guarded UPDATE (edge 28) | `jashim` | `ui` smoke · `xunit` AppointmentQueueTests |
| LC-QUE-03 | Doctor has no session today | refused with a plain message | `jashim` | `gap` |
| LC-QUE-04 | Session is full | refused or overflow handled | `jashim` | `gap` |
| LC-QUE-05 | No-show or cancel, then re-issue | the next patient is queued; the ended serial is never handed out again | `jashim` | `xunit` AppointmentQueueTests |
| LC-QUE-09 | **Cancelling the day's top serial does not block the next one** | re-issue succeeds (M3-D1: it used to fail until midnight) | `jashim` | `xunit` AppointmentQueueTests |
| LC-QUE-10 | The "next serial" the screen offers is the one the patient receives | label and allocator share one arithmetic (M3-D2) | `jashim` | `xunit` AppointmentQueueTests |
| LC-QUE-06 | Lobby display masks names | no full name, no money on the public page | anon | `auto` edge-cases 8 |
| LC-QUE-07 | Parallel serial issuance | no duplicate numbers | — | `xunit` NumberSeriesTests · `xunit` AppointmentQueueTests |
| LC-QUE-08 | **Issuing a serial requires `appointments.create`** | a read-only grant must not be able to issue | `jashim` | `auto` money-and-controls 8 · `xunit` HandlerPermissionTests |

---

## LC-FD — front desk

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-FD-01 | Live estimate includes **unposted** bed-days | estimate = posted + accrued, nothing written | `jashim` | `auto` frontdesk-check |
| LC-FD-02 | Advance subtracted from the estimate | estimate = charges − advance | `jashim` | `auto` frontdesk-check |
| LC-FD-03 | Reading the screen twice changes nothing | genuinely read-only | `jashim` | `auto` frontdesk-check |
| LC-FD-04 | Admitted patient buying at the counter | banner warns, sale still allowed | `rasel` | `auto` lifecycle-thread 5 |
| LC-FD-05 | Free bed availability shown | ward occupancy accurate — admitting takes exactly one bed off that class's Free and leaves Total alone; releasing restores it | `jashim` | `auto` frontdesk-check |
| LC-FD-06 | Today's doctors panel tracks the queue | issuing a serial moves booked and waiting; calling the patient in changes **nothing** (in-chamber still counts as waiting); finishing moves waiting into done | `jashim` | `auto` frontdesk-check |

---

## LC-EMR — consultation, prescription, orders

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-EMR-01 | Nurse records pre-checkup vitals (US5.3) | vitals on the encounter | `nasrin` | `auto` emr-thread 2 |
| LC-EMR-02 | Consultant writes and signs a note (US5.1/5.2) | note finalised | `chowdhury` | `auto` emr-thread 3 |
| LC-EMR-03 | Ordered tests reach the billing counter | nothing re-typed | `chowdhury` → `rasel` | `auto` emr-thread 4 |
| LC-EMR-04 | Longitudinal record reads across modules | one patient, five schemas | `chowdhury` | `auto` emr-thread 5 |
| LC-EMR-05 | A signed prescription is **corrected, never edited** | supersedes, new version | `chowdhury` | `auto` emr-thread 6 |
| LC-EMR-06 | Draft saved and resumed later | draft survives | `chowdhury` | `xunit` EmrTests |
| LC-EMR-07 | Template reuse prefills the note (the *favourite* half is still open) | prefills the note | `chowdhury` | `xunit` EmrTests |
| LC-EMR-08 | Nurse cannot write or sign a note | refused at the handler, not just hidden | `nasrin` | `auto` role-journeys XCUT-03 |
| LC-EMR-09 | Prescription prints | print sheet renders | `chowdhury` | `ui` spec-0024 |

---

## LC-DX — diagnostics order and the payment gate

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-DX-01 | Order creates unbilled charge lines | lines exist, `invoice_id` null | `rasel` | `auto` golden-thread 4 |
| LC-DX-02 | **Payment in full releases the lab** | sample raised on payment | `rasel` | `auto` lifecycle-thread 3 |
| LC-DX-03 | **Partial payment must NOT release the lab** | sample withheld until settled | `rasel` | `auto` money-and-controls 2 |
| LC-DX-04 | Discount above threshold routes to approval | invoice blocked pending decision | `rasel` → `shahid` | `auto` discount-and-dues 1–2 |
| LC-DX-05 | Discount larger than the bill | refused outright | `rasel` | `auto` edge-cases 7 |
| LC-DX-06 | Order for an admitted patient | lands on the folio, no invoice gate | `nasrin` | `auto` ipd-thread 7 |
| LC-DX-07 | Order cancelled after payment | reversal, not deletion | `rasel` | `gap` |
| LC-DX-08 | Order slip and barcode labels print | sheet renders | `rasel` | `ui` documents.spec |

---

## LC-BIL — billing, cash, day-close

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-BIL-01 | Counter session opens with a float | session bound to the operator | `rasel` | `auto` golden-thread 3 |
| LC-BIL-02 | OPD bill created and paid | receipt issued | `rasel` | `auto` lifecycle-thread 2 |
| LC-BIL-03 | `net = gross − discount + tax + rounding_adj` | invoice identity holds | — | `xunit` MoneySpineTests |
| LC-BIL-04 | `Σ receipts + due = realised value` | money spine balances **in every state, reversals included** — the unqualified `= net` form is false once anything is cancelled or refunded (spec 0032, M4-D1) | — | `xunit` MoneySpineTests |
| LC-BIL-05 | **Double-click Save bills once** | one invoice, submission token | `rasel` | `auto` edge-cases 3, lifecycle-thread 10 |
| LC-BIL-06 | Advance collected against a folio | advance applied at settlement | `rasel` | `auto` ipd-thread 4 |
| LC-BIL-07 | Due collected later | due cleared | `rasel` | `auto` discount-and-dues 4 |
| LC-BIL-08 | **Over-collection refused** | paying more than the balance rejected | `rasel` | `auto` discount-and-dues 5 |
| LC-BIL-09 | Refund as a negative receipt, approval-gated | no hard delete | `rasel` → `shahid` | `xunit` MoneySpineTests |
| LC-BIL-10 | Invoice cancelled, never deleted | reversal recorded | `rasel` | `auto` money-and-controls 3 · `xunit` MoneySpineTests |
| LC-BIL-11 | **A price change never alters a historical invoice** | old invoice reproduces its old price | `admin` | `auto` money-and-controls 1 |
| LC-BIL-12 | Effective-dated rate resolves by service date | correct `rate_version_id` on the line | `admin` | `xunit` ApprovalAndRateTests |
| LC-BIL-13 | Day-close variance = counted − expected | shortfall recorded, not blocked | `rasel` | `auto` golden-thread 9 |
| LC-BIL-14 | **A 01:00 Dhaka receipt belongs to the previous business day** | night shift closes correctly (spec 0027) | `rasel` | `xunit` BusinessDayTests |
| LC-BIL-15 | Carry-close approval when a session spans midnight | approval routed | `shahid` | `gap` |
| LC-BIL-16 | Money receipt and day-close statement print | sheets render | `rasel` | `ui` documents.spec |
| LC-BIL-17 | Parallel collection on one invoice | no double-collection | — | `xunit` MoneySpineTests |
| LC-BIL-18 | Whole taka only, no paisa anywhere | integer money end to end | — | `gap` |

---

## LC-LAB — laboratory

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-LAB-01 | Sample collected then received | states advance in order | `ripon` | `auto` golden-thread 5 |
| LC-LAB-02 | Results entered, one abnormal | flagged against the reference band | `ripon` | `auto` golden-thread 6 |
| LC-LAB-03 | Reference bands vary by age and sex | correct band chosen | `ripon` | `gap` |
| LC-LAB-04 | Report is watermarked provisional until verified | watermark present | `ripon` | `ui` spec-0013 |
| LC-LAB-05 | **Pathologist verifies and e-signs** | result final, signer recorded | `farhana` | `auto` golden-thread 7 |
| LC-LAB-06 | **The technologist who entered cannot verify** | four eyes enforced at the handler | `ripon` | `auto` role-journeys XCUT-03 |
| LC-LAB-07 | Rejected sample spawns a recollection | new sample, old one closed | `ripon` | `xunit` LisAndDayCloseTests |
| LC-LAB-08 | Amend after verification, approval-gated | result versioned, not overwritten | `farhana` | `auto` money-and-controls 7 |
| LC-LAB-09 | Report delivered and handover logged | delivery recorded | `jashim` | `auto` golden-thread 8 |
| LC-LAB-10 | Public report-status lookup leaks nothing | neutral answer, no money, no name | anon | `auto` edge-cases 8 |

---

## LC-RAD — radiology

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-RAD-01 | Imaging order paid at the counter | study created | `rasel` | `auto` radiology-thread 1 |
| LC-RAD-02 | Study reaches the right modality worklist **and no other** | routed by modality | `moinul` | `auto` radiology-thread 2 |
| LC-RAD-03 | Technician marks the study performed | state advances | `moinul` | `auto` radiology-thread 3 |
| LC-RAD-04 | **Technician cannot write the report** | refused | `moinul` | `auto` role-journeys LC-ROLE-12 |
| LC-RAD-05 | Reporting consultant writes the report | draft saved | `farhana` | `auto` radiology-thread 4 |
| LC-RAD-06 | Signing makes it final and deliverable | editor refuses further editing | `farhana` | `auto` radiology-thread 5 |
| LC-RAD-07 | Report prints | sheet renders | `farhana` | `ui` spec-0026 |

---

## LC-PHA — pharmacy

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-PHA-01 | Pharmacist opens the pharmacy counter | own session, own custody | `parvin` | `auto` pharmacy-thread 1 |
| LC-PHA-02 | Purchase order raise → approve → order → receive | §11 states in order | `parvin` → `shahid` | `auto` pharmacy-thread 2 |
| LC-PHA-03 | **Sale picks the earliest expiry first (FEFO)** | property asserted, not a fixed batch | `parvin` | `auto` pharmacy-thread 3 |
| LC-PHA-04 | **Expired batch cannot be sold** | refused | `parvin` | `auto` pharmacy-thread 4 |
| LC-PHA-05 | Credit sale to a named patient raises a due | due visible at billing | `parvin` | `auto` pharmacy-thread 5 |
| LC-PHA-06 | Refund restocks **the exact batch sold** | allocation reversed | `parvin` | `auto` pharmacy-thread 6 |
| LC-PHA-07 | Quarantine → return to supplier | stock moves, ledger entries | `parvin` | `auto` pharmacy-thread 7 |
| LC-PHA-08 | Outlet transfer: indent → send FEFO → receive | both sides balance | `parvin` | `auto` pharmacy-thread 8 |
| LC-PHA-09 | Stock count variance → approval → posted | adjustment on the ledger, not an edit | `parvin` → `shahid` | `auto` pharmacy-thread 9 |
| LC-PHA-10 | Ward indent issues FEFO at batch MRP to the folio | charge on the folio | `nasrin` → `parvin` | `auto` ipd-thread 6 |
| LC-PHA-11 | Indent asks for more than the shelf holds | refused or partially issued, never negative | `nasrin` | `auto` edge-cases 4 |
| LC-PHA-12 | Partial return of an indent restocks its exact batch | allocation-accurate | `parvin` | `auto` ipd-thread 8 |
| LC-PHA-13 | Damage → approval-gated write-off | disposed and logged | `parvin` → `shahid` | `auto` pharmacy-full 14 |
| LC-PHA-14 | Staff-pharmacy sale variant is tagged | attributable | `parvin` | `auto` pharmacy-full 17 |
| LC-PHA-15 | Supplier ledger and payment | balance moves | `parvin` | `auto` pharmacy-full 8 |
| LC-PHA-16 | Supplier replacement, not credit | distinct from a return | `parvin` | `auto` pharmacy-full 16 |
| LC-PHA-17 | Reorder shortlist by reorder level | shortlist correct | `parvin` | `auto` pharmacy-full 7 |
| LC-PHA-18 | **Stock can never go negative** | constraint holds under load | — | `xunit` PharmacyStockTests |
| LC-PHA-19 | Pharmacy dashboard tiles | takings, stock value, near expiry | `parvin` | `auto` pharmacy-thread 10 |

---

## LC-ADM — admission

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-ADM-01 | Admission into a free general bed | bed occupied, folio opened | `jashim` | `auto` ipd-thread 2 |
| LC-ADM-02 | **Bed-day catch-up is idempotent** | `UNIQUE(admission_id, on_date)` holds (P18) | — | `auto` ipd-thread 3 |
| LC-ADM-03 | Transfer to a cabin | history keeps the moment | `jashim` | `auto` ipd-thread 10 |
| LC-ADM-04 | Vacated bed goes to Cleaning, then free | ward recovers the bed | `jashim` | `auto` ipd-thread 14 |
| LC-ADM-05 | Reservation confirmed / cancelled | bed held then released | `jashim` | `gap` |
| LC-ADM-06 | Out-of-service bed | excluded from availability | `jashim` | `gap` |
| LC-ADM-07 | Admission when no bed is free | refused with a plain message | `jashim` | `gap` |
| LC-ADM-08 | Ward board and IPD report show the day | census correct | `jashim` | `auto` ipd-thread 13 |

---

## LC-NUR — ward nursing

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-NUR-01 | Nurse posts a ward service | price from the rate plan, her name on the line | `nasrin` | `auto` ipd-thread 5 |
| LC-NUR-02 | MAR dose scheduled then administered | dose recorded | `nasrin` | `auto` emr-thread 7 |
| LC-NUR-03 | **A recorded dose cannot be recorded twice** | control removed after administration | `nasrin` | `auto` emr-thread 7 |
| LC-NUR-04 | Glucose reading charted | value on the diabetic chart | `nasrin` | `auto` emr-thread 7 |
| LC-NUR-05 | Shift handover recorded | receive note stored | `nasrin` | `auto` emr-thread 7 |
| LC-NUR-06 | A missed dose is visible as missed | not silently absent | `nasrin` | `gap` |

---

## LC-OT — operation theatre

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-OT-01 | Case scheduled for an admitted patient | booking created | `shaheen` | `auto` ot-thread 2 |
| LC-OT-02 | **Theatre or surgeon clash refused** (US7.1) | double-booking impossible | `shaheen` | `auto` ot-thread 2 |
| LC-OT-03 | §11 state machine in order | ready → start → complete | `shaheen` | `auto` ot-thread 3 |
| LC-OT-04 | Consumable comes off pharmacy stock onto the folio | one movement, two ledgers | `shaheen` | `auto` ot-thread 4 |
| LC-OT-05 | Completing the case bills it (US7.2) | completion charges posted | `shaheen` | `auto` ot-thread 5 |
| LC-OT-06 | **A completed case cannot be completed again** | idempotent terminal state | `shaheen` | `auto` ot-thread 6 |
| LC-OT-07 | Case appears on the operation register | register lists it by patient | `shaheen` | `auto` ot-thread 7 |
| LC-OT-08 | Case cancelled — the slot is freed | slot freed (asserted); **no completion charge posted** (open) | `shaheen` | `xunit` OtTests |
| LC-OT-09 | Case postponed with a reason | reason required (asserted); **original slot released** (open) | `shaheen` | `xunit` OtTests |
| LC-OT-10 | OT In-charge cannot bill the case | refused | `shaheen` | `auto` role-journeys LC-ROLE-08 |

---

## LC-DIS — discharge

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-DIS-01 | **The gate does not open silently on an unpaid bill** | one-click button absent while money is owed | `rasel` | `auto` lifecycle-thread 7 |
| LC-DIS-02 | Everything collected, then one click discharges | gate opens | `rasel` | `auto` lifecycle-thread 8 |
| LC-DIS-03 | Summary → clearance → draft → invoice with advance applied | full settlement path | `rasel` | `auto` ipd-thread 11 |
| LC-DIS-04 | Discharge **with** a due needs a typed reason | reason lands in tier-2 audit (§3.2) | `rasel` | `auto` money-and-controls 5 |
| LC-DIS-05 | Certificate issued with a sequential number | number allocated | `rasel` | `auto` ipd-thread 12 |
| LC-DIS-06 | Certificate reprint is counted and audited | reprint recorded | `rasel` | `auto` ipd-thread 12 |
| LC-DIS-07 | A settlement **draft** reopens for a late charge; a **confirmed** one never does | draft → open for `ipd.settle`; locked folio refuses reopen, and a post-lock charge needs a supervisor approval | `rasel` → `shahid` | `auto` money-and-controls 6 |
| LC-DIS-08 | **Supervisor cannot settle** | `shahid` lacks `ipd.settle` | `shahid` | `auto` role-journeys LC-ROLE-05 |
| LC-DIS-09 | Post-discharge: certificate, history, return visit all work | records intact | `jashim` | `auto` lifecycle-thread 9 |

---

## LC-EXIT — terminal exits

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-EXIT-01 | **Patient dies with charges on the folio** | the family can still be billed | `rasel` | `auto` edge-cases 1 |
| LC-EXIT-02 | **Patient absconds** | the due survives for §11 follow-up | `rasel` | `auto` edge-cases 2, lifecycle-thread 11 |
| LC-EXIT-03 | Absconded admission is not "discharged" | state stays Absconded | `rasel` | `auto` lifecycle-thread 11 |
| LC-EXIT-04 | A folio settled while blocked is not a life sentence | ward can still release (spec 0021) | `rasel` | `auto` edge-cases 6 |

---

## LC-BLK — R4 bill-block

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-BLK-01 | Block freezes the folio — bars service and OPD | writes refused while blocked | `jashim` | `auto` edge-cases 5, ipd-thread 9 |
| LC-BLK-02 | Release unfreezes it | writes resume | `jashim` | `auto` edge-cases 5, ipd-thread 9 |
| LC-BLK-03 | Block and release are both approval-gated | approval required each way | `shahid` | `auto` money-and-controls 4 |

---

## LC-XCUT — cross-cutting

| ID | Case | Expected | By | Coverage |
|---|---|---|---|---|
| LC-XCUT-01 | **The dashboard belongs to the MD alone** | eleven roles refused, one allowed | all | `auto` role-journeys |
| LC-XCUT-02 | Anonymous surfaces answer without leaking | login, queue, report-status, health only | anon | `auto` role-journeys |
| LC-XCUT-03 | **Hiding a button is not the control** | handler-level POST refused too | `nasrin`, `ripon` | `auto` role-journeys |
| LC-XCUT-04 | Audit is append-only; app role has no DELETE grant | grant absent | — | `xunit` MoneySpineTests |
| LC-XCUT-05 | One business action = one transaction across 14 contexts | no half-write | — | `xunit` ConcurrencyTests |
| LC-XCUT-06 | Module boundaries hold (ADR-0003) | no cross-module reference | — | `xunit` ModuleBoundaryTests |
| LC-XCUT-07 | Entitlement gating hides an unlicensed module | nav and endpoint both refuse | — | `xunit` EntitlementTests |
| LC-XCUT-08 | SMS queued, and resendable | tray shows it | `admin` | `xunit` SmsQueueTests *(e2e half still open)* |
| LC-XCUT-09 | **Power cut mid-transaction leaves no half-write** | recovery clean | — | `xunit` ConcurrencyTests |
| LC-XCUT-10 | **Two operators editing one folio concurrently** | one wins, other told plainly | — | `xunit` ConcurrencyTests |
| LC-XCUT-11 | **Forty operators at once** | §8 N1 response budget holds | — | `gap` |
| LC-XCUT-12 | Asia/Dhaka throughout, no DST, no native date use | guard passes | — | `auto` check-no-native-date.sh |
| LC-XCUT-13 | **Every protected route is loaded by some UI test** | 11 of 64 are never loaded | — | `ui` smoke.spec (ROUTES) |
| LC-XCUT-14 | **A deployment's role grants match the code's grant matrix** | no environment carries permissions the code does not grant | `admin` | `auto` grant-drift |

---

## Gap register

Every `gap` above, with a severity. **High** = money, permissions, audit or a terminal exit is
unproven · **Medium** = a cross-module seam or a real operator habit is unproven · **Low** =
convenience or cosmetic. This register was the input to the remediation specs. Twelve of the thirteen High-severity
gaps were closed by specs **0030** (authorization) and **0031** (coverage) on 2026-07-28; the
one that remains is LC-XCUT-11, and it remains deliberately — see its row.

Spec **0032**'s M1 sweep closed six more on the same day — LC-REG-05, 09, 10, 11, 12 and 14 —
and added LC-REG-18. Two of those six (LC-REG-09, LC-REG-10) were **never gaps**:
`RegistrationTests.No_phone_registration_succeeds` and
`RegistrationTests.Unknown_emergency_registers_without_identity` already asserted them at the
service layer when the register was written, and the triage missed them. What is still unproven
for both is the *page* path — that the ≤ 60-second screen itself accepts a nameless, ageless
emergency admission. Because every `LC-REG` row names a **performer**, a case performed by
`jashim` is a claim about that operator's path and a service-level fact does not discharge it.
Rather than reopen closed ids (they are stable and append-only), the screen path is recorded as
**LC-REG-19** and **LC-REG-20**, both open.

The same sweep found **five more rows marked `gap` that were already asserted**, and removed them
from this register rather than leaving it overstating the hole:

- **LC-EMR-06** ← `EmrTests.Draft_is_resumed_not_duplicated`
- **LC-EMR-07** ← `EmrTests.A_template_applies_its_drug_lines`; only the *favourite* half is open
- **LC-LAB-07** ← `LisAndDayCloseTests.Sample_chain_collect_receive_reject_spawns_child_with_same_tests`
  — new barcode, `RecollectionOf` chain, tests carried over
- **LC-OT-08** ← `OtTests.A_cancelled_case_frees_its_slot`; the slot half only
- **LC-OT-09** ← `OtTests.Cancelling_and_postponing_both_need_a_reason`; the reason half only

And rows marked **covered that were not**. Two cited a script or class that does not assert what
the row claims — LC-FD-05 (`auto` frontdesk-check, which never reads the beds panel) and LC-BIL-09
(`xunit MoneySpineTests`, which had no refund test at all). Three more named a class that does not
exist, found by mechanically resolving every `xunit` citation against `tests/`:

- **LC-BIL-12** cited `RateTests`; the class is `ApprovalAndRateTests`
- **LC-PHA-18** cited `PharmacyTests`; the class is `PharmacyStockTests`
- **LC-XCUT-05** cited "architecture tests" — a category, not a class; it is `ConcurrencyTests`

Every `xunit` citation in this document now resolves to a class that exists. The register drifted
in both directions because
`check-lifecycle-traceability.sh` validates only that a cited **`auto` script file exists**: it
never checked `xunit` class names, never checked that a cited script asserts the case it is
credited with, and nothing at all looks for a false gap.

| ID | Gap | Severity |
|---|---|---|
| LC-XCUT-11 | **No load or concurrency test exists anywhere in the repo.** `docs/architecture/06-deployment.md` §2a says so plainly: the suite *"says nothing about 40 operators at once"*. Spec 0031 added `eng/verify/load-probe.py` as a first cut and deliberately did **not** mark this covered: what forty operators means, what mix of work they do, and what passing looks like on 2 vCPU / 3 GB are architecture questions, raised as **ADR-0024 (Proposed)**. Marking it green off a read-only probe would be the more damaging outcome. | **High** |
| LC-REG-16 | Patient merge after records exist — no orphaned money, no lost history. **Not a test gap: the feature is unbuilt.** `reg.patient_merge` and `patient.merged_into` are read by `PatientSearch.Searchable`, `/registration` and `FindDuplicatesAsync`, and written by nothing in `src/` (verified by grep, spec 0032). The same is true of patient deactivation — `patient.active` has no writer. Both are PRD §5 M1 [S] sub-features and belong to the PM, not to QA. | **Medium** |
| LC-LAB-03 | Age- and sex-specific reference bands. | **Medium** |
| LC-OT-08 / LC-OT-09 | OT cancel and postpone were marked wholly unasserted; both are **partly** asserted (`OtTests.A_cancelled_case_frees_its_slot`, `.Cancelling_and_postponing_both_need_a_reason`). What is genuinely open is narrower: that a cancelled case posts **no completion charge**, and that a postponement **releases the original slot** (spec 0032). | **Low** |
| LC-ADM-05 / LC-ADM-06 / LC-ADM-07 | Reservation, out-of-service bed, and the no-free-bed refusal. | **Medium** |
| LC-DX-07 | Order cancelled after payment. | **Medium** |
| LC-BIL-15 | Carry-close approval for a session spanning midnight. | **Medium** |
| LC-EMR-07 | Draft resume and template reuse were **never gaps** — `EmrTests.Draft_is_resumed_not_duplicated` and `.A_template_applies_its_drug_lines` (spec 0032). Only `AddFavouriteAsync`, the *favourite* half, has no test. | **Low** |
| LC-NUR-06 | A missed dose being visible as missed. | **Medium** |
| LC-XCUT-08 | SMS queue and resend. | **Medium** |
| LC-ROLE-13 | Sidebar equals permissions ∩ entitlements per role (`NavComposer`). | **Medium** |
| LC-QUE-03 / LC-QUE-04 | No session today; session full. **LC-QUE-04 is not merely untested** — `MaxSerials` is displayed and never enforced, and US3.1's AC requires *"capacity limit enforced with waitlist option"*, so this is an unmet [M] acceptance criterion routed to the PM (spec 0032, M3-R1). LC-QUE-05 was closed by `AppointmentQueueTests`. | **Medium** |
| LC-BIL-18 | Whole-taka-only asserted structurally but not end to end. | **Low** |
| LC-XCUT-08 | SMS **queue and resend end to end**. The module's rules are now asserted (`SmsQueueTests` — G19 both ways, edge 24's recorded skip, resend-unchanged, live-vs-simulation), so this is no longer "nothing asserts this"; what is open is the tray showing it, on screen. | **Low** |

### Coverage summary

| | Count |
|---|---|
| Cases in this document | 175 |
| Covered — `auto` / `ui` / `xunit` | 162 (93%) |
| `gap` — nothing asserts it | 13 (7%) |
| of which **High** severity | 1 — LC-XCUT-11, open by decision (ADR-0024) |

Was 130 covered / 39 gaps / 13 High at the first QA pass (2026-07-28). Specs 0030 and 0031
closed twelve of the thirteen High gaps the same day.

Spec **0032**'s module sweep then moved the number in three different ways, and the mix matters
more than the total:

| | |
|---|---|
| Genuinely closed by new tests | LC-REG-05, 11, 12, 14 · LC-REG-18, 19, 20 added and closed · LC-QUE-02, 05, 07 · LC-QUE-09, 10 added and closed · LC-BIL-04, 09, 10 · LC-FD-05 · LC-FD-06 added and closed · LC-XCUT-08 (business-logic half) |
| **Corrected — never gaps** | LC-REG-09, 10 · LC-EMR-06, 07 · LC-LAB-07 · LC-OT-08, 09 |
| **Corrected — never covered** | LC-FD-05 (reopened) · LC-BIL-12 (citation fixed) |
| Newly opened by the sweep | LC-REG-19, 20 — the registration **screen** path |

**Read this number carefully.** It counts rows in this document, not behaviour in the product.
Seven of the rows above moved because the register was wrong, not because anything was built or
tested — and the register was wrong in both directions at once. A percentage derived from a
hand-maintained list measures the list. `check-lifecycle-traceability.sh` guarantees only that
cited **`auto` script files exist**; it does not check `xunit` class names, does not check that a
cited script asserts the case it is credited with, and cannot detect a false gap at all.
Until it does, treat this figure as a working index and not as evidence.

Counts are produced by `eng/check-lifecycle-traceability.sh --stats`, so they cannot drift from
the tables.
