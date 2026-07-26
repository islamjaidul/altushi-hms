---
name: mvp-scope-check
description: Decide whether a proposed feature belongs in the frozen HMS ERP MVP or must be deferred, and route it correctly. Use before building, designing, or estimating any feature, and whenever someone proposes adding a module, screen, or capability to the MVP.
---

# MVP scope check

MVP scope is **frozen** at PRD §9A.2. The MVP exists to win one customer whose hospital is still **under construction** — it must de-risk the MD's signature, not maximise features.

## In scope — the 8 modules ("Golden Thread")

1. Patient Registration & ID Card (M1)
2. Appointment / Serial & Queue — lite (M3)
3. OPD & Emergency Billing (M4) — incl. discount approval + counter day-close
4. Diagnostic Test Order & Report Delivery (M8) — incl. barcode labels
5. LIS-lite (M9) — manual results only, **no analyzer integration**
6. MD Dashboard & Day-Close view (M22)
7. Admin/Masters/Roles/Approval engine/Audit (M21) — incl. bulk price-list import
8. Notifications (M20) — with simulation mode

## Explicitly deferred

IPD folio & OT · Pharmacy (first module *after* MVP) · Radiology/PACS · Blood Bank · Canteen · full Accounts · HR/Payroll · Consultant & Referral payouts · analyzer/DICOM/biometric integrations · patient portal/app.

## Decision procedure

1. Does it map to one of the 8 modules above? → in scope, build it.
2. Is it required for the **golden thread** to run end-to-end (register → serial → bill → order → barcode → sample → result → verify → report → day-close → dashboard)? → in scope.
3. Otherwise → **out of scope.** Do not build it. Record it as a question to the PM with your reasoning and a recommended default, then continue with in-scope work.

## Two rules that override "it's not in the MVP"

- **Design-for rule (PRD §9 binding):** the patient folio, consultant accruals, and multi-branch questions must be *structurally accommodated* in the MVP data model even though those modules ship later. Retrofitting a folio under live billing is the known competitor failure mode. Designing for them ≠ building them.
- **Money-model rule:** BEFTN payouts, TDS, and VAT (§3.4) are not MVP features, but the money model must not make them painful later (§16 Q13–Q14).

## Anti-patterns

- Silently expanding scope because a feature is "small" or "standard".
- Cutting scope unilaterally — a scope-cut *recommendation* to the PM is welcome; a silent cut is not.
- Building a demo-only path that can't survive real operation. Anything that works only when a presenter drives the keyboard is a defect (§9A.4).
