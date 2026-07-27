# 0027 — Notes

## How it surfaced

Running the full suite as part of Wave 3's verification, at 01:22 Asia/Dhaka. Four integration
tests failed that had passed that morning. Nothing in the diff touched day-close.

The temptation at that point is to assume the new modules broke something and go looking in the
new code. What made it quick was the error message itself — "this session belongs to business day
2026-07-28" on a run whose UTC date was the 27th. The dates disagreeing *by exactly one day, in
the direction of Dhaka's offset*, is a timezone bug announcing itself.

## What was actually wrong

`BusinessDayCalendar.BusinessDayOf` converts to Asia/Dhaka and applies the configured boundary.
`CloseAsync` compared its result against `DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)`.
Those agree from 06:00 to 24:00 Dhaka and disagree from 00:00 to 06:00.

So for six hours a night, every counter session was classified as stale and refused a normal
close. §8 N1's responsiveness and P2's whole reason for existing (night-shift attribution in a
24/7 hospital) were both quietly broken in exactly the hours they were written for.

## The part worth remembering

**The test suite was only green for eighteen hours a day and nobody noticed**, because every run
had happened in the daytime. A test that uses the real clock only tests the hours you happen to
run it in. The regression test therefore parks a `FixedClock` at 01:30 Asia/Dhaka and asserts
both directions — a same-day session closing, and a genuinely stale one still demanding the
carry-close.

The first assertion in that class is not about day-close at all; it pins the premise
(`BusinessDayOf(19:30 UTC) == 28 July` while `UtcDateTime.Date == 27 July`). If someone later
changes the boundary or the timezone, that test says what broke rather than leaving a confusing
failure two layers down.

## Follow-up

Two other `GetUtcNow()` comparisons were checked while here (`FolioService`'s bed-day catch-up and
the reports' date ranges) and both convert properly. A full sweep of clock comparisons across the
codebase is worth doing but is not this spec's job; recorded here so it is not forgotten.
