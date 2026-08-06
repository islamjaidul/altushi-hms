using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Web;
using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0059 — the leave year, against a real database.
/// <para>
/// The requirement that shapes it is §7.6's "never partially applied": a close that half-ran would
/// leave some people with next year's entitlement and some without, and nothing to say which.
/// </para>
/// </summary>
[Collection("postgres")]
public sealed class HrLeaveYearTests(PostgresFixture pg)
{
    private static long _branchSeed = 9800;
    private static long NextBranch() => Interlocked.Increment(ref _branchSeed);

    private static AuditWriter Audit() => new(TimeProvider.System);
    private static LeaveYearService Years() =>
        new(new PolicyResolver(Audit(), TimeProvider.System), Audit(), TimeProvider.System);

    private static readonly DateOnly ClosingYear = new(2025, 7, 1);
    private const int Day = 10_000;

    [Fact]
    public async Task A_close_previews_every_balance_and_commits_all_of_them()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (typeId, _) = await SeedLeaveTypeAsync(tx, branch, entitlement: 20 * Day, carry: 10 * Day);
        var one = await SeedBalanceAsync(tx, branch, typeId, opening: 0, accrued: 20 * Day, availed: 5 * Day);
        var two = await SeedBalanceAsync(tx, branch, typeId, opening: 0, accrued: 20 * Day, availed: 0, code: "B");

        var close = await tx.RunAsync(s => Years().ProposeAsync(
            s.Hr, s.Kernel, branch, ClosingYear, 1, "Farid"));
        await tx.RunAsync(s => Years().PreviewAsync(s.Hr, s.Kernel, branch, close.Id, 1, "Farid"));

        await using (var read = HrContext())
        {
            var after = await read.LeaveYearCloses.SingleAsync(c => c.Id == close.Id);
            Assert.Equal(LeaveCloseState.Previewed, after.State);
            Assert.Equal(2, after.EmployeeCount);
            Assert.Equal(2, await read.LeaveCloseLines.CountAsync(l => l.CloseId == close.Id));

            // 15 left of 20 carries whole (cap 10 → 10 carried, 5 lapsed); 20 left carries 10.
            var line = await read.LeaveCloseLines.SingleAsync(
                l => l.CloseId == close.Id && l.EmployeeId == one);
            Assert.Equal(15 * Day, line.ClosingBalanceBp);
            Assert.Equal(10 * Day, line.CarryBp);
            Assert.Equal(5 * Day, line.LapseBp);
            Assert.Equal(20 * Day, line.AccrueBp);
        }

        await tx.RunAsync(s => Years().CommitAsync(s.Hr, s.Kernel, branch, close.Id, 1, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(LeaveCloseState.Committed,
            (await hr.LeaveYearCloses.SingleAsync(c => c.Id == close.Id)).State);

        // Both people got a new year, not one of them.
        foreach (var id in new[] { one, two })
        {
            var next = await hr.LeaveBalances.SingleAsync(
                b => b.EmployeeId == id && b.LeaveYear == ClosingYear.Year + 1);
            Assert.Equal(20 * Day, next.AccruedBp);
            Assert.Equal(10 * Day, next.OpeningBp);
        }

        Assert.All(await hr.LeaveCloseLines.Where(l => l.CloseId == close.Id).ToListAsync(),
            l => Assert.True(l.Applied));
    }

    [Fact]
    public async Task An_encashable_type_writes_an_encashment_and_records_it_on_the_closing_year()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (typeId, _) = await SeedLeaveTypeAsync(
            tx, branch, entitlement: 20 * Day, carry: 10 * Day, encashable: true);
        var one = await SeedBalanceAsync(tx, branch, typeId, opening: 0, accrued: 20 * Day, availed: 0);

        var close = await tx.RunAsync(s => Years().ProposeAsync(
            s.Hr, s.Kernel, branch, ClosingYear, 1, "Farid"));
        await tx.RunAsync(s => Years().PreviewAsync(s.Hr, s.Kernel, branch, close.Id, 1, "Farid"));
        await tx.RunAsync(s => Years().CommitAsync(s.Hr, s.Kernel, branch, close.Id, 1, "Farid"));

        await using var hr = HrContext();
        var encashment = await hr.LeaveEncashments.SingleAsync(e => e.EmployeeId == one);
        Assert.Equal(10 * Day, encashment.DaysBp);
        // Priced by payroll, not guessed at by the close.
        Assert.Equal(0, encashment.AmountTaka);

        // The closing year's own arithmetic still adds up after the days left it.
        var closing = await hr.LeaveBalances.SingleAsync(
            b => b.EmployeeId == one && b.LeaveYear == ClosingYear.Year);
        Assert.Equal(10 * Day, closing.EncashedBp);
    }

    [Fact]
    public async Task A_year_is_closed_once()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();

        await tx.RunAsync(s => Years().ProposeAsync(s.Hr, s.Kernel, branch, ClosingYear, 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Years().ProposeAsync(s.Hr, s.Kernel, branch, ClosingYear, 1, "Farid")));

        Assert.Contains("already", e.Message);
    }

    [Fact]
    public async Task A_close_cannot_be_committed_before_it_is_previewed()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();

        var close = await tx.RunAsync(s => Years().ProposeAsync(
            s.Hr, s.Kernel, branch, ClosingYear, 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Years().CommitAsync(s.Hr, s.Kernel, branch, close.Id, 1, "Farid")));

        Assert.Contains("proposed", e.Message);
    }

    [Fact]
    public async Task An_unconfigured_leave_type_carries_the_balance_unchanged_rather_than_lapsing_it()
    {
        // D2: no policy means no rule. Inventing a carry-forward cap would be inventing an
        // entitlement, and lapsing somebody's days on a guess is the worst version of that.
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (typeId, _) = await SeedLeaveTypeAsync(tx, branch, withPolicy: false);
        var one = await SeedBalanceAsync(tx, branch, typeId, opening: 0, accrued: 20 * Day, availed: 0);

        var close = await tx.RunAsync(s => Years().ProposeAsync(
            s.Hr, s.Kernel, branch, ClosingYear, 1, "Farid"));
        await tx.RunAsync(s => Years().PreviewAsync(s.Hr, s.Kernel, branch, close.Id, 1, "Farid"));

        await using var hr = HrContext();
        var line = await hr.LeaveCloseLines.SingleAsync(l => l.EmployeeId == one);
        Assert.Equal(20 * Day, line.CarryBp);
        Assert.Equal(0, line.LapseBp);
        Assert.Contains("no leave policy is configured", line.Basis);
    }

    // ---- balance adjustment (G35) -------------------------------------------------------------

    [Fact]
    public async Task An_adjustment_moves_the_column_nothing_had_ever_written()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (typeId, _) = await SeedLeaveTypeAsync(tx, branch);
        var one = await SeedBalanceAsync(tx, branch, typeId, opening: 0, accrued: 10 * Day, availed: 0);

        await tx.RunAsync(s => Years().AdjustAsync(
            s.Hr, s.Kernel, branch, one, typeId, ClosingYear.Year, 2 * Day,
            "carried over from the paper register", 1, "Farid"));

        await using var hr = HrContext();
        var balance = await hr.LeaveBalances.SingleAsync(b => b.EmployeeId == one);
        Assert.Equal(2 * Day, balance.AdjustmentBp);
        Assert.Equal(12 * Day, balance.AvailableBp);
    }

    [Fact]
    public async Task An_adjustment_needs_a_reason()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (typeId, _) = await SeedLeaveTypeAsync(tx, branch);
        var one = await SeedBalanceAsync(tx, branch, typeId, opening: 0, accrued: 10 * Day, availed: 0);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Years().AdjustAsync(s.Hr, s.Kernel, branch, one, typeId, ClosingYear.Year,
                2 * Day, "", 1, "Farid")));

        Assert.Contains("reason", e.Message);
    }

    [Fact]
    public async Task A_balance_cannot_be_adjusted_below_zero()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (typeId, _) = await SeedLeaveTypeAsync(tx, branch);
        var one = await SeedBalanceAsync(tx, branch, typeId, opening: 0, accrued: 10 * Day, availed: 0);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Years().AdjustAsync(s.Hr, s.Kernel, branch, one, typeId, ClosingYear.Year,
                -15 * Day, "correction", 1, "Farid")));

        Assert.Contains("available", e.Message);
    }

    // ---- fixtures --------------------------------------------------------------------------------

    private async Task<(long TypeId, long PolicyId)> SeedLeaveTypeAsync(
        HrTx tx, long branch, int entitlement = 20 * Day, int carry = 10 * Day,
        bool encashable = false, bool withPolicy = true)
        => await tx.RunAsync(async s =>
        {
            var type = new LeaveType
            {
                BranchId = branch, Code = $"EL{branch}", Name = "Earned leave",
                Paid = true, Active = true, Accrual = LeaveAccrual.AnnualGrant,
            };
            s.Hr.LeaveTypes.Add(type);
            await s.Hr.SaveChangesAsync();

            long policyId = 0;
            if (withPolicy)
            {
                var policy = new LeavePolicy
                {
                    BranchId = branch, LeaveTypeId = type.Id,
                    EffectiveFrom = new DateOnly(2020, 1, 1),
                    AnnualEntitlementBp = entitlement,
                    MaxCarryForwardBp = carry,
                    Encashable = encashable,
                    CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
                };
                s.Hr.LeavePolicies.Add(policy);
                await s.Hr.SaveChangesAsync();
                policyId = policy.Id;
            }

            return (type.Id, policyId);
        });

    private async Task<long> SeedBalanceAsync(
        HrTx tx, long branch, long typeId, int opening, int accrued, int availed, string code = "A")
        => await tx.RunAsync(async s =>
        {
            var employee = new Employee
            {
                BranchId = branch,
                EmployeeCode = $"Y{branch}-{code}",
                FullName = "Leave Subject " + code,
                JoinedOn = new DateOnly(2021, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();

            s.Hr.LeaveBalances.Add(new LeaveBalance
            {
                BranchId = branch,
                EmployeeId = employee.Id,
                LeaveTypeId = typeId,
                LeaveYear = ClosingYear.Year,
                OpeningBp = opening,
                AccruedBp = accrued,
                AvailedBp = availed,
            });
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
