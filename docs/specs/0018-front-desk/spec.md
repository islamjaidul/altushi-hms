# 0018 — M2 Front Desk / Help Desk

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §5 M2, US2.1, US2.2, §12 (Front Desk Operator row)
- **MVP:** post-MVP — Wave 2 of `11-build-plan-phase2.md` ("thin, rides along" with M6)

## Problem

The hospital's answer window has no screen. "Is a cabin free?", "what does my father owe so
far?", "which doctor sits today?" are answered by phoning the ward or walking to billing —
M6 now holds all of the answers, but nothing composes them into the one read-mostly view the
enquiry desk needs (PRD: "answers patient-party questions instantly").

## Requirements

- [M] Admitted patient enquiry: name/UHID/phone → ward/bed, admitting doctor, admission date
- [M] Live bed/cabin status board with class & tariff (US2.1 — satisfied by `/ipd/board`,
  which the Front Desk role already reads; the enquiry screen links it)
- [M] Admitted patient **live bill estimate** (US2.2): folio balance including today's
  postings — posted charges + accrued-but-unposted bed days − advances, shown as
  advance paid / net payable
- [M] Appointment details view: today's doctors, serial counts, queue state
- [S] Previous patient searching with visit history summary (encounters + admissions)
- [S] Bed booking & reservation — reservation ships in M6 (`/ipd/admit` reserve-only);
  advance-at-reservation is deferred (a folio exists only from admission; see out-of-scope)
- [C] Public waiting-area display feed → R3 (spec 0019)

## Acceptance criteria

1. One screen (`/frontdesk`) answers US2.1/US2.2: type 2–3 letters → the patient's admission
   status, bed, doctor, and a bill estimate that equals posted folio charges **plus bed days
   accrued but not yet posted** minus advances — without writing anything (read-only: the
   estimate must NOT post charges; posting stays with `ipd.service.post`/`ipd.settle` holders).
2. The same enquiry shows a non-admitted patient's visit history summary (recent encounters,
   past admissions) — [S] previous-patient searching.
3. Today's doctors render with serial counts and queue progress from the appointments module.
4. §12: the screen needs only `ipd.read` + `registration.read` — the Front Desk Operator's
   existing grants; no money action is reachable from it.
5. Playwright covers the route + the read-only property; the estimate math is asserted in an
   integration test (estimate = posted + accrued − advances).

## Out of scope

- **Advance receipt at reservation** [S] — an advance needs a folio and a counter session;
  the folio is born at admission (0017 design). Taking reservation deposits would need a
  pre-admission folio variant — deferred until a PM answer on refund rules for cancelled
  reservations (raised as **P19**).
- Public display feed — spec 0019.

## Risks / open questions

- P19 (above). Recommended default: reservations hold no money; a cancelled reservation
  therefore never needs a refund path.
