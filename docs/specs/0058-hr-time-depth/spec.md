# 0058 — M16 Phase 4: time depth (devices, regularization, overtime approval and comp-off, roster patterns, short leave, shift swap)

- **Status:** Done
- **Date:** 2026-08-06
- **PRD ref:** `docs/m16-hr-payroll-prd.md` §5.4 (G22–G31), §7.4, §7.5, §9, §11, §17 Phase 4;
  main PRD §5 M16, §5A-16, §13 I8, §11, §12
- **Parent:** `docs/specs/0054-hr-payroll-industry-standard/`. Fourth of five build specs.
- **Predecessors:** `0055` (report grammar), `0056` (lifecycle), `0057` (money). Overtime becomes
  payable here, so it reaches the payroll engine 0057 finished.

## Problem

The attendance engine is good and the surface around it is one screen wide.

**1. Overtime is paid without anyone approving it.** `AttendanceService` derives OT minutes and
`PayrollService` pays them against the employer's multiplier. Nobody signs. §5A-16 requires OT to be
payable only when approved — pre-approved by request or post-approved from the exception list — and
there is no request, no approval and no state.

**2. `OvertimeRule.BankInsteadOfPay` is a flag nothing reads.** The column exists, the Policies
screen writes it, and `BuildLineAsync` never looks at it. An employer who switches banking on gets
exactly the same payslip. There is no OT bank, no comp-off, and nothing to expire.

**3. Only HR can correct attendance.** An employee who was present and is marked absent has no way
to say so. §7.4 wants a regularization request they raise themselves, decided by a manager or HR,
producing the same audited correction the HR screen already produces.

**4. There is no device.** §13 I8 asks for a device registry with automatic collection and a
last-seen health indication; the module has CSV import and manual entry, and no idea a device
exists. A site whose clock stopped reporting three days ago finds out at payroll.

**5. A roster is built one cell at a time.** `Roster` and `RosterEntry` exist; the screen assigns
one employee to one day. §5 M16 says "24/7 rotating shifts" and §7.5 asks for patterns, copy-last-
period, bulk assignment and coverage warnings. Building a 40-nurse month by hand is 1,200 clicks.

**6. `AttendanceStatus.Errand` is a status nothing produces.** Short leave, gate pass and outdoor
duty are the hourly absences a Bangladeshi hospital actually runs on, and there is no way to record
one.

**7. No shift swap.** Two nurses agreeing to trade a night is a conversation and then an HR edit.

## Requirements

- [M] **Device registry** — devices with a location, a serial, a last-seen time and a stated health;
  a device that has stopped reporting says so on the command centre. File import stays as the
  fallback and for sites with no device. (G22, §13 I8)
- [M] **Regularization request** — the employee raises it with a reason, a manager or HR decides, and
  the outcome is the **same audited `AttendanceCorrection`** the HR screen writes. Not a second
  correction path. (G23)
- [M] **Overtime approval** — per employer policy, OT is payable only when approved: pre-approved by
  request, or post-approved from the exception list. Unapproved OT is derived, visible, and **not
  paid**. (G24)
- [M] **OT bank and comp-off** — overtime banked instead of paid, comp-off earned from the bank,
  requested, approved, availed, and expiring after a configured window. (G25)
- [M] **Roster patterns** — define a rotating cycle, apply it to a team over a period; copy the
  previous period; bulk-assign a shift to selected people and days. (G27)
- [M] **Coverage** — required versus rostered per shift per day, flagged when short, and a warning
  when a rostered person is on approved leave. (§7.5)
- [S] **Short leave and outdoor duty** — an hourly absence attendance honours without consuming a
  leave day, landing on the existing `errand` status. (G29)
- [S] **Shift swap** between two employees with manager approval. (G28)
- [M] **Holiday calendar management** with per-unit assignment and a year rollover. (G31)
- [M] **Week start becomes employer configuration**, resolving the constant spec 0055 left commented
  in `PeriodCalendar`. (ADR-0029's obligation)
- [M] No statutory value (D2): the grace period, the late policy, the OT multiplier, the comp-off
  expiry window and the coverage requirement are all employer configuration.
- [M] Every new decision is attributable and audited; nothing is deleted.

## Acceptance criteria

1. A device that has not reported within its configured window is shown as silent on the command
   centre and on the device register, naming the last time it was heard from.
2. An employee's regularization request, once approved, produces exactly one `AttendanceCorrection`
   carrying the requester's reason and the approver's identity — indistinguishable in the audit from
   one HR made directly, except that it names both people.
3. With approval required, a run pays no overtime minute that has not been approved, and the
   attendance exception list offers post-approval for each.
4. With banking switched on, approved overtime credits the OT bank instead of paying, the bank
   balance equals credits minus availed minus expired, and a comp-off cannot be taken against a
   balance that is not there.
5. Applying a rotating pattern to ten people over a month writes the roster entries the pattern
   describes, skips days that already have an entry rather than overwriting them, and says how many
   it skipped.
6. Coverage shows required versus rostered per shift per day and flags a shortfall; a rostered person
   on approved leave is flagged before the roster is published.
7. A short-leave request the employee raised and a manager approved leaves the day payable and marks
   it `errand`, without consuming a leave day.
8. A shift swap approved by a manager exchanges exactly two roster entries and records both sides.
9. The week start is read from employer configuration everywhere the period control uses it, and
   falls back to the stated default when unconfigured.
10. All guards pass, including `check-additive-migrations.sh`, plus the full test suite.

## Out of scope

| Deferred | Reason | Goes to |
|---|---|---|
| The device *protocol* — actually talking to a ZKTeco-class clock | §9A.3 excluded the live feed because the devices are not in the room, and hard rule 3 forbids asserting a vendor protocol we have not verified against real hardware. This spec ships the registry, the health surface and an authenticated **push endpoint** a device or a site agent posts to; a vendor-specific poller is a spec of its own, written with a device on the desk. | later |
| Employee-facing request screens (ESS) | The request objects, their approval chains and the manager's decision surface are here. The employee's own door is ESS work, and HR raises on their behalf until then. | 0059 |
| Attendance calendar for one employee (G10) | Month-grid shaped; it shares a component with 0059's leave calendar and both should land together rather than growing two grids. | 0059 |
| Manual bulk attendance entry (`[S]` §7.4) | The device registry, import and per-day correction cover every site that has asked. | later |
| Leave-year close, encashment, balance adjustment, leave calendar (G32–G37) | §5.5, and §17 puts them in Phase 5. | 0059 |
| Roster print for the notice board (`[S]`) | Rides on 0059's notice board. | 0059 |

## What landed

| Area | Delivered |
|---|---|
| Schema | `HrTime0058`: `attendance_device`, `regularization_request`, `overtime_request`, `overtime_bank_entry`, `comp_off_request`, `roster_pattern`, `roster_pattern_step`, `shift_swap_request`, `short_leave_request`, `shift_requirement`; `OvertimeRule` gains `RequireApproval` and `CompOffExpiryDays`; `PayrollPolicy` gains `WeekStartsOn`. |
| Devices | A registry with location, serial, expected reporting interval and last-seen; `DeviceHealth` computes silent/late/healthy from the interval; an authenticated push endpoint accepts punches and stamps the device. Import stays. |
| Regularization | `RequestService.RaiseRegularizationAsync` → manager or HR decides → `AttendanceService.CorrectAsync`, the one correction path, carrying both names. |
| Overtime | `overtime_request` with pre and post approval; `PayrollService` pays only approved minutes when the rule requires approval, and **banks instead of paying** when the rule says so — the flag that had never been read. |
| Comp-off | `overtime_bank_entry` as an append-only ledger of credits, debits and expiries; `comp_off_request` spends it; a nightly sweep expires what the employer's window says. |
| Roster | Patterns as an ordered cycle of steps applied over a range; copy-previous-period; bulk assign; coverage against `shift_requirement`, with an approved-leave clash warning. |
| Short leave | Hourly `short_leave_request` for short leave and outdoor duty, landing on the `errand` status that existed and had no producer. |
| Shift swap | Two-sided request, manager-approved, exchanging exactly two entries. |
| Week start | `PayrollPolicy.WeekStartsOn`, read by `HrPeriodCalendarSource` — closing ADR-0029's open constant. |
| Registers | Device health, regularization, overtime approval, OT bank and comp-off, roster coverage, short leave, shift swap. |

**A defect found by a test written for this spec:** the regularization request linked to
correction **#0**. `AttendanceService.CorrectAsync` stages its row on the caller's context, so the
entity's id is still zero until something saves — and the request read it immediately. Nothing
failed: the correction was written, the day was corrected, the audit was complete. Only the link
from the request back to its outcome pointed at a row that never existed, and the one place that
would have shown it is the trail an auditor follows. Flushed before the id is read.

**Verification:** 934 tests green (156 Kernel, 373 Web, 104 Architecture, 300 Integration, 1
PrintGolden). Pure tests for pattern expansion and its rotation offsets, bank arithmetic, device
health boundaries and coverage; integration tests for the regularization path producing exactly one
correction, unapproved overtime not being paid, banking instead of paying, the bank refusing an
overdraft, the expiry sweep being idempotent, a pattern leaving an occupied day alone, a published
roster refusing a bulk edit, a swap exchanging exactly two entries, and coverage counting a
shortfall. Every guard passes.

## Notes

**One correction path, not two.** The regularization request is a *request*; its approval calls the
same `AttendanceService.CorrectAsync` the HR screen calls. A second write path would be a second
place for the arrears rule and the audit tier to drift, and spec 0039 already fought that fight once.

**The banking flag was the tell.** `OvertimeRule.BankInsteadOfPay` was written by a screen, stored
in a column, and read by nothing — the same shape as `hr.gratuity_rule` in 0056 and the member
ledger in 0057. Three phases running, the most valuable work has been finding the places where the
product already promised something and quietly did not do it.

**The device endpoint, not the device protocol.** §13 I8 wants a live feed and §9A.3 excluded it
because nobody has held the hardware. Shipping a registry, a health indication and an authenticated
push endpoint gives a site agent or a device that can POST somewhere to send to — and asserts
nothing about a vendor protocol this project cannot verify (hard rule 3).
