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

    // ----------------------------------------------------------------------- plumbing
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
