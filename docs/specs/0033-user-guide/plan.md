# 0033 — Plan

## Approved: 2026-07-29


## Context

The user is handing the HMS ERP to hospital computer operators and wants a `user-guide.md` in the repo root: how to use the software, organized **by role**. They also asked to verify and clearly explain whether patient charges (pathology test, doctor fee, medicine, ward/cabin bill) automatically flow into "the accounts module."

User decisions (asked and answered): **quick-reference handbook** depth (numbered daily-task steps per role, not field-by-field), **English only**, **include demo logins with a go-live warning**.

The codebase was explored by three agents (roles/permissions, money flow, screen inventory) — all claims below are file-verified.

## The verified answer to the "accounts" question (goes in the guide verbatim, in plain words)

**There is no Accounts module.** M15 Accounts & Finance is unbuilt (`docs/qa/module-coverage.md`), as are M12/M13/M14/M16/M17/M18/M19. What exists is the **billing spine**, and the guide must describe it honestly:

- Every charge is a row in `bill.charge_line`, parented to either an **encounter** (outdoor: one per patient per business day **per counter**) or an **IPD folio** (indoor). Invoicing sweeps all unbilled lines on that one parent (`BillingService.CreateInvoiceAsync` / `CreateFolioInvoiceAsync`). So an outdoor patient gets **separate invoices** at the OPD counter, diagnostics counter, and pharmacy (pharmacy forces its own `PHARM` encounter) — there is no single consolidated outdoor bill.
- **AUTOMATIC** charges: test orders (diagnostics + radiology) post charge lines at order time — including doctor-ordered tests from EMR, which **auto-appear at the billing counter** ("Charges raised elsewhere are included automatically"); IPD admission fee + package; **bed-days** (lazy catch-up whenever the folio is opened or settled — no nightly job); inpatient lab/imaging orders; **OT operation + surgeon/anaesthetist/assistant fees on case completion** (same transaction as the state change); service-charge % at settlement prep; FEFO batch pricing on all medicine.
- **MANUAL** charges: OPD consultation fee (cashier adds CON-GEN/CON-SPC to the cart — the only doctor fee that is manual outdoors); IPD doctor visit + oxygen/nursing/services (picked on the folio, price always server-resolved); pharmacy cart (no prescription auto-populate — pharmacist re-picks); ward medicine indent request (issue pricing is automatic).
- **Pay-before-sample:** the lab/radiology only sees an order when it is **fully paid** (part-paid = no tube; paying the balance at Due Collection releases it). Indoor orders skip the gate — the money is already on the folio.
- **The IPD folio is the real consolidator** (inpatients only): bed, OT, indoor lab/imaging, indents, services → one settlement invoice; advances applied at settlement; excess advance returned from the drawer; folio locks. Outdoor invoices during the stay are **listed** at discharge with Collect links, never absorbed.
- **Aggregation:** receipts → counter session → **day-close** (`DayCloseService`; variance recorded, never blocking) → `bill.day_close_summary` → `bill.v_dashboard_day` (the seam a future M15 will consume, per migration comment) → MD dashboard, `/billing/reports`, printed `/billing/statement/{id}` ("the paper the counter hands the accounts desk"). No ledger/journal/voucher anywhere; the only "ledger" is the pharmacy **supplier payables** ledger. M17 consultant payout: unbuilt — only attribution (`charge_line.DoctorId`, `ot.team.AmountPosted`) is recorded for it.

Guide phrasing: *"Charges collect automatically on the patient's bill (encounter or folio) and every taka reaches the day-close statement and reports automatically. A full double-entry Accounts module is future scope — today the accounts desk works from the printed day-close statement and the Collection & Income reports."*

## Deliverables

### 1. Spec `docs/specs/0033-user-guide/` (hard rule 0 — spec first)

- `spec.md` — what/why: operator handoff handbook; AC: all 12 roles covered, all 14 built modules covered, money-flow section matches code behaviour, demo logins with go-live warning, root location. Cite PRD §4 (personas), §7 (UX), §6.2–6.6 (journeys). Status `Approved` → `In Progress` → `Done`.
- `plan.md` — this plan, archived on approval (before implementation starts).
- `tasks.md` — checklist per guide section.
- Add index row to `docs/specs/README.md` (newest last).

### 2. `user-guide.md` at repo root (~700–900 lines)

Structure:

1. **About this system** — what it is; 14 built modules and the 8 not-yet-built (name Accounts explicitly); demo site + local run pointer; **demo logins table** (12 users, password `Demo#1234`) with go-live warning (accounts get deactivated/repassworded at go-live per RUNBOOK §9).
2. **Getting started (every role)** — login (`/login`; 5 failed attempts = 5-min lock; 15-min idle logout); home screen = up to 6 big action cards; sidebar shows only what your role may do ("a missing menu item means permission, not a bug"); search-first UI (2–3 letters, patient by name/UHID/phone); barcode scanning where offered; F-key hint strip; green/red toasts; printing = the Print bar on the same screen (A4 or thermal receipt).
3. **How the money works** — the section answering the user's question (content above), with: the outdoor vs indoor picture, an **AUTOMATIC vs MANUAL table** using the user's own example (doctor fee, lab test, medicine, ward/cabin bill), the counter-session rule ("no money moves without an open counter"), day-close chain, and the honest "no Accounts module yet" statement.
4. **Role guides** ×12 — for each: who you are (persona name + demo user), your menu, **start of shift**, **daily tasks as numbered step sequences**, **end of shift**, when to call the supervisor (approvals). Roles in operational order:
   - **Receptionist** (`jashim`) — register (≤60s screen, age/DOB one box, duplicate warning + "register anyway", identity-unknown for unconscious ER cases, welcome SMS), print/reissue ID card, patient directory, issue serials + run queue (serial never reissued; SMS with serial), IPD admit/reserve, ward board bed states, help-desk enquiries (read-only estimate).
   - **Billing Operator** (`rasel`) — open counter with float (ER counter ⇒ ER bills automatically); OPD invoice (cart + auto-swept charges from doctor/other counters; discount needs reason, >৳200 goes to approval; split tender; due-blocked patients refused; warning if patient admitted); diagnostic order (referrer, promised delivery time, **full payment releases the lab + prints labels**); due collection (collecting a test balance releases the order); refund & cancel (cancel = unpaid only; refund needs reason + tender + approval; Execute after approval); report delivery ("Collect due first" — no deliver-anyway); IPD settlement at discharge steps 3–4; day-close (counted cash, variance recorded) → print statement; collection reports.
   - **Billing Supervisor** (`shahid`) — approvals inbox (discount/refund/reopen/carry-close/late-post/block/release/write-off/PO); everything the operator does except opening a session (**no `billing.session.open`** — decides, doesn't hold a drawer); collection reports.
   - **Pharmacist** (`parvin`) — open counter (**gotcha: after opening you land on a "no access" page — go to Pharmacy Sale in the menu**); POS (brand/generic search, FEFO automatic, expired stock invisible, credit needs a named patient, staff sale, admitted-patient warning → indent instead); indoor issue queue (issue FEFO to folio); stock & expiry (quarantine/return/write-off approval/dispose; stock count); purchases + GRN (batch no + expiry mandatory); transfers; suppliers & payables ledger; reports/dashboard; own day-close.
   - **Lab Technologist** (`ripon`) — work board six columns; collect (scan tube) → receive; reject & re-collect (reason; new label prints); result entry (Enter advances; ranges/flags computed server-side; entered result corrected only by amendment); cannot verify.
   - **Pathologist** (`farhana`) — verification queue (pick reporting consultant signature, verify = e-sign + report-ready SMS); amend released reports (reason; v2, v1 kept); radiology report writing/signing.
   - **Radiology Technician** (`moinul`) — modality worklist (only paid orders appear); mark study done (film size/count); machines & mapping (unmapped test = appears on no worklist).
   - **OPD Consultant** (`chowdhury`) — consultation queue (= the paid list); consult screen (note, drug rows, **order tests — they bill automatically**, templates/favourites); Finalise signs — corrections only after that; printable prescription; patient record; OT board/register read.
   - **Nurse** (`nasrin`) — pre-checkup vitals; ward board; **post folio services (never touches a price)**; raise medicine/investigation indents; nursing charts (MAR schedule/administer, diabetic chart, receive note — attributable, uneditable); ward indents ledger.
   - **OT In-charge** (`shaheen`) — schedule (indoor→folio vs day-case→counter; clash refused); case: sent-for → start → **complete = billed in the same action** (findings, anaesthesia; consumables issued to the bill); postpone/cancel with reason; register; theatres.
   - **Admin** (`admin`) — users & roles (deactivate never delete; permission matrix live within ~5 min); price list (**reprice = new version from a future date; past dates refused**; provisional ৳0 items; unpriced count = go-live checklist); CSV import (partial commit); doctors/referrers/consultants; report templates (bands frozen per result); SMS templates (+placeholder validation, resend); audit viewer; SMS tray. Note: Admin **cannot** open the patient directory (no `registration.read`) or bill — by design.
   - **MD** (`md`) — dashboard (income/collected/due/discount, variance by operator, discount register, consultant ranking, 12-day trend, yesterday digest; reversed invoices excluded from income); tier-2 approvals; audit; read-only ward/pharmacy views.
5. **Reference** — approval matrix (request type → tier-0 limit → tier-1 → tier-2, 10-min escalation); the **printing map** table (13 documents → screen → button); public displays (`/public/queue` lobby TV, `/public/report-status` — no login, masked names, no amounts); SMS events (registration/appointment/report-ready; simulated until a gateway exists — the tray is the log); **troubleshooting** ("menu item missing" = permission; locked out 5 min; idle logout; "counter already has an open session" overnight = supervisor carry-close **has no screen yet — an admin/engineer must intervene**; provisional watermark meaning; IPD menu items appear under a "Front Desk" heading).

Tone rules: simple English, short sentences, numbered steps, name buttons exactly as on screen (agent 3 captured exact labels), ৳ for money, refer to screens by menu label + URL. Reuse `Index.Describe()` one-liners (src/Hms.Web/Pages/Index.cshtml.cs:17) as section blurbs.

## Files to create/edit

| File | Action |
|---|---|
| `docs/specs/0033-user-guide/spec.md`, `plan.md`, `tasks.md` | create |
| `docs/specs/README.md` | append index row |
| `user-guide.md` (root) | create |

No code changes. No PRD changes (no changelog bump needed).

## Verification

1. **Route/label accuracy**: every URL and menu label in the guide greps against `src/Hms.Web/ModuleNav.cs` and `eng/verify/role-journeys.py` ROUTES; demo usernames/roles against `src/Hms.Web/DevSeed.cs`.
2. **Money-flow claims**: each AUTOMATIC/MANUAL claim is already file-cited from exploration (`BillingService.cs`, `IpdBilling.cs`, `OtBilling.cs`, `DiagnosticsRelease.cs`, `EmrOrdering.cs`, `PharmacySale.cs`, `DayCloseService.cs`); spot-check any claim reworded during writing.
3. **Spec compliance**: run the `spec-auditor` agent after closing spec 0033 (checks archived plan, index row, status header).
4. Optional live check: the app runs locally (`docker start hms-dev-db` + `dotnet run` in `src/Hms.Web`) if any screen behaviour needs eyeballing — not expected to be necessary.
