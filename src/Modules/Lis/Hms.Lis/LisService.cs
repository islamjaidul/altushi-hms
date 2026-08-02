using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hms.Kernel.Data;
using Hms.Lis.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Lis;

public sealed class LisException(string message) : Exception(message);

/// <summary>
/// 02 §2.8: the sample chain. Every transition is a state-guarded UPDATE (ADR-0015 #4) —
/// an illegal move affects 0 rows and surfaces a comprehensible error, never a silent jump.
/// </summary>
public sealed class LisService(TimeProvider clock)
{
    // ---- samples -----------------------------------------------------------

    public async Task<Sample> CreateSampleAsync(
        LisDbContext lis, long branchId, string sampleType, IReadOnlyList<long> orderTestIds,
        long? recollectionOf = null, CancellationToken ct = default)
    {
        var sample = new Sample
        {
            BranchId = branchId,
            Barcode = $"S{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            SampleType = sampleType,
            RecollectionOf = recollectionOf,
        };
        lis.Samples.Add(sample);
        await lis.SaveChangesAsync(ct);
        foreach (var otId in orderTestIds)
            lis.SampleTests.Add(new SampleTest { SampleId = sample.Id, OrderTestId = otId });
        await lis.SaveChangesAsync(ct);
        return sample;
    }

    /// <summary>Edge 27: reprint reuses the SAME barcode; only the print event is new.</summary>
    public async Task<string> PrintLabelAsync(
        LisDbContext lis, long sampleId, long actorId, CancellationToken ct = default)
    {
        var sample = await lis.Samples.SingleAsync(s => s.Id == sampleId, ct);
        var isReprint = await lis.LabelPrints.AnyAsync(p => p.SampleId == sampleId, ct);
        lis.LabelPrints.Add(new LabelPrint
        {
            SampleId = sampleId, PrintedAt = clock.GetUtcNow(), PrintedBy = actorId, Reprint = isReprint,
        });
        await lis.SaveChangesAsync(ct);
        return sample.Barcode;
    }

    private async Task TransitionAsync(
        LisDbContext lis, long sampleId, string from, string to,
        string? setClause, object?[] args, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var affected = to switch
        {
            SampleState.Collected => await lis.Database.ExecuteSqlAsync($"""
                UPDATE lis.sample SET state = 'collected', collected_at = {now}, collected_by = {(long)args[0]!}
                WHERE id = {sampleId} AND state = {from}
                """, ct),
            SampleState.Received => await lis.Database.ExecuteSqlAsync($"""
                UPDATE lis.sample SET state = 'received', received_at = {now}, received_by = {(long)args[0]!}
                WHERE id = {sampleId} AND state = {from}
                """, ct),
            SampleState.Rejected => await lis.Database.ExecuteSqlAsync($"""
                UPDATE lis.sample SET state = 'rejected', rejected_reason = {(string)args[0]!}
                WHERE id = {sampleId} AND state = {from}
                """, ct),
            _ => await lis.Database.ExecuteSqlAsync($"""
                UPDATE lis.sample SET state = {to} WHERE id = {sampleId} AND state = {from}
                """, ct),
        };
        if (affected == 0)
            throw new LisException($"Sample already moved on — expected '{from}' (scan again, edge 28).");
    }

    public Task CollectAsync(LisDbContext lis, long sampleId, long actorId, CancellationToken ct = default)
        => TransitionAsync(lis, sampleId, SampleState.PendingCollection, SampleState.Collected, null, [actorId], ct);

    public Task ReceiveAsync(LisDbContext lis, long sampleId, long actorId, CancellationToken ct = default)
        => TransitionAsync(lis, sampleId, SampleState.Collected, SampleState.Received, null, [actorId], ct);

    /// <summary>Rejection spawns a child sample (new barcode) bound to the SAME tests (02 §2.8).</summary>
    public async Task<Sample> RejectAndSpawnRecollectionAsync(
        LisDbContext lis, long sampleId, string reason, CancellationToken ct = default)
    {
        await TransitionAsync(lis, sampleId, SampleState.Received, SampleState.Rejected, null, [reason], ct);
        var original = await lis.Samples.AsNoTracking().SingleAsync(s => s.Id == sampleId, ct);
        var testIds = await lis.SampleTests.Where(st => st.SampleId == sampleId)
            .Select(st => st.OrderTestId).ToListAsync(ct);
        return await CreateSampleAsync(lis, original.BranchId, original.SampleType, testIds, sampleId, ct);
    }

    // ---- results (edge 22/34) ---------------------------------------------

    public async Task<Result> EnterResultAsync(
        LisDbContext lis, long orderTestId, Dictionary<string, object> values, string? narrative,
        long actorId, CancellationToken ct = default)
    {
        var version = await lis.Results.Where(r => r.OrderTestId == orderTestId)
            .Select(r => (int?)r.Version).MaxAsync(ct) ?? 0;
        if (version > 0)
            throw new LisException("A result already exists — corrections go through amendment (edge 22).");
        var result = new Result
        {
            OrderTestId = orderTestId,
            Version = 1,
            Values = JsonSerializer.Serialize(values),
            Narrative = narrative,
            EnteredBy = actorId,
            EnteredAt = clock.GetUtcNow(),
        };
        lis.Results.Add(result);
        await lis.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Edge 34: the verifier is a first-class signatory; the e-sign hash covers the values.</summary>
    public async Task VerifyAsync(
        LisDbContext lis, long resultId, long verifierId, string verifierRole,
        CancellationToken ct = default)
    {
        var result = await lis.Results.SingleAsync(r => r.Id == resultId, ct);
        if (result.VerifiedAt is not null)
            throw new LisException("Already verified — amendments create a new version (edge 22).");
        result.VerifiedBy = verifierId;
        result.VerifiedAt = clock.GetUtcNow();
        result.VerifierRole = verifierRole;
        result.EsignHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{result.OrderTestId}|{result.Version}|{result.Values}|{verifierId}")));
        await lis.SaveChangesAsync(ct);
    }

    /// <summary>Edge 22: amendment keeps v1 immutable; v2 records what it supersedes; approval-gated.
    /// <para>Spec 0039 WP4 (AUD-M10-01): the narrative travels with the amendment. It used to be
    /// dropped, so an amended radiology report — whose findings ARE the narrative — printed an
    /// empty body. Callers pass the narrative that should appear on v2; a numeric lab test
    /// passes null exactly as before.</para></summary>
    public async Task<Result> AmendAsync(
        LisDbContext lis, long orderTestId, Dictionary<string, object> newValues,
        string? narrative, long actorId, long amendApprovalId, CancellationToken ct = default)
    {
        var latest = await lis.Results.Where(r => r.OrderTestId == orderTestId)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct)
            ?? throw new LisException("Nothing to amend.");
        if (latest.VerifiedAt is null)
            throw new LisException("Unverified results are edited, not amended.");
        if (newValues.Count == 0 && string.IsNullOrWhiteSpace(narrative))
            throw new LisException("An amendment needs a corrected value or corrected findings.");

        var amended = new Result
        {
            OrderTestId = orderTestId,
            Version = latest.Version + 1,
            Values = JsonSerializer.Serialize(newValues),
            Narrative = string.IsNullOrWhiteSpace(narrative) ? null : narrative.Trim(),
            EnteredBy = actorId,
            EnteredAt = clock.GetUtcNow(),
            AmendApprovalId = amendApprovalId,
            SupersedesVersion = latest.Version,
        };
        lis.Results.Add(amended);
        await lis.SaveChangesAsync(ct);
        return amended;
    }
}
