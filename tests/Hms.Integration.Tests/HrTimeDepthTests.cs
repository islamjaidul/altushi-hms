using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Web;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0058 — the surface around the attendance engine, against a real database.
/// <para>
/// The cases that matter: the regularization path producing exactly <b>one</b> correction, overtime
/// that is not paid until somebody signs, the banking flag that had never been read, a bank that
/// refuses an overdraft, and a pattern that does not overwrite the day somebody swapped for a
/// wedding.
/// </para>
/// </summary>
[Collection("postgres")]
public sealed class HrTimeDepthTests(PostgresFixture pg)
{
    private static long _branchSeed = 9200;
    private static long NextBranch() => Interlocked.Increment(ref _branchSeed);

    private static AuditWriter Audit() => new(TimeProvider.System);
    private static PolicyResolver Policies() => new(Audit(), TimeProvider.System);
    private static AttendanceService Attendance() => new(Audit(), TimeProvider.System);
    private static TimeRequestService Requests() =>
        new(Attendance(), Policies(), Audit(), TimeProvider.System);
    private static RosterPlanService Rosters() => new(Audit());
    private static PayrollService Payroll() =>
        new(new NumberSeriesService(), Audit(),
            new ApprovalEngine(Audit(), TimeProvider.System), Policies(),
            new FiscalCalendar(7), TimeProvider.System);

    private static readonly DateOnly Day = new(2026, 3, 10);
    private static readonly DateOnly Period = new(2026, 3, 1);

    // ---- regularization (G23) --------------------------------------------------------------------

    /// <summary>
    /// The load-bearing rule: an approved request produces the <b>same</b> correction the HR screen
    /// produces. A second write path would be a second place for the arrears rule and the audit tier
    /// to drift.
    /// </summary>
    [Fact]
    public async Task An_approved_regularization_produces_exactly_one_correction()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);
        await SeedDayAsync(tx, branch, employeeId, Day, AttendanceStatus.Absent);

        var request = await tx.RunAsync(s => Requests().RaiseRegularizationAsync(
            s.Hr, s.Kernel, branch, employeeId, Day, AttendanceStatus.Present,
            "I was on the ward, the clock did not read my finger", 5, "Nasrin"));

        await tx.RunAsync(s => Requests().DecideRegularizationAsync(
            s.Hr, s.Kernel, branch, request.Id, approve: true, "verified with the duty roster",
            9, "Farid"));

        await using var hr = HrContext();
        var day = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == Day);
        Assert.Equal(AttendanceStatus.Present, day.Status);
        Assert.True(day.Corrected);

        var correction = Assert.Single(
            await hr.AttendanceCorrections.Where(c => c.AttendanceDayId == day.Id).ToListAsync());
        // Both people are named: the employee who said it and the person who agreed.
        Assert.Contains("clock did not read my finger", correction.Reason);
        Assert.Contains("Nasrin", correction.Reason);
        Assert.Equal("Farid", correction.CorrectedByName);

        var after = await hr.RegularizationRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(RequestState.Applied, after.State);
        Assert.Equal(correction.Id, after.CorrectionId);
    }

    [Fact]
    public async Task A_rejected_regularization_changes_nothing()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);
        await SeedDayAsync(tx, branch, employeeId, Day, AttendanceStatus.Absent);

        var request = await tx.RunAsync(s => Requests().RaiseRegularizationAsync(
            s.Hr, s.Kernel, branch, employeeId, Day, AttendanceStatus.Present, "I was here",
            5, "Nasrin"));
        await tx.RunAsync(s => Requests().DecideRegularizationAsync(
            s.Hr, s.Kernel, branch, request.Id, approve: false, "the roster says otherwise",
            9, "Farid"));

        await using var hr = HrContext();
        var day = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId);
        Assert.Equal(AttendanceStatus.Absent, day.Status);
        Assert.Empty(await hr.AttendanceCorrections.Where(c => c.AttendanceDayId == day.Id).ToListAsync());
        Assert.Equal(RequestState.Rejected,
            (await hr.RegularizationRequests.SingleAsync(r => r.Id == request.Id)).State);
    }

    [Fact]
    public async Task Only_one_request_is_open_for_a_day_at_a_time()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);
        await SeedDayAsync(tx, branch, employeeId, Day, AttendanceStatus.Absent);

        await tx.RunAsync(s => Requests().RaiseRegularizationAsync(
            s.Hr, s.Kernel, branch, employeeId, Day, AttendanceStatus.Present, "first", 5, "Nasrin"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Requests().RaiseRegularizationAsync(
                s.Hr, s.Kernel, branch, employeeId, Day, AttendanceStatus.OnLeave, "second",
                5, "Nasrin")));

        Assert.Contains("already a request open", e.Message);
    }

    [Fact]
    public async Task A_day_that_does_not_exist_cannot_be_regularized()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Requests().RaiseRegularizationAsync(
                s.Hr, s.Kernel, branch, employeeId, Day, AttendanceStatus.Present, "why",
                5, "Nasrin")));

        Assert.Contains("No attendance is derived", e.Message);
    }

    // ---- overtime approval and banking (G24, G25) ------------------------------------------------

    [Fact]
    public async Task With_approval_required_unapproved_overtime_is_not_paid()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedPayableAsync(tx, branch, gross: 30_000);
        await SeedDayAsync(tx, branch, employeeId, Day, AttendanceStatus.Present, overtimeMinutes: 120);
        await SeedOvertimeRuleAsync(tx, branch, requireApproval: true, bank: false);

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == run.Id);
        Assert.Equal(120, line.OvertimeMinutes);        // derived, and visible
        Assert.Empty(await hr.PayrollComponentLines
            .Where(c => c.PayrollLineId == line.Id && c.ComponentCode == "OT").ToListAsync());
        Assert.Contains("not approved", line.Note);
    }

    [Fact]
    public async Task Approved_overtime_is_paid()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedPayableAsync(tx, branch, gross: 30_000);
        await SeedDayAsync(tx, branch, employeeId, Day, AttendanceStatus.Present, overtimeMinutes: 120);
        await SeedOvertimeRuleAsync(tx, branch, requireApproval: true, bank: false);

        var request = await tx.RunAsync(s => Requests().RaiseOvertimeAsync(
            s.Hr, s.Kernel, branch, employeeId, Day, 120, OvertimeApprovalKind.Post, "ward short",
            5, "Nasrin"));
        await tx.RunAsync(s => Requests().DecideOvertimeAsync(
            s.Hr, s.Kernel, branch, request.Id, approve: true, null, 9, "Farid"));

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == run.Id);
        var ot = await hr.PayrollComponentLines
            .SingleAsync(c => c.PayrollLineId == line.Id && c.ComponentCode == "OT");
        Assert.True(ot.AmountTaka > 0);
    }

    /// <summary>
    /// The flag that had never been read. <c>OvertimeRule.BankInsteadOfPay</c> shipped with the
    /// module, was written by the Policies screen, and produced exactly the same payslip either way.
    /// </summary>
    [Fact]
    public async Task With_banking_on_approved_overtime_credits_the_bank_instead_of_paying()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedPayableAsync(tx, branch, gross: 30_000);
        await SeedDayAsync(tx, branch, employeeId, Day, AttendanceStatus.Present, overtimeMinutes: 120);
        await SeedOvertimeRuleAsync(tx, branch, requireApproval: true, bank: true, expiryDays: 90);

        var request = await tx.RunAsync(s => Requests().RaiseOvertimeAsync(
            s.Hr, s.Kernel, branch, employeeId, Day, 120, OvertimeApprovalKind.Post, null,
            5, "Nasrin"));
        await tx.RunAsync(s => Requests().DecideOvertimeAsync(
            s.Hr, s.Kernel, branch, request.Id, approve: true, null, 9, "Farid"));

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var credit = await hr.OvertimeBank.SingleAsync(b => b.EmployeeId == employeeId);
        Assert.Equal(OvertimeBankKind.Credit, credit.Kind);
        Assert.Equal(120, credit.Minutes);
        Assert.Equal(Day.AddDays(90), credit.ExpiresOn);

        Assert.Equal(120, await TimeRequestService.BankBalanceAsync(hr, employeeId));

        // And nothing was paid for it.
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == run.Id);
        Assert.Empty(await hr.PayrollComponentLines
            .Where(c => c.PayrollLineId == line.Id && c.ComponentCode == "OT").ToListAsync());
    }

    [Fact]
    public async Task A_comp_off_cannot_be_taken_against_minutes_that_are_not_there()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Requests().RaiseCompOffAsync(
                s.Hr, s.Kernel, branch, employeeId, Day, 480, null, 5, "Nasrin")));

        Assert.Contains("not there", e.Message);
    }

    [Fact]
    public async Task Taking_a_comp_off_debits_the_bank()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);
        await SeedOvertimeRuleAsync(tx, branch, requireApproval: true, bank: true);
        await CreditBankAsync(tx, branch, employeeId, 480);

        var request = await tx.RunAsync(s => Requests().RaiseCompOffAsync(
            s.Hr, s.Kernel, branch, employeeId, Day, 240, "family", 5, "Nasrin"));
        await tx.RunAsync(s => Requests().DecideCompOffAsync(
            s.Hr, s.Kernel, branch, request.Id, approve: true, null, 9, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(240, await TimeRequestService.BankBalanceAsync(hr, employeeId));
        Assert.Equal(RequestState.Approved,
            (await hr.CompOffRequests.SingleAsync(r => r.Id == request.Id)).State);
    }

    [Fact]
    public async Task Expiring_the_bank_lapses_only_what_is_still_there()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);
        await CreditBankAsync(tx, branch, employeeId, 480, expiresOn: Day.AddDays(30));

        var expired = await tx.RunAsync(s => Requests().ExpireBankAsync(
            s.Hr, branch, Day.AddDays(60), 1));

        await using var hr = HrContext();
        Assert.Equal(1, expired);
        Assert.Equal(0, await TimeRequestService.BankBalanceAsync(hr, employeeId));

        // Idempotent: running the sweep again lapses nothing, because it already has.
        var again = await tx.RunAsync(s => Requests().ExpireBankAsync(
            s.Hr, branch, Day.AddDays(61), 1));
        Assert.Equal(0, again);
    }

    // ---- short leave (G29) --------------------------------------------------------------------------

    [Fact]
    public async Task An_approved_short_leave_marks_the_day_an_errand_and_leaves_it_payable()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);
        await SeedDayAsync(tx, branch, employeeId, Day, AttendanceStatus.Present);

        var request = await tx.RunAsync(s => Requests().RaiseShortLeaveAsync(
            s.Hr, s.Kernel, branch, employeeId, Day, ShortLeaveKind.OutdoorDuty,
            new TimeOnly(14, 0), new TimeOnly(16, 30), "collecting supplies", 5, "Nasrin"));

        await tx.RunAsync(s => Requests().DecideShortLeaveAsync(
            s.Hr, s.Kernel, branch, request.Id, approve: true, null, 9, "Farid"));

        await using var hr = HrContext();
        var day = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId);
        // errand: a status that shipped with the module and had no producer.
        Assert.Equal(AttendanceStatus.Errand, day.Status);
        // Still a payable day — an hourly absence does not consume a leave day.
        Assert.Equal(10_000, day.PayableFractionBp);
    }

    [Fact]
    public async Task An_hourly_absence_that_ends_before_it_starts_is_refused()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var employeeId = await SeedEmployeeAsync(tx, branch);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Requests().RaiseShortLeaveAsync(
                s.Hr, s.Kernel, branch, employeeId, Day, ShortLeaveKind.ShortLeave,
                new TimeOnly(16, 0), new TimeOnly(14, 0), "why", 5, "Nasrin")));

        Assert.Contains("after the start", e.Message);
    }

    // ---- roster patterns and swap (G27, G28) ------------------------------------------------------------

    [Fact]
    public async Task Applying_a_pattern_writes_the_days_it_describes()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (rosterId, shiftA, shiftB, unitId) = await SeedRosterAsync(tx, branch);
        var one = await SeedEmployeeAsync(tx, branch, unitId, "P1");
        var two = await SeedEmployeeAsync(tx, branch, unitId, "P2");
        var patternId = await SeedPatternAsync(tx, branch, shiftA, shiftB);

        var result = await tx.RunAsync(s => Rosters().ApplyPatternAsync(
            s.Hr, s.Kernel, branch, rosterId, patternId, [one, two],
            Period, Period.AddDays(13), 1, "Farid"));

        await using var hr = HrContext();
        // 14 days each, less the two rest days per person in a 14-day cycle.
        Assert.Equal(24, result.Written);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(24, await hr.RosterEntries.CountAsync(e => e.RosterId == rosterId));
        // Staggered: on day one they are not on the same shift.
        var dayOne = await hr.RosterEntries.Where(e => e.OnDate == Period).ToListAsync();
        Assert.Equal(2, dayOne.Count);
        Assert.NotEqual(dayOne[0].ShiftId, dayOne[1].ShiftId);
    }

    /// <summary>
    /// A pattern applied over a hand-made exception must not silently undo it — that is exactly the
    /// day somebody swapped for a wedding.
    /// </summary>
    [Fact]
    public async Task Applying_a_pattern_leaves_a_day_that_already_has_a_shift_alone()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (rosterId, shiftA, shiftB, unitId) = await SeedRosterAsync(tx, branch);
        var one = await SeedEmployeeAsync(tx, branch, unitId, "P1");
        var patternId = await SeedPatternAsync(tx, branch, shiftA, shiftB);

        await tx.RunAsync(async s =>
        {
            s.Hr.RosterEntries.Add(new RosterEntry
            {
                RosterId = rosterId, EmployeeId = one, OnDate = Period, ShiftId = shiftB,
            });
            await s.Hr.SaveChangesAsync();
        });

        var result = await tx.RunAsync(s => Rosters().ApplyPatternAsync(
            s.Hr, s.Kernel, branch, rosterId, patternId, [one], Period, Period.AddDays(6),
            1, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(1, result.Skipped);
        Assert.Contains("left alone", result.Words);
        // The hand-made exception survived.
        var kept = await hr.RosterEntries.SingleAsync(e => e.EmployeeId == one && e.OnDate == Period);
        Assert.Equal(shiftB, kept.ShiftId);
    }

    [Fact]
    public async Task A_published_roster_is_not_bulk_edited()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (rosterId, shiftA, _, unitId) = await SeedRosterAsync(tx, branch, published: true);
        var one = await SeedEmployeeAsync(tx, branch, unitId, "P1");

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Rosters().BulkAssignAsync(
                s.Hr, s.Kernel, branch, rosterId, shiftA, [one], Period, Period.AddDays(6),
                1, "Farid")));

        Assert.Contains("published", e.Message);
    }

    [Fact]
    public async Task An_approved_swap_exchanges_exactly_two_entries()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (rosterId, shiftA, shiftB, unitId) = await SeedRosterAsync(tx, branch);
        var one = await SeedEmployeeAsync(tx, branch, unitId, "P1");
        var two = await SeedEmployeeAsync(tx, branch, unitId, "P2");

        await tx.RunAsync(async s =>
        {
            s.Hr.RosterEntries.Add(new RosterEntry
            { RosterId = rosterId, EmployeeId = one, OnDate = Day, ShiftId = shiftA });
            s.Hr.RosterEntries.Add(new RosterEntry
            { RosterId = rosterId, EmployeeId = two, OnDate = Day, ShiftId = shiftB });
            await s.Hr.SaveChangesAsync();
        });

        var request = await tx.RunAsync(s => Requests().RaiseSwapAsync(
            s.Hr, s.Kernel, branch, one, Day, two, Day, "wedding", 5, "Nasrin"));
        await tx.RunAsync(s => Requests().DecideSwapAsync(
            s.Hr, s.Kernel, branch, request.Id, approve: true, null, 9, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(shiftB, (await hr.RosterEntries.SingleAsync(e => e.EmployeeId == one)).ShiftId);
        Assert.Equal(shiftA, (await hr.RosterEntries.SingleAsync(e => e.EmployeeId == two)).ShiftId);
        Assert.Equal(RequestState.Applied,
            (await hr.ShiftSwapRequests.SingleAsync(r => r.Id == request.Id)).State);
    }

    // ---- coverage (§7.5) ----------------------------------------------------------------------------------

    [Fact]
    public async Task Coverage_counts_the_rostered_and_flags_a_shortfall()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (rosterId, shiftA, _, unitId) = await SeedRosterAsync(tx, branch);
        var one = await SeedEmployeeAsync(tx, branch, unitId, "P1");

        await tx.RunAsync(async s =>
        {
            s.Hr.RosterEntries.Add(new RosterEntry
            { RosterId = rosterId, EmployeeId = one, OnDate = Day, ShiftId = shiftA });
            s.Hr.ShiftRequirements.Add(new ShiftRequirement
            {
                BranchId = branch, OrgUnitId = unitId, ShiftId = shiftA, RequiredHeadcount = 3,
            });
            await s.Hr.SaveChangesAsync();
        });

        await using var hr = HrContext();
        var coverage = await RosterPlanService.CoverageAsync(hr, branch, unitId, Day, Day);

        var cell = Assert.Single(coverage, c => c.ShiftId == shiftA);
        Assert.Equal(3, cell.Required);
        Assert.Equal(1, cell.Rostered);
        Assert.True(cell.IsShort);
        Assert.Equal(2, cell.Short);
    }

    // ---- fixtures --------------------------------------------------------------------------------------------

    private async Task<long> SeedEmployeeAsync(
        HrTx tx, long branch, long? unitId = null, string code = "T-001")
        => await tx.RunAsync(async s =>
        {
            var employee = new Employee
            {
                BranchId = branch,
                EmployeeCode = $"T{branch}-{code}",
                FullName = "Time Subject " + code,
                JoinedOn = new DateOnly(2021, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();

            if (unitId is { } unit)
            {
                var designation = await s.Hr.Designations.FirstAsync(d => d.BranchId == branch);
                var grade = await s.Hr.Grades.FirstAsync(g => g.BranchId == branch);
                s.Hr.Assignments.Add(new EmployeeAssignment
                {
                    BranchId = branch, EmployeeId = employee.Id, OrgUnitId = unit,
                    DesignationId = designation.Id, GradeId = grade.Id,
                    EffectiveFrom = employee.JoinedOn,
                    CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
                });
                await s.Hr.SaveChangesAsync();
            }

            return employee.Id;
        });

    private async Task SeedDayAsync(
        HrTx tx, long branch, long employeeId, DateOnly onDate, string status,
        int overtimeMinutes = 0)
        => await tx.RunAsync(async s =>
        {
            s.Hr.AttendanceDays.Add(new AttendanceDay
            {
                BranchId = branch,
                EmployeeId = employeeId,
                OnDate = onDate,
                Status = status,
                PayableFractionBp = status == AttendanceStatus.Absent ? 0 : 10_000,
                WorkedMinutes = status == AttendanceStatus.Absent ? 0 : 480,
                OvertimeMinutes = overtimeMinutes,
                DerivedAt = DateTimeOffset.UtcNow,
            });
            await s.Hr.SaveChangesAsync();
        });

    private async Task SeedOvertimeRuleAsync(
        HrTx tx, long branch, bool requireApproval, bool bank, int expiryDays = 0)
        => await tx.RunAsync(s => Policies().SetOvertimeAsync(
            s.Hr, s.Kernel, branch, new DateOnly(2020, 1, 1),
            thresholdMinutes: 0, multiplierBp: 20_000, maxMinutesPerMonth: 0,
            bankInsteadOfPay: bank, 1, "Farid", requireApproval, expiryDays));

    private async Task CreditBankAsync(
        HrTx tx, long branch, long employeeId, int minutes, DateOnly? expiresOn = null)
        => await tx.RunAsync(async s =>
        {
            s.Hr.OvertimeBank.Add(new OvertimeBankEntry
            {
                BranchId = branch, EmployeeId = employeeId, Kind = OvertimeBankKind.Credit,
                OnDate = Day, Minutes = minutes, Narration = "seed",
                ExpiresOn = expiresOn, RecordedAt = DateTimeOffset.UtcNow, RecordedBy = 1,
            });
            await s.Hr.SaveChangesAsync();
        });

    private async Task<(long RosterId, long ShiftA, long ShiftB, long UnitId)> SeedRosterAsync(
        HrTx tx, long branch, bool published = false)
        => await tx.RunAsync(async s =>
        {
            var unit = new OrgUnit { BranchId = branch, Code = $"U{branch}", Name = "Ward", Active = true };
            s.Hr.OrgUnits.Add(unit);
            s.Hr.Designations.Add(new Designation
            { BranchId = branch, Code = $"D{branch}", Name = "Nurse", Active = true });
            s.Hr.Grades.Add(new Grade
            { BranchId = branch, Code = $"G{branch}", Name = "N1", Rank = 1, Active = true });

            var morning = new Shift
            {
                BranchId = branch, Code = $"M{branch}", Name = "Morning",
                StartsAt = new TimeOnly(7, 0), EndsAt = new TimeOnly(15, 0),
                StandardMinutes = 480, Active = true,
            };
            var evening = new Shift
            {
                BranchId = branch, Code = $"E{branch}", Name = "Evening",
                StartsAt = new TimeOnly(15, 0), EndsAt = new TimeOnly(23, 0),
                StandardMinutes = 480, Active = true,
            };
            s.Hr.Shifts.AddRange(morning, evening);
            await s.Hr.SaveChangesAsync();

            var roster = new Roster
            {
                BranchId = branch, OrgUnitId = unit.Id,
                FromDate = Period, ToDate = Period.AddMonths(1).AddDays(-1),
                Published = published,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Rosters.Add(roster);
            await s.Hr.SaveChangesAsync();

            return (roster.Id, morning.Id, evening.Id, unit.Id);
        });

    private async Task<long> SeedPatternAsync(HrTx tx, long branch, long shiftA, long shiftB)
        => await tx.RunAsync(async s =>
        {
            var pattern = new RosterPattern
            {
                BranchId = branch, Code = $"P{branch}", Name = "6-1-6-1",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.RosterPatterns.Add(pattern);
            await s.Hr.SaveChangesAsync();

            s.Hr.RosterPatternSteps.AddRange(
                new RosterPatternStep { BranchId = branch, PatternId = pattern.Id, Ordinal = 0, ShiftId = shiftA, Days = 6 },
                new RosterPatternStep { BranchId = branch, PatternId = pattern.Id, Ordinal = 1, ShiftId = null, Days = 1 },
                new RosterPatternStep { BranchId = branch, PatternId = pattern.Id, Ordinal = 2, ShiftId = shiftB, Days = 6 },
                new RosterPatternStep { BranchId = branch, PatternId = pattern.Id, Ordinal = 3, ShiftId = null, Days = 1 });
            await s.Hr.SaveChangesAsync();

            return pattern.Id;
        });

    private async Task<long> SeedPayableAsync(HrTx tx, long branch, long gross)
        => await tx.RunAsync(async s =>
        {
            s.Hr.PayrollPolicies.Add(new PayrollPolicy
            {
                BranchId = branch, EffectiveFrom = new DateOnly(2020, 1, 1),
                DayCountConvention = DayCountConvention.Fixed30, MinimumNetPayTaka = 0,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            });

            var basic = new PayComponent
            {
                BranchId = branch, Code = $"BASIC{branch}", Name = "Basic",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Fixed,
                DisplayOrder = 1, Active = true,
            };
            s.Hr.PayComponents.Add(basic);
            s.Hr.PayComponents.Add(new PayComponent
            {
                BranchId = branch, Code = "OT", Name = "Overtime",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.OvertimePay, DisplayOrder = 20, Active = true,
            });
            await s.Hr.SaveChangesAsync();

            var employee = new Employee
            {
                BranchId = branch, EmployeeCode = $"T{branch}-PAY", FullName = "Payable Subject",
                JoinedOn = new DateOnly(2021, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();

            var structure = new EmployeePayStructure
            {
                BranchId = branch, EmployeeId = employee.Id,
                EffectiveFrom = employee.JoinedOn, Reason = "joining",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.PayStructures.Add(structure);
            await s.Hr.SaveChangesAsync();

            s.Hr.PayStructureComponents.Add(new EmployeePayComponent
            { PayStructureId = structure.Id, ComponentId = basic.Id, AmountTaka = gross });
            await s.Hr.SaveChangesAsync();

            return employee.Id;
        });

    private async Task<HrTx> TxAsync()
    {
        await MigrateAsync();
        return new HrTx(Config());
    }

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Hms"] = pg.ConnectionString,
        })
        .Build();

    private HrDbContext HrContext() => new(Options<HrDbContext>("hr"));

    private DbContextOptions<T> Options<T>(string schema) where T : DbContext
        => new DbContextOptionsBuilder<T>()
            .UseNpgsql(pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention()
            .Options;

    private async Task MigrateAsync()
    {
        await using var kernel = new KernelDbContext(Options<KernelDbContext>("kernel"));
        await kernel.Database.MigrateAsync();
        await using var auth = new AuthDbContext(Options<AuthDbContext>("adm"));
        await auth.Database.MigrateAsync();
        await using var hr = new HrDbContext(Options<HrDbContext>("hr"));
        await hr.Database.MigrateAsync();
    }
}
