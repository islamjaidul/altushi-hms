using System.Text.Json;
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
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// The payroll arithmetic spec 0038 proved wrong, now proven right (spec 0039 WP3):
/// AUD-M16-04 (a floored run must lock and post — the journal carries the shortfall as a
/// recoverable advance, once), AUD-M16-05 (a percent-of component is not paid to an employee
/// whose structure omits it), AUD-M16-07 (overtime multiplies before dividing, so the rate does
/// not truncate to zero at ordinary Bangladeshi salaries), AUD-M16-06 (one branch's holiday does
/// not reprorate another branch), and AUD-M16-09 (posting writes payslips). Each was watched to
/// fail against the pre-fix arithmetic.
/// </summary>
[Collection("postgres")]
public sealed class HrPayrollTests(PostgresFixture pg)
{
    private static long _branchSeed = 4200;
    private static long NextBranch() => Interlocked.Increment(ref _branchSeed);

    private readonly AuditWriter _audit = new(TimeProvider.System);

    private PayrollService Payroll() => new(
        new NumberSeriesService(), _audit, new ApprovalEngine(_audit, TimeProvider.System),
        Resolver(), new FiscalCalendar(7), TimeProvider.System);

    private PolicyResolver Resolver() => new(_audit, TimeProvider.System);

    // ------------------------------------------------------------------ AUD-M16-04 / -09
    [Fact]
    public async Task A_run_with_an_employee_at_the_minimum_net_floor_locks_and_posts()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var period = new DateOnly(2026, 3, 1);

        var employeeId = await SeedOrgAsync(tx, branch, minimumNetPay: 8_000, basic: 5_000,
            convention: DayCountConvention.CalendarDays);

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);

        await using (var hr = HrContext())
        {
            var line = await hr.PayrollLines.SingleAsync(l => l.RunId == runId);
            Assert.Equal(3_000, line.CarriedShortfallTaka);   // floor 8000 over a 5000 net
            Assert.Equal(8_000, line.NetPayTaka);             // never a negative payslip
        }

        // The whole point of AUD-M16-04: this Lock refused the entire run before the fix,
        // because the journal credited gross − shortfall against a debit of gross.
        await tx.RunAsync(s => payroll.ReviewAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));
        await ReviewVarianceAsync(tx, branch, runId);
        await tx.RunAsync(s => payroll.ApproveAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));
        var journal = await tx.RunAsync(s => payroll.LockAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));

        Assert.True(journal.IsBalanced,
            $"the lock journal must balance ({journal.TotalDebit} vs {journal.TotalCredit})");
        Assert.Equal(8_000, journal.TotalDebit);              // gross 5000 + advance 3000

        await tx.RunAsync(s => payroll.PostAsync(
            s.Hr, s.Kernel, branch, runId, new JournalOnlyPosting(), 1, "test"));

        await using (var hr = HrContext())
        {
            var run = await hr.PayrollRuns.SingleAsync(r => r.Id == runId);
            Assert.Equal(PayrollRunState.Posted, run.State);

            // AUD-M16-09: posting is what issues the [M] payslip, numbered.
            var line = await hr.PayrollLines.SingleAsync(l => l.RunId == runId);
            var slip = await hr.Payslips.SingleAsync(p => p.PayrollLineId == line.Id);
            Assert.Equal(employeeId, slip.EmployeeId);
            Assert.StartsWith("PS-", slip.PayslipNo);
        }
    }

    // ------------------------------------------------------------------ AUD-M16-07
    [Fact]
    public async Task Overtime_pays_from_multiplied_minutes_not_a_truncated_minute_rate()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var resolver = Resolver();
        // October 2026: 31 days, and its own fiscal year — run numbers are unique
        // hospital-wide while the series is per-branch, so tests must not share a fy.
        var period = new DateOnly(2026, 10, 1);

        var employeeId = await SeedOrgAsync(tx, branch, minimumNetPay: 0, basic: 12_000,
            convention: DayCountConvention.CalendarDays);

        await tx.RunAsync(s => resolver.SetOvertimeAsync(
            s.Hr, s.Kernel, branch, new DateOnly(2026, 1, 1), 0, 20_000, 0, false, 1, "test"));
        await tx.RunAsync(s =>
        {
            s.Hr.AttendanceDays.Add(new AttendanceDay
            {
                BranchId = branch, EmployeeId = employeeId, OnDate = new DateOnly(2026, 10, 10),
                Status = AttendanceStatus.Present, PayableFractionBp = 10_000,
                OvertimeMinutes = 300, WorkedMinutes = 780, DerivedAt = DateTimeOffset.UtcNow,
            });
            return Task.CompletedTask;
        });

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);

        await using var hr = HrContext();
        var ot = await hr.PayrollComponentLines
            .SingleAsync(c => c.RunId == runId && c.ComponentCode == "OT");

        // Day rate 12000/31 = 387 Tk. 300 minutes at 2.0x = 387 × 300 × 2 / 480 = 483.75 → 484.
        // The pre-fix code truncated 387/480 to a 0 Tk minute rate and paid nothing.
        Assert.Equal(484, ot.AmountTaka);
    }

    // ------------------------------------------------------------------ AUD-M16-05
    [Fact]
    public async Task A_percent_of_component_is_not_paid_where_the_structure_omits_it()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var period = new DateOnly(2027, 10, 1);               // its own fiscal year (see above)

        // SeedOrgAsync configures BASIC only; the HRA component (50% of basic) exists on the
        // branch but is deliberately absent from this employee's structure.
        await SeedOrgAsync(tx, branch, minimumNetPay: 0, basic: 12_000,
            convention: DayCountConvention.CalendarDays);

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == runId);
        var hra = await hr.PayrollComponentLines
            .Where(c => c.RunId == runId && c.ComponentCode == "HRA").ToListAsync();

        Assert.Empty(hra);                    // pre-fix: 6,000 Tk of HRA created from nothing
        Assert.Equal(12_000, line.GrossEarningsTaka);
    }

    // ------------------------------------------------------------------ AUD-M16-06
    [Fact]
    public async Task A_holiday_in_another_branch_does_not_shrink_this_branchs_denominator()
    {
        var branchA = NextBranch();
        var branchB = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); this test
        // runs as branch A — branch B is the foreign branch whose holiday must not leak in.
        BranchScope.Current = branchA;
        var tx = await TxAsync();
        var payroll = Payroll();
        var period = new DateOnly(2028, 10, 1);               // 31 days, its own fiscal year

        await SeedOrgAsync(tx, branchA, minimumNetPay: 0, basic: 12_000,
            convention: DayCountConvention.WorkingDays);

        await tx.RunAsync(async s =>
        {
            var foreign = new HolidayCalendar { BranchId = branchB, Name = "Branch B calendar" };
            s.Hr.HolidayCalendars.Add(foreign);
            await s.Hr.SaveChangesAsync();
            s.Hr.Holidays.Add(new Holiday
            {
                CalendarId = foreign.Id, OnDate = new DateOnly(2028, 10, 26), Name = "B-only day",
            });
        });

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branchA, period, 1, "test")).Id);

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == runId);
        var stamp = JsonDocument.Parse(line.PolicyStampJson!);
        // Pre-fix, branch B's holiday shrank this to 30 and inflated branch A's day rate.
        Assert.Equal(31, stamp.RootElement.GetProperty("denominator").GetInt32());
    }

    // ------------------------------------------------------------------ AUD-M16-01 write path
    [Fact]
    public async Task A_policy_rule_effective_dates_rather_than_overwriting()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var resolver = Resolver();
        var d1 = new DateOnly(2026, 1, 1);
        var d2 = new DateOnly(2026, 6, 1);

        await tx.RunAsync(s => resolver.SetDeductionAsync(
            s.Hr, s.Kernel, branch, d1, 10_000, 10_000, 1, "test"));
        await tx.RunAsync(s => resolver.SetDeductionAsync(
            s.Hr, s.Kernel, branch, d2, 5_000, 10_000, 1, "test"));

        await using (var hr = HrContext())
        {
            var rows = await hr.DeductionRules.Where(r => r.BranchId == branch)
                .OrderBy(r => r.EffectiveFrom).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(d2.AddDays(-1), rows[0].EffectiveTo);   // closed, not overwritten
            Assert.Null(rows[1].EffectiveTo);

            // History still resolves history; today resolves today (hard rule 5).
            var march = await resolver.DeductionAsync(hr, branch, new DateOnly(2026, 3, 15));
            var july = await resolver.DeductionAsync(hr, branch, new DateOnly(2026, 7, 15));
            Assert.Equal(10_000, march!.PerAbsentDayBp);
            Assert.Equal(5_000, july!.PerAbsentDayBp);
        }

        // Saving twice on one day is the operator fixing a typo: amend, don't stack.
        await tx.RunAsync(s => resolver.SetDeductionAsync(
            s.Hr, s.Kernel, branch, d2, 7_500, 10_000, 1, "test"));
        await using (var hr = HrContext())
        {
            var open = await hr.DeductionRules
                .SingleAsync(r => r.BranchId == branch && r.EffectiveTo == null);
            Assert.Equal(7_500, open.PerAbsentDayBp);
        }
    }

    [Fact]
    public async Task The_tax_band_set_is_replaced_whole_and_validated()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var resolver = Resolver();
        var from = new DateOnly(2026, 1, 1);

        await tx.RunAsync(s => resolver.SetTaxSlabsAsync(
            s.Hr, s.Kernel, branch, from, [(30_000, 0), (60_000, 500), (0, 1_000)], 1, "test"));

        await using (var hr = HrContext())
        {
            var slabs = await resolver.TaxSlabsAsync(hr, branch, new DateOnly(2026, 2, 1));
            Assert.Equal(3, slabs.Count);
            // The engine's own band walk: 70,000 taxable → 0 + 30,000×5% + 10,000×10% = 2,500.
            Assert.Equal(2_500, PayrollService.ApplySlabs(70_000, slabs));
        }

        // Ceilings out of order must refuse — a mis-ordered table silently mis-taxes everyone.
        await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s => resolver.SetTaxSlabsAsync(
            s.Hr, s.Kernel, branch, from, [(60_000, 500), (30_000, 0)], 1, "test")));
    }

    // ------------------------------------------------------------------ 0052 WP1
    /// <summary>
    /// §5 M16's `[M]` without-pay handling. Two employees on identical structures take leave of
    /// identical length in the same period; one type is <c>Paid</c>, the other is not. Before this
    /// fix both were paid in full: <c>LeaveType.Paid</c> was written by the masters screen, seeded
    /// by the demo, and read by nothing at all.
    /// </summary>
    [Fact]
    public async Task An_unpaid_leave_day_deducts_pay_and_a_paid_leave_day_does_not()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var resolver = Resolver();
        var period = new DateOnly(2029, 10, 1);               // 31 days, its own fiscal year

        var paidEmployee = await SeedOrgAsync(tx, branch, minimumNetPay: 0, basic: 12_000,
            convention: DayCountConvention.CalendarDays);
        var unpaidEmployee = await SeedPeerAsync(tx, branch, basic: 12_000);

        await SeedLwpComponentAsync(tx, branch);
        await tx.RunAsync(s => resolver.SetDeductionAsync(
            s.Hr, s.Kernel, branch, new DateOnly(2029, 1, 1),
            perAbsentDayBp: 10_000, perLeaveWithoutPayDayBp: 10_000, 1, "test"));

        // Same three days off for both. The only difference is the leave type's Paid flag.
        await SeedLeaveAsync(tx, branch, paidEmployee, paid: true,
            from: new DateOnly(2029, 10, 5), days: 3);
        await SeedLeaveAsync(tx, branch, unpaidEmployee, paid: false,
            from: new DateOnly(2029, 10, 5), days: 3);

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);

        await using var hr = HrContext();
        var paidLine = await hr.PayrollLines.SingleAsync(l => l.RunId == runId && l.EmployeeId == paidEmployee);
        var unpaidLine = await hr.PayrollLines.SingleAsync(l => l.RunId == runId && l.EmployeeId == unpaidEmployee);

        // Day rate 12,000 / 31 = 387 Tk. Three unpaid days at a full day each = 1,161 Tk.
        Assert.Equal(3 * 10_000, unpaidLine.LeaveWithoutPayDaysBp);
        Assert.Equal(12_000 - 1_161, unpaidLine.NetPayTaka);

        // The control: paid leave is still paid, and carries no deduction at all.
        Assert.Equal(0, paidLine.LeaveWithoutPayDaysBp);
        Assert.Equal(12_000, paidLine.NetPayTaka);

        // US16.1's AC — the deduction is a named line an operator can trace, not a silent subtraction.
        var lwp = await hr.PayrollComponentLines
            .SingleAsync(c => c.PayrollLineId == unpaidLine.Id && c.ComponentCode == "LWP");
        Assert.Equal(1_161, lwp.AmountTaka);
        Assert.Equal("3 unpaid leave day(s)", lwp.Basis);
        Assert.False(await hr.PayrollComponentLines
            .AnyAsync(c => c.PayrollLineId == paidLine.Id && c.ComponentCode == "LWP"));
    }

    /// <summary>
    /// ADR-0027 applied to this rule too: with no deduction rule configured there is no rate, so
    /// the run deducts nothing and says why — it does not invent a full day.
    /// </summary>
    [Fact]
    public async Task Unpaid_leave_with_no_deduction_rule_deducts_nothing_and_says_so()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var period = new DateOnly(2030, 10, 1);

        var employeeId = await SeedOrgAsync(tx, branch, minimumNetPay: 0, basic: 12_000,
            convention: DayCountConvention.CalendarDays);
        await SeedLwpComponentAsync(tx, branch);          // component exists; the RULE does not
        await SeedLeaveAsync(tx, branch, employeeId, paid: false,
            from: new DateOnly(2030, 10, 5), days: 3);

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == runId);

        Assert.Equal(3 * 10_000, line.LeaveWithoutPayDaysBp);   // counted
        Assert.Equal(12_000, line.NetPayTaka);                  // but not priced
        Assert.Contains("no unpaid-leave deduction rule", line.Note);
        Assert.False(await hr.PayrollComponentLines.AnyAsync(c => c.RunId == runId && c.ComponentCode == "LWP"));
    }

    /// <summary>
    /// Hard rule 5 for the new code path: an unpaid-leave deduction is history once its run is
    /// locked. Flipping the leave type to paid and re-rating the deduction afterwards must not
    /// reach back into a locked payslip — the figures were computed once and stored, not derived
    /// on read.
    /// </summary>
    [Fact]
    public async Task A_locked_unpaid_leave_deduction_survives_the_leave_type_being_made_paid()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var resolver = Resolver();
        var period = new DateOnly(2033, 10, 1);

        var employeeId = await SeedOrgAsync(tx, branch, minimumNetPay: 0, basic: 12_000,
            convention: DayCountConvention.CalendarDays);
        await SeedLwpComponentAsync(tx, branch);
        await tx.RunAsync(s => resolver.SetDeductionAsync(
            s.Hr, s.Kernel, branch, new DateOnly(2033, 1, 1),
            perAbsentDayBp: 10_000, perLeaveWithoutPayDayBp: 10_000, 1, "test"));
        await SeedLeaveAsync(tx, branch, employeeId, paid: false,
            from: new DateOnly(2033, 10, 5), days: 3);

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);
        await tx.RunAsync(s => payroll.ReviewAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));
        await ReviewVarianceAsync(tx, branch, runId);
        await tx.RunAsync(s => payroll.ApproveAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));
        await tx.RunAsync(s => payroll.LockAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));

        // The employer changes their mind about both the type and the rate, a year later.
        await tx.RunAsync(async s =>
        {
            var type = await s.Hr.LeaveTypes.SingleAsync(t => t.BranchId == branch && t.Code == "UL");
            type.Paid = true;
        });
        await tx.RunAsync(s => resolver.SetDeductionAsync(
            s.Hr, s.Kernel, branch, new DateOnly(2034, 1, 1),
            perAbsentDayBp: 5_000, perLeaveWithoutPayDayBp: 5_000, 1, "test"));

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == runId);
        var lwp = await hr.PayrollComponentLines
            .SingleAsync(c => c.PayrollLineId == line.Id && c.ComponentCode == "LWP");

        Assert.Equal(3 * 10_000, line.LeaveWithoutPayDaysBp);
        Assert.Equal(1_161, lwp.AmountTaka);
        Assert.Equal(12_000 - 1_161, line.NetPayTaka);
    }

    // ------------------------------------------------------------------ 0052 WP2
    /// <summary>
    /// The journal debits <em>Employee Advances</em> for a floored line — an asset the employer is
    /// owed. Before this fix nothing recorded that debt: the ledger tables had no writer at all, so
    /// the books claimed money the system had no record of and would never collect.
    /// </summary>
    [Fact]
    public async Task A_floored_lines_shortfall_is_recorded_as_a_recoverable_advance()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var period = new DateOnly(2031, 10, 1);

        var employeeId = await SeedOrgAsync(tx, branch, minimumNetPay: 8_000, basic: 5_000,
            convention: DayCountConvention.CalendarDays);

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);
        await tx.RunAsync(s => payroll.ReviewAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));
        await ReviewVarianceAsync(tx, branch, runId);
        await tx.RunAsync(s => payroll.ApproveAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));

        await using (var hr = HrContext())
            Assert.Empty(await hr.LedgerEntries.Where(e => e.PayrollRunId == runId).ToListAsync());

        var journal = await tx.RunAsync(s => payroll.LockAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));

        await using (var hr = HrContext())
        {
            var entry = await hr.LedgerEntries.SingleAsync(e => e.PayrollRunId == runId);
            Assert.Equal(LedgerKind.Advance, entry.Kind);
            Assert.Equal(employeeId, entry.EmployeeId);
            // Negative debits the member: this is money they owe, not a credit to them.
            Assert.Equal(-3_000, entry.EmployeeShareTaka);
            // And it matches the asset the journal booked, to the taka.
            var advance = journal.Lines.Single(l => l.Account == "Employee Advances");
            Assert.Equal(advance.DebitTaka, -entry.EmployeeShareTaka);
        }

        // Reversing the run gives the debt back — a mirrored entry, never a delete (hard rule 4).
        await tx.RunAsync(s => payroll.ReverseAsync(
            s.Hr, s.Kernel, branch, runId, "wrong month", 1, "test"));

        await using (var hr = HrContext())
        {
            var entries = await hr.LedgerEntries.Where(e => e.EmployeeId == employeeId)
                .OrderBy(e => e.Id).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal(0, entries.Sum(e => e.EmployeeShareTaka));   // net nil after reversal
        }
    }

    // ------------------------------------------------------------------ 0052 WP3
    /// <summary>
    /// The state machine had no lock and no concurrency token, in a module with no row locking at
    /// all. Two operators pressing Lock together both read <c>approved</c>, both proceeded, and both
    /// wrote a <c>hr.payroll.lock</c> audit row — an audit trail asserting something that did not
    /// happen, against hard rule 4. The loser must now wait, re-read, and be refused in words.
    /// </summary>
    [Fact]
    public async Task Two_operators_locking_one_run_serialize_and_the_loser_is_refused()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var payroll = Payroll();
        var period = new DateOnly(2032, 10, 1);

        await SeedOrgAsync(tx, branch, minimumNetPay: 0, basic: 12_000,
            convention: DayCountConvention.CalendarDays);

        var runId = await tx.RunAsync(async s =>
            (await payroll.GenerateAsync(s.Hr, s.Kernel, branch, period, 1, "test")).Id);
        await tx.RunAsync(s => payroll.ReviewAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));
        await ReviewVarianceAsync(tx, branch, runId);
        await tx.RunAsync(s => payroll.ApproveAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));

        // Two operators, two connections, both starting from `approved` — the house shape for this
        // (ConcurrencyTests.Two_operators_posting_to_one_folio_serialize_rather_than_lose_a_charge).
        await using var connA = new NpgsqlConnection(pg.ConnectionString);
        await using var connB = new NpgsqlConnection(pg.ConnectionString);
        await connA.OpenAsync();
        await connB.OpenAsync();

        var txA = await connA.BeginTransactionAsync();
        var txB = await connB.BeginTransactionAsync();
        await using var hrA = Attach<HrDbContext>(connA, txA, "hr");
        await using var kernelA = Attach<KernelDbContext>(connA, txA, "kernel");
        await using var hrB = Attach<HrDbContext>(connB, txB, "hr");
        await using var kernelB = Attach<KernelDbContext>(connB, txB, "kernel");

        await payroll.LockAsync(hrA, kernelA, branch, runId, 1, "operator A");
        // Both contexts, as HrTx flushes them: the audit row is staged on the kernel context and
        // a commit over an unflushed tracker writes nothing (spec 0037).
        await hrA.SaveChangesAsync();
        await kernelA.SaveChangesAsync();      // A holds the row lock, uncommitted

        var operatorB = Task.Run(async () =>
            await payroll.LockAsync(hrB, kernelB, branch, runId, 2, "operator B"));

        var finishedEarly = await Task.WhenAny(operatorB, Task.Delay(1500)) == operatorB;
        Assert.False(finishedEarly, "the second operator did not wait for the row lock");

        await txA.CommitAsync();

        var refused = await Assert.ThrowsAsync<HrException>(() => operatorB);
        Assert.Contains("locked", refused.Message);
        await txB.RollbackAsync();

        // Exactly one lock happened, and the audit says so exactly once.
        await using var kernel = pg.CreateKernelContext();
        var locks = await kernel.AuditEvents
            .Where(a => a.Action == "hr.payroll.lock" && a.EntityId == runId).ToListAsync();
        Assert.Single(locks);
    }

    // ----------------------------------------------------------------------- plumbing
    private T Attach<T>(NpgsqlConnection conn, NpgsqlTransaction dbTx, string schema)
        where T : DbContext
    {
        var ctx = (T)Activator.CreateInstance(typeof(T), Options<T>(schema, conn))!;
        ctx.Database.UseTransaction(dbTx);
        return ctx;
    }

    /// <summary>A second employee on the same branch and the same basic, for a controlled pair.</summary>
    private async Task<long> SeedPeerAsync(HrTx tx, long branch, long basic)
        => await tx.RunAsync(async s =>
        {
            var basicDef = await s.Hr.PayComponents.SingleAsync(c => c.BranchId == branch && c.Code == "BASIC");
            var employee = new Employee
            {
                BranchId = branch,
                EmployeeCode = $"T{branch}-002",
                FullName = "Test Peer",
                JoinedOn = new DateOnly(2025, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();

            var structure = new EmployeePayStructure
            {
                BranchId = branch, EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2025, 1, 1), Reason = "joining",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.PayStructures.Add(structure);
            await s.Hr.SaveChangesAsync();

            s.Hr.PayStructureComponents.Add(new EmployeePayComponent
            {
                PayStructureId = structure.Id, ComponentId = basicDef.Id, AmountTaka = basic,
            });
            return employee.Id;
        });

    /// <summary>The employer's own leave-without-pay deduction component.</summary>
    private static Task SeedLwpComponentAsync(HrTx tx, long branch)
        => tx.RunAsync(s =>
        {
            s.Hr.PayComponents.Add(new PayComponent
            {
                BranchId = branch, Code = "LWP", Name = "Leave without pay",
                Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.LeaveWithoutPay, DisplayOrder = 50,
            });
            return Task.CompletedTask;
        });

    /// <summary>
    /// Approved leave plus the attendance days it produced — the shape
    /// <c>AttendanceService.DeriveDayAsync</c> writes, so the engine sees what it would see live.
    /// </summary>
    private static Task SeedLeaveAsync(
        HrTx tx, long branch, long employeeId, bool paid, DateOnly from, int days)
        => tx.RunAsync(async s =>
        {
            var code = paid ? "PL" : "UL";
            var type = await s.Hr.LeaveTypes
                .FirstOrDefaultAsync(t => t.BranchId == branch && t.Code == code);
            if (type is null)
            {
                type = new LeaveType
                {
                    BranchId = branch, Code = code, Name = paid ? "Paid leave" : "Leave without pay",
                    Accrual = LeaveAccrual.None, Paid = paid,
                };
                s.Hr.LeaveTypes.Add(type);
                await s.Hr.SaveChangesAsync();
            }

            var application = new LeaveApplication
            {
                BranchId = branch,
                ApplicationNo = $"LV-T{branch}-{employeeId}",
                EmployeeId = employeeId,
                LeaveTypeId = type.Id,
                FromDate = from,
                ToDate = from.AddDays(days - 1),
                DaysBp = days * 10_000,
                Reason = "test",
                State = LeaveApplicationState.Approved,
                AppliedAt = DateTimeOffset.UtcNow, AppliedBy = 1, AppliedByName = "test",
            };
            s.Hr.LeaveApplications.Add(application);
            await s.Hr.SaveChangesAsync();

            for (var i = 0; i < days; i++)
                s.Hr.AttendanceDays.Add(new AttendanceDay
                {
                    BranchId = branch, EmployeeId = employeeId, OnDate = from.AddDays(i),
                    Status = AttendanceStatus.OnLeave, PayableFractionBp = 10_000,
                    LeaveApplicationId = application.Id, DerivedAt = DateTimeOffset.UtcNow,
                });
        });

    private async Task<HrTx> TxAsync()
    {
        await MigrateAsync();
        return new HrTx(Config());
    }

    /// <summary>
    /// A one-employee org on its own branch: BASIC (fixed, taxable, PF-able) configured on the
    /// employee, HRA (50% percent-of BASIC) declared on the branch but NOT configured, OT and
    /// ABSENT computed components, and a payroll policy. Codes are branch-suffixed because master
    /// codes are unique per branch but employee codes are unique hospital-wide.
    /// </summary>
    private async Task<long> SeedOrgAsync(
        HrTx tx, long branch, long minimumNetPay, long basic, string convention)
        => await tx.RunAsync(async s =>
        {
            var basicDef = new PayComponent
            {
                BranchId = branch, Code = "BASIC", Name = "Basic salary",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Fixed,
                Taxable = true, PfApplicable = true, DisplayOrder = 10,
            };
            s.Hr.PayComponents.Add(basicDef);
            await s.Hr.SaveChangesAsync();

            s.Hr.PayComponents.AddRange(
                new PayComponent
                {
                    BranchId = branch, Code = "HRA", Name = "House rent allowance",
                    Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.PercentOf,
                    BasedOnComponentId = basicDef.Id, PercentBp = 50_00, DisplayOrder = 20,
                },
                new PayComponent
                {
                    BranchId = branch, Code = "OT", Name = "Overtime",
                    Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Computed,
                    ComputedKind = ComputedComponent.OvertimePay, DisplayOrder = 30,
                },
                new PayComponent
                {
                    BranchId = branch, Code = "ABSENT", Name = "Absence deduction",
                    Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                    ComputedKind = ComputedComponent.AbsenceDeduction, DisplayOrder = 40,
                });

            s.Hr.PayrollPolicies.Add(new PayrollPolicy
            {
                BranchId = branch, EffectiveFrom = new DateOnly(2025, 1, 1),
                DayCountConvention = convention, MinimumNetPayTaka = minimumNetPay,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            });

            var employee = new Employee
            {
                BranchId = branch,
                EmployeeCode = $"T{branch}-001",
                FullName = "Test Subject",
                JoinedOn = new DateOnly(2025, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();

            var structure = new EmployeePayStructure
            {
                BranchId = branch, EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2025, 1, 1), Reason = "joining",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.PayStructures.Add(structure);
            await s.Hr.SaveChangesAsync();

            s.Hr.PayStructureComponents.Add(new EmployeePayComponent
            {
                PayStructureId = structure.Id, ComponentId = basicDef.Id, AmountTaka = basic,
            });

            return employee.Id;
        });

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Hms"] = pg.ConnectionString,
        })
        .Build();

    private HrDbContext HrContext() => new(Options<HrDbContext>("hr"));

    private DbContextOptions<T> Options<T>(string schema, NpgsqlConnection? conn = null)
        where T : DbContext
        => (conn is null
                ? new DbContextOptionsBuilder<T>().UseNpgsql(pg.ConnectionString,
                    o => o.MigrationsHistoryTable("__ef_migrations", schema))
                : new DbContextOptionsBuilder<T>().UseNpgsql(conn,
                    o => o.MigrationsHistoryTable("__ef_migrations", schema)))
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

    /// <summary>
    /// §9's Variance Reviewed, inserted between exceptions and approval by spec 0057. Every line in
    /// a branch's first run is "new this month" and therefore outside any tolerance, so a test that
    /// walks a run to approval has to explain them exactly as an operator would.
    /// </summary>
    private async Task ReviewVarianceAsync(HrTx tx, long branch, long runId)
    {
        var payroll = Payroll();
        var money = new DisbursementService(
            new NumberSeriesService(), new AuditWriter(TimeProvider.System),
            new FiscalCalendar(7), TimeProvider.System);

        await using (var read = HrContext())
        {
            foreach (var note in await read.VarianceNotes.Where(v => v.RunId == runId).ToListAsync())
            {
                await tx.RunAsync(s => money.ExplainAsync(
                    s.Hr, s.Kernel, branch, note.Id, "first run for this branch", 1, "test"));
            }
        }

        await tx.RunAsync(s => payroll.ReviewVarianceAsync(s.Hr, s.Kernel, branch, runId, 1, "test"));
    }

}
