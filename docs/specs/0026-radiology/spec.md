# 0026 — M10 Radiology & Imaging (Wave 3)

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §5 M10, §5A-10, §11, §12, §13 I10
- **MVP:** in scope — Wave 3 of `11-build-plan-phase2.md`

## Problem

Imaging orders are billed and then vanish into the same lab pipeline as a blood test. A
radiology technician has no worklist: to know what to shoot today they read the lab board, which
is sorted by sample and means nothing to them. US10.1 names the consequence exactly — re-typing
patient names into the machine is "the #1 source of mismatched studies", and today the product
gives them no alternative.

A radiologist has no reporting surface either. The verified-result machinery exists (M9) and the
per-test template engine exists (5A-10, shipped with the MVP admin), but nothing joins them into
the "open study → apply template → edit findings → sign" flow US10.2 asks for.

## Requirements

- [M] **Modality master** (X-ray, CT, MRI, Ultrasound, ECG/Echo) with each imaging test mapped to
  the machine that performs it.
- [M] **Worklist per modality**, fed from paid M8 orders — the technician sees today's studies for
  their machine, by patient, without reading a lab board (US10.1).
- [M] **Study-done marking** by the technician, attributable, with film/consumable usage recorded
  against the study.
- [M] **Template-based report entry**: the exam's parameter template applied, findings and
  impression edited, saved as the order-test's result through the **existing** result engine — one
  result store, not a second one.
- [M] **Signing with e-signature**, reusing M9's verification (same hash, same audit), and an
  **amendment** path identical to M9's.
- [M] An unsigned report **cannot print as final** (US10.2 AC); a signed one becomes deliverable
  through the existing delivery flow.
- [S] **Radiology report print** on the hospital letterhead with the reporting consultant's line.

## Acceptance criteria

1. A paid imaging order appears on the worklist of the modality that performs it, and nowhere
   else; an unpaid one does not appear at all.
2. Marking a study done records who and when, and moves it off the "to shoot" list.
3. Opening the report editor applies the exam template's parameters; findings and impression are
   free text.
4. Saving a report stores it as the order-test's result (M9's store), so the existing delivery,
   amendment and audit paths apply unchanged.
5. An unsigned report prints marked **provisional**; a signed one prints as final and carries the
   verifier's name.
6. Signing makes the order deliverable in the existing `/diagnostics/delivery` flow.
7. Amending a signed report creates v2 and keeps v1 (edge 22), through M9's approval-gated path.

## Out of scope

- **[S] DICOM Modality Worklist feed** and **[S] PACS integration** — the customer owns no
  DICOM-speaking device and no archive; building an untestable protocol integration would be
  fabricating capability. Cut explicitly in the matrix, with the seam (`study.accession_no`)
  reserved so the feed has something to key on later. §13 I10 stays a Phase-3 item.
- **[C] Comparison view with prior studies** — cut; the patient record (spec 0024) already lists
  prior verified results, which covers the common case.
- Film/consumable **stock** deduction: usage is recorded on the study for M12 to consume, but the
  radiology store does not exist yet (M12 owns it). Recording without deducting is stated, not
  silently half-done.

## Risks / open questions

- **Modality mapping is data, not code.** Seeded for the demo's five imaging tests; a customer
  maps their own. If a test is mapped to no modality it appears on an "unassigned" list rather
  than disappearing — a study nobody can see is worse than an untidy list.
- **Validatable today?** The workflow is, on seeded orders. Actual modality integration is
  blocked on hardware and is not claimed.
