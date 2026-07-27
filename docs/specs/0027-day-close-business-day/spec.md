# 0027 — Day-close compares a Dhaka business day against a UTC date

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §5 M4, §8 (edge 16/17), P2, §16 (Asia/Dhaka)
- **MVP:** in scope — a correctness defect in shipped money code

## Problem

A counter session is stamped with its business day by `BusinessDayCalendar`, which works in
**Asia/Dhaka**. `DayCloseService.CloseAsync` then guards against closing a *stale* session by
comparing that stamp against `clock.GetUtcNow().UtcDateTime` — a **UTC** date.

Those two agree for eighteen hours a day and disagree for six. Between **00:00 and 06:00
Asia/Dhaka** (18:00–24:00 UTC the previous day), the session's business day reads as tomorrow
relative to the UTC date, and the guard fires:

> This session belongs to business day 2026-07-28 — it needs a supervised carry-close before a
> new session can open.

The operator is told to fetch a supervisor to close a session they opened twenty minutes ago.
This is a 24/7 hospital (§8 N1, P2 exists precisely because of night shifts), so those are real
hours with real cash in a real drawer.

It also means **the test suite is only green for eighteen hours a day**. Four integration tests
fail after 18:00 UTC and pass again after midnight UTC — which is why this has survived since the
day-close work shipped.

## Requirements

- [M] The staleness guard must compare like with like: the session's business day against the
  business day of *now*, both computed by `BusinessDayCalendar`.
- [M] The carry-close path (edge 17) keeps working unchanged for genuinely stale sessions.
- [M] A test that fails only during certain hours must not be possible to write by accident here
  again — the fix is covered by a test that pins the boundary explicitly.

## Acceptance criteria

1. A session opened at 01:00 Asia/Dhaka closes normally, with no approval, at 01:30 Asia/Dhaka.
2. A session opened yesterday still requires the carry-close approval, at any hour.
3. The four previously time-dependent integration tests pass at any time of day.
4. A new test drives a fixed clock across the 00:00–06:00 Dhaka window and asserts both.

## Out of scope

- Changing the configurable boundary itself (P2's default stays 00:00 Asia/Dhaka).
- Auditing every other clock comparison in the codebase. Two were checked while here and are
  correct; a full sweep is recorded as a follow-up rather than half-done under this spec.

## Risks / open questions

- None material. The change narrows an over-eager guard; nothing that was allowed becomes
  forbidden.
