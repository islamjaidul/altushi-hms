using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// P24. `ReopenDraftAsync` took no `KernelDbContext` and wrote nothing, so a discharge bill
/// could be assembled, withdrawn and assembled again leaving no trace that it had happened.
/// The money stayed traceable — charge lines carry whoever posted them, and the folio lock is
/// audited — but the withdrawal itself was invisible, which is the shape of a discount dispute.
///
/// Tier 1 with before/after, matching `ipd.folio.lock`: ADR-0011 puts money on tier 1, and a
/// pair of transitions that undo each other belongs in one tier.
/// </summary>
[Collection("postgres")]
public class SettlementReopenAuditTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly FolioService _folios = new(
        new AuditWriter(TimeProvider.System), new BusinessDayCalendar(TimeOnly.MinValue),
        TimeProvider.System);

    public SettlementReopenAuditTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var ipd = CreateIpd();
        await ipd.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IpdDbContext CreateIpd() => new(
        new DbContextOptionsBuilder<IpdDbContext>()
            .UseNpgsql(_pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", "ipd"))
            .UseSnakeCaseNamingConvention().Options);

    private async Task<long> DraftFolioAsync()
    {
        await using var ipd = CreateIpd();
        var folio = new Folio
        {
            BranchId = 1, PatientId = 1,
            AdmissionId = Random.Shared.NextInt64(1_000_000, 9_000_000),
        };
        ipd.Folios.Add(folio);
        await ipd.SaveChangesAsync();
        await _folios.BeginSettlementAsync(ipd, folio.Id);
        return folio.Id;
    }

    [Fact]
    public async Task Reopening_a_draft_is_audited_with_the_operator_who_did_it()
    {
        var folioId = await DraftFolioAsync();

        await using var ipd = CreateIpd();
        await using var kernel = _pg.CreateKernelContext();
        await _folios.ReopenDraftAsync(ipd, kernel, folioId, actorId: 77, actorName: "Rasel Ahmed");

        await using var check = _pg.CreateKernelContext();
        var evt = await check.AuditEvents.SingleAsync(
            e => e.Entity == "ipd.folio" && e.EntityId == folioId
                 && e.Action == "ipd.settlement.reopen");

        Assert.Equal(77, evt.ActorId);
        Assert.Equal("Rasel Ahmed", evt.ActorNameSnapshot);
        Assert.Equal(1, evt.Tier);                                  // ADR-0011: money is tier 1
        Assert.Contains(FolioState.SettlementDraft, evt.Before);     // what it was withdrawn from
        Assert.Contains(FolioState.Open, evt.After);                 // and what it went back to

        await using var after = CreateIpd();
        Assert.Equal(FolioState.Open, (await after.Folios.SingleAsync(f => f.Id == folioId)).State);
    }

    [Fact]
    public async Task A_folio_that_is_not_in_draft_is_refused_and_writes_nothing()
    {
        // The confirmed-settlement case: once locked there is no reopen, so there must be no
        // audit row either — a refused action that logs looks exactly like one that happened.
        await using var ipd = CreateIpd();
        var folio = new Folio
        {
            BranchId = 1, PatientId = 1,
            AdmissionId = Random.Shared.NextInt64(1_000_000, 9_000_000),
        };
        ipd.Folios.Add(folio);
        await ipd.SaveChangesAsync();                                // state = open, never drafted

        await using var kernel = _pg.CreateKernelContext();
        var refused = await Assert.ThrowsAsync<IpdException>(
            () => _folios.ReopenDraftAsync(ipd, kernel, folio.Id, 77, "Rasel Ahmed"));
        Assert.Contains("not in settlement draft", refused.Message);

        await using var check = _pg.CreateKernelContext();
        Assert.False(await check.AuditEvents.AnyAsync(
            e => e.EntityId == folio.Id && e.Action == "ipd.settlement.reopen"));
    }
}
