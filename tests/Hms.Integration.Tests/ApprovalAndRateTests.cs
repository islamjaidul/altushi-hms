using Hms.Admin;
using Hms.Admin.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

[Collection("postgres")]
public class ApprovalAndRateTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly ApprovalEngine _engine = new(new AuditWriter(TimeProvider.System), TimeProvider.System);
    private readonly RateResolver _resolver = new();

    public ApprovalAndRateTests(PostgresFixture pg) => _pg = pg;

    private AdmDbContext CreateAdm() => new(
        new DbContextOptionsBuilder<AdmDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "adm_data"))
        .UseSnakeCaseNamingConvention().Options);

    public async Task InitializeAsync()
    {
        await using var adm = CreateAdm();
        await adm.Database.MigrateAsync();

        await using var kernel = _pg.CreateKernelContext();
        if (!await kernel.ApprovalPolicies.AnyAsync(p => p.Type == "discount"))
        {
            // §12 workflow as data: billing operators auto-approve below ৳200; then supervisor → MD (P4)
            kernel.ApprovalPolicies.AddRange(
                new ApprovalPolicy { Type = "discount", Tier = 0, Role = "Billing Operator", ThresholdMin = 200 },
                new ApprovalPolicy { Type = "discount", Tier = 1, Role = "Billing Supervisor", EscalationMinutes = 10 },
                new ApprovalPolicy { Type = "discount", Tier = 2, Role = "MD" });
            await kernel.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- approval engine (C7) ---------------------------------------------

    [Fact]
    public async Task Discount_under_threshold_auto_approves_synchronously()
    {
        await using var kernel = _pg.CreateKernelContext();
        var result = await _engine.RaiseAsync(kernel, 1, "discount", "bill.invoice", 1,
            requesterId: 7, requesterRole: "Billing Operator", "regular patient", amount: 150);

        Assert.True(result.AutoApproved);
        Assert.NotNull(result.ApprovalId);
        var row = await kernel.ApprovalRequests.SingleAsync(r => r.Id == result.ApprovalId);
        Assert.Equal(ApprovalState.Approved, row.State);
        Assert.Contains("Billing Supervisor", row.ThresholdSnapshot);   // routing chain snapshotted
    }

    [Fact]
    public async Task Discount_above_threshold_goes_pending_then_decided_once()
    {
        await using var kernel = _pg.CreateKernelContext();
        var result = await _engine.RaiseAsync(kernel, 1, "discount", "bill.invoice", 2,
            7, "Billing Operator", "corporate guest", amount: 900);

        Assert.False(result.AutoApproved);
        await _engine.DecideAsync(kernel, result.RequestId!.Value, approve: true, 21, "Shahid", "ok");

        // Second decision on the same request must lose comprehensibly (ADR-0015 #4)
        await using var kernel2 = _pg.CreateKernelContext();
        var ex = await Assert.ThrowsAsync<ApprovalException>(
            () => _engine.DecideAsync(kernel2, result.RequestId.Value, false, 22, "MD", "no"));
        Assert.Contains("already decided", ex.Message);
    }

    [Fact]
    public async Task Delegation_window_authorizes_substitute(
        )
    {
        await using var kernel = _pg.CreateKernelContext();
        kernel.ApprovalDelegations.Add(new ApprovalDelegation
        {
            FromUser = 21, ToUser = 23,
            ValidFrom = DateTimeOffset.UtcNow.AddHours(-1), ValidTo = DateTimeOffset.UtcNow.AddDays(7),
            Types = ["discount", "refund"],
        });
        await kernel.SaveChangesAsync();

        Assert.True(await _engine.IsDelegateAsync(kernel, 21, 23, "discount"));
        Assert.False(await _engine.IsDelegateAsync(kernel, 21, 23, "reopen"));   // type-scoped (P4)
        Assert.False(await _engine.IsDelegateAsync(kernel, 21, 99, "discount"));
    }

    // ---- effective-dated rates (C6, edge 13) ------------------------------

    [Fact]
    public async Task Overlapping_rate_versions_are_rejected_by_constraint()
    {
        await using var adm = CreateAdm();
        adm.RateVersions.Add(new RateVersion
        {
            BranchId = 1, CatalogKind = "test", CatalogId = 501, Price = 400,
            ValidFrom = new DateOnly(2026, 7, 1), ValidTo = null, AuthorId = 1,
        });
        await adm.SaveChangesAsync();

        adm.RateVersions.Add(new RateVersion
        {
            BranchId = 1, CatalogKind = "test", CatalogId = 501, Price = 450,
            ValidFrom = new DateOnly(2026, 8, 1), ValidTo = null, AuthorId = 1,   // overlaps open-ended
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => adm.SaveChangesAsync());
        Assert.Contains("ex_rate_version_no_overlap",
            (ex.InnerException as PostgresException)?.ConstraintName ?? "");
    }

    [Fact]
    public async Task Price_change_is_a_new_version_and_history_resolves_unchanged()
    {
        await using var adm = CreateAdm();
        adm.RateVersions.Add(new RateVersion
        {
            BranchId = 1, CatalogKind = "test", CatalogId = 502, Price = 1200,
            ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 8, 1), AuthorId = 1,
        });
        adm.RateVersions.Add(new RateVersion
        {
            BranchId = 1, CatalogKind = "test", CatalogId = 502, Price = 1500,
            ValidFrom = new DateOnly(2026, 8, 1), ValidTo = null, AuthorId = 1,   // the "price change"
        });
        await adm.SaveChangesAsync();

        var july = await _resolver.ResolveAsync(adm, "test", 502, new DateOnly(2026, 7, 15));
        var august = await _resolver.ResolveAsync(adm, "test", 502, new DateOnly(2026, 8, 15));

        Assert.Equal(1200, july.Price);                 // history reproduces its price (edge 13)
        Assert.Equal(1500, august.Price);
        Assert.NotEqual(july.RateVersionId, august.RateVersionId);
    }

    [Fact]
    public async Task Package_scope_beats_corporate_beats_standard()
    {
        await using var adm = CreateAdm();
        var from = new DateOnly(2026, 1, 1);
        adm.RateVersions.AddRange(
            new RateVersion { BranchId = 1, CatalogKind = "test", CatalogId = 503, Price = 500, ValidFrom = from, AuthorId = 1 },
            new RateVersion { BranchId = 1, CatalogKind = "test", CatalogId = 503, Price = 450, Scope = "corporate:9", ValidFrom = from, AuthorId = 1 },
            new RateVersion { BranchId = 1, CatalogKind = "test", CatalogId = 503, Price = 400, Scope = "package:4", ValidFrom = from, AuthorId = 1 });
        await adm.SaveChangesAsync();

        var day = new DateOnly(2026, 7, 26);
        Assert.Equal(500, (await _resolver.ResolveAsync(adm, "test", 503, day)).Price);
        Assert.Equal(450, (await _resolver.ResolveAsync(adm, "test", 503, day, corporateScope: "corporate:9")).Price);
        Assert.Equal(400, (await _resolver.ResolveAsync(adm, "test", 503, day, "corporate:9", "package:4")).Price);
    }

    [Fact]
    public async Task Missing_rate_fails_comprehensibly()
    {
        await using var adm = CreateAdm();
        var ex = await Assert.ThrowsAsync<RateResolutionException>(
            () => _resolver.ResolveAsync(adm, "test", 99999, new DateOnly(2026, 7, 26)));
        Assert.Contains("No rate effective", ex.Message);
    }
}
