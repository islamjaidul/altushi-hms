# 0041 — Nursing Station (ward nursing console)

- **Status:** Done (build + local verification; deploy rides the next ERP image — `notes.md` §6)
- **Date:** 2026-08-03
- **PRD ref:** §5 M5/M6/M16, §5A-7, §5A.2 R5 (new), §7, §12 nurse row
- **MVP:** in scope (Phase-2 scope release; composition over M5/M6/M16, not a new module)

## Problem

Ward nursing work is scattered and partly invisible. A nurse covering a ward has no single
screen answering "who is in my beds, what is due, what is overdue, who is on duty":

- The MAR exists (`/emr/charts`, spec 0024) but every dose is scheduled **by hand** — nothing
  derives the schedule from the doctor's indoor prescription, and a dose past its time is
  indistinguishable from one that is simply pending (**LC-NUR-06**, gap since the 0038 audit).
- Nursing to-dos (turn the patient, remove cannula, pre-op prep) live on paper; nothing is
  attributable or auditable.
- M6's `[S]` "Nurse/ward-boy/aya calling & duty assignment views" is recorded **ABSENT** by the
  2026-08 audit — no code anywhere. The HR roster lives on the HRM host and names no wards
  (P27 keeps ward vocabulary out of `hr`).

Salma (P5, ward in-charge) feels all three every shift.

## Requirements

- [S] **Ward monitor** (`R5`): per-ward board of occupied beds showing patient banner, latest
  vitals, doses due/overdue, pending indents, open care tasks, and today's duty strip. Read-only.
- [S] **Schedule from prescription**: the Medicine Chart can generate its dose schedule from the
  finalised indoor prescription; unparseable lines fall back to manual scheduling (never guess a
  drug time); regeneration never duplicates a dose. Overdue doses are visibly flagged; a missed
  dose shows as missed with its reason (**closes LC-NUR-06**).
- [S] **Care tasks**: create / complete / cancel-with-reason, every action attributable
  (user + time), no delete path.
- [S] **Ward duty assignment**: assign nurse/ward-boy/aya per ward, shift, and day; end an
  assignment with a reason; on-duty-today visible on the ward monitor; HR roster shown where
  published, graceful message where not.
- [M] Role boundary: nursing screens denied to non-nursing roles at the handler, not the button.

## Acceptance criteria

1. Generating from "Napa 500mg, 1+0+1, 3 days" yields exactly 6 doses at the morning/night
   slots; running Generate again yields 0 new doses; a PRN line is excluded and reported.
2. A dose 30+ minutes past schedule shows an **Overdue** badge on the chart and counts on the
   station board; marking it missed requires a reason and renders that reason; a second mark is
   refused with a plain sentence.
3. A care task can be created, completed (actor name visible), and cancelled only with a
   reason; a second complete is refused.
4. A duty assignment appears on the station's duty strip; a duplicate active assignment is
   refused; ending requires a reason; the same name can be reassigned after ending.
5. A Billing user is denied `/ipd/duty` and `/emr/tasks`; the whole flow passes as
   `eng/verify/nursing-thread.py` registered in the lifecycle suite (t1).
6. All existing guards and test suites stay green; migrations are additive.

## Out of scope

- Deployment to the VM (rides the next ERP image rebuild — stale since 2026-07-29).
- Nurse-call hardware, ICU flowsheets beyond the existing charts, auto-marking doses missed at
  discharge, frequency dialects beyond `a+b+c(+d)` / `N hourly`.
- Any new licensable module / nav prefix — entries join the existing IPD and Clinical groups.

## Risks / open questions

- Slot times (08/13/20; 4-part 08/12/16/20; N-hourly from 06:00) are clinical-convention
  defaults held as constants in one pure class — confirm with PM/nursing SME.
- R5 MoSCoW set to **[S]**; PM to confirm ERP-side duty assignment satisfies M6's `[S]` item
  (routed via `docs/architecture/09-questions-for-pm.md` conventions).
- Dose horizon clamped to 10 days; only active admissions listed. Default chosen over
  auto-writing clinical records.
