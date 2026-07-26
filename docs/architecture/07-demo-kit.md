# 07 — Demo Kit

- **Status:** Draft for PM review · **Date:** 2026-07-26 · **Spec:** `docs/specs/0003-mvp-architecture/`
- Purpose: the sales demo is a first-class product artifact (§9A). A failed demo loses the customer (brief edge cases 1–10). Everything here runs fully offline on the presenter's laptop or the site VM.

## 1. Seed dataset — "Altushi General Hospital" (fictional, Sylhet-flavoured)

Realism matters (the reference design already demonstrates the register-level look): Bangladeshi names, Sylhet localities, plausible prices in ৳, real TATs. All records carry `seed_tag` so **demo data is separable from real data by one flag** (edge 4) — go-live wipe = delete-by-tag migration (the one sanctioned bulk delete, executed *before* production use begins, never after).

| Master | Contents |
|---|---|
| Departments | Pathology, Biochemistry, Hematology, Imaging, Medicine, Surgery, Gynae, Paediatrics, ENT, Cardiology |
| Doctors | ~25 with specialties, rooms, schedules, serial capacity; 3 marked reporting consultants (edge 34); several `provisional` (edge 11 demo) |
| Test catalog | **~200 tests** with realistic prices & TATs (CBC ৳400/4h, RBS ৳150/1h, Lipid profile ৳1,200/24h, X-ray chest ৳500, USG whole abdomen ৳1,500 …), reference ranges banded by age/sex, sample types |
| Rate plans | standard + one corporate (e.g., "Rose Garments Ltd.") + one health package — **with a visible price-change history** (proves effective-dating live, edge 13) |
| Beds | 60 beds/cabins across 4 wards incl. several `not_yet_available` (edge 14 — the construction story) |
| Users | 2+ per role (9 roles), memorable names (the reference's cast: reception Jashim, billing Rasel, lab Ripon, pathologist Dr. Farhana …), passwords in the demo card; approver roles 2FA-enrolled with printed codes |
| Referrers | ~15 doctors/agents with territories |
| SMS templates | registration, report-ready — one in Bangla (edge 9 lives in the demo script) |

**Seeded history (edge 4 — no empty dashboard, ever):** ~90 days of generated transactions — ~14k invoices, payments with dues and collections, discounts (approved and rejected), day-closes with occasional small variances, samples through the full pipeline, delivered reports incl. one amended (edge 22), one merged duplicate patient (edge 23), one unknown-emergency registration (edge 25). Volumes follow §14 typical/day with realistic hour-of-day shape (08:00–13:00 + evening peaks) so every chart, KPI tile and register looks like a living hospital.

## 2. One-command reset (edge 5)

`./demo-reset.sh [instanceName]` → stops app connections, restores the golden Postgres snapshot (template-database copy or volume snapshot — fastest wins in testing), clears job/outbox queues, resets number series to the snapshot state, restarts app. **Target < 30 s** (estimate; the script self-times and reports). The golden snapshot is versioned with the app image — reset is always to a *known-good, tested* state. `-p demoA|demoB` gives two isolated same-day instances (edge 6).

## 3. Offline demo checklist (run before every presentation)

1. Airplane-mode test: pull network, full golden thread passes (edge 1 — CI also runs an image-level "no external hosts" check).
2. `demo-reset.sh` fresh; log in as each demo persona once.
3. Printer test page **and** printer-unplugged PDF fallback rehearsal (edge 2).
4. SMS simulation tray visible and empty (edge 3).
5. Battery + a charged spare laptop with the same images (hardware redundancy is part of the kit).
6. Restore-drill dry run ready (`edge 8` script pointed at scratch instance).
7. Bangla footer/SMS sample renders and prints (edge 9).
8. Demo card printed: personas, passwords, F-keys, the 20-minute script.

## 4. The 20-minute golden-thread runbook (§9A.2 seam, §9A.4 criteria)

| Min | Beat | What the MD sees |
|---|---|---|
| 0–2 | MD dashboard, *populated* (90 days history) | "This is your hospital running" — F2 fear answered before it's voiced |
| 2–4 | **Their receptionist** registers a walk-in, keyboard-only, ≤ 60 s; ID card prints (or PDF preview) | F3: "my staff can run it" — the presenter narrates, never touches the keyboard (edge 10) |
| 4–6 | Serial issued; doctor's queue shows the patient | the joins, not the modules |
| 6–9 | Diagnostic invoice: 3 tests type-ahead, TAT promise shown, **discount above threshold → approval request → supervisor approves on second screen** | F2: money control, live |
| 9–11 | Pay → **barcode labels print** · report-ready SMS shown in simulation tray | the most tangible "real software" moment (§9A.2) |
| 11–14 | Lab: scan → collect → receive → result entry (H/L auto-flags) → pathologist verify with e-sign | fulfilment half of the seam |
| 14–15 | Report delivered (version-logged); print + PDF fallback shown; Bangla footer visible | edge 2/9 answered on request |
| 15–17 | **Counter day-close**: counted cash, small variance recorded, statement prints | F2 closed |
| 17–19 | Back to MD dashboard: today's thread visible to the taka, discount attributed by name; drill to register | "every taka lands here" — the closing screen the MD remembers |
| 19–20 | Construction pitch: bulk price-list import demo + provisional beds board + go-live checklist | F1: "start configuring your hospital this week" — the signature ask |

Contingency beats (rehearsed, not improvised): power-cut recovery (edge 7 — pull the plug, relaunch, invoice intact), live restore (edge 8), second-meeting instance swap (edge 6).

## 5. Config-during-construction kit (the F1 lock-in, §9A.1)

The leave-behind: import templates (test catalog/price list, beds, doctors, users) + the validation-error round-trip flow (ADR-0010) + provisional-record dashboard + go-live checklist report. Sales objective per §9A.4: leave with their real price list committed to load.
