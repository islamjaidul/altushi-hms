# 0046 — Departments above wards, and the nurse who receives her own patients

- **Status:** Approved
- **Date:** 2026-08-04
- **PRD ref:** §5 M6, §5A.2 R5, §7, §12
- **MVP:** in scope — post-freeze Phase 2; extends the shipped R5 Nursing Station (0041/0042)
- **Requested by:** the owner, ahead of tonight's presentation

## Problem

The R5 station (`/ipd/station`) shows every ward to every `ipd.read` holder, and a newly
admitted patient simply *appears* on the board — no ward-side acknowledgement, no vitals on
arrival. The `emr.receive_note` entity exists (0041) but is unreachable except as a buried form
on the charts screen, and admission-scoped vitals are never written by any screen, so the
station's "latest vitals" column is permanently empty. There is also no notion of a clinical
department: the nurse in charge of Medicine sees ICU's patients and vice versa. A hospital
running multiple units cannot give each nursing team its own board.

## Requirements

- [M] A **Department** master above wards (Medicine, Critical Care, Private Wing, Surgery,
  Gynae & Obstetrics, Paediatrics seeded; existing wards mapped: General Male/Female → Medicine,
  ICU → Critical Care, Cabin Block → Private Wing).
- [M] Nurses can be assigned to departments; an assigned nurse's station shows **only** her
  departments' wards and patients. Unassigned users (matron, admin) keep the whole-hospital view.
- [M] The station separates **New — awaiting receive** (no receive note) from **In care**
  (received), with a visible count of arrivals awaiting receive.
- [M] A **Receive** action on each new patient captures arrival vitals (BP, pulse, temperature,
  SpO2, weight) and the handover record (received from, condition, belongings) in one step,
  attributably, and is refused server-side for a nurse outside the patient's department.
- [S] A second seeded nurse (`rehana`, Critical Care) so the scoping contrast is demonstrable.

## Acceptance criteria

1. Signed in as `nasrin` (Medicine), `/ipd/station` lists only General Male/Female wards and
   their patients; as `rehana`, only ICU; as `admin`, all wards — verified manually and by the
   nursing-thread run.
2. Admit a patient to a Medicine bed → the tile shows **NEW / awaiting receive**; completing
   the Receive form moves it to **In care** and the captured vitals appear on the tile
   (asserting the `emr.vitals` and `emr.receive_note` rows, not just the markup).
3. Posting the Receive form for an ICU patient as `nasrin` is refused with a clear message,
   even if the URL is typed directly.
4. `dotnet test` green; `role-journeys`, `nursing-thread` (13/13) green locally.

## Out of scope

- A department CRUD/admin screen — departments and assignments are seeded this round.
- Department scoping on `/emr/charts`, `/emr/tasks`, `/ipd/board` and other per-admission
  surfaces (they stay permission-gated as today; row-level scoping there is a follow-up).
- Ward scoping for porters/phlebotomists (0042's open QA item) and duty-roster integration.

## Risks / open questions

- A bed transfer moves the patient between departments silently (ward membership is derived
  from the open bed stay). Accepted for now — the receiving department sees them as NEW if no
  receive note exists yet, which is arguably correct behaviour.
- Department membership is read per request (not a claim) — an assignment change takes effect
  on the next page load by design.
