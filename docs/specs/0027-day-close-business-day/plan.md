# 0027 — Plan

## Approved: 2026-07-28

1. `DayCloseService` takes `BusinessDayCalendar` (the same singleton the rest of the money spine
   already uses) and the guard becomes:

   ```csharp
   if (session.BusinessDay != businessDay.BusinessDayOf(now) && !isCarryClose)
   ```

2. Update the three construction sites: DI registration in `Program.cs` (automatic — the
   calendar is already registered), `HistoryGenerator`, and the integration tests' own instances.

3. New test `Day_close_at_one_in_the_morning_needs_no_approval`, driving a **fake clock** fixed at
   01:00 Asia/Dhaka, asserting a same-day session closes and a yesterday session does not. A fixed
   clock is the point: the bug existed because the real clock only exposes it for six hours.

4. Re-run the full suite; the four time-dependent failures must go.
