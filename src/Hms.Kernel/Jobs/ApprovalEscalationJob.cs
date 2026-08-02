using System.Text.Json;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Kernel.Jobs;

/// <summary>
/// Spec 0039 WP6 (AUD-XCUT-02, P4): a pending approval that outlives its tier's
/// <c>EscalationMinutes</c> is escalated to the next tier of its chain. The chain is read from
/// the request's own <c>ThresholdSnapshot</c> — the policy as it stood when the request was
/// raised — falling back to the live <c>kernel.approval_policy</c> rows for requests raised
/// before snapshots carried a chain. Escalation is a state-guarded UPDATE (ADR-0015 #4), so the
/// two hosts' workers sharing one database can never double-escalate, and every escalation is
/// audit-logged. The top tier holds: there is nobody above it to route to.
/// </summary>
public sealed class ApprovalEscalationJob(AuditWriter audit, TimeProvider clock) : IRecurringJob
{
    public string Name => "approval.escalation";

    /// <summary>Attribution for the worker's own writes: audit rows need an actor, and the
    /// escalation has none — it is the machine applying policy (P4), not a person.</summary>
    public const string SystemActor = "system";

    private sealed record ChainTier(int Tier, string Role, int EscalationMinutes);
    private sealed record Snapshot(List<ChainTier>? Chain);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var kernel = services.GetRequiredService<KernelDbContext>();
        var now = clock.GetUtcNow();

        // The worker serves every branch; the request-scoped branch filter (WP5) is for signed-in
        // operators and is deliberately opted out of here, out loud. BranchId still travels with
        // each row into its audit event.
        var pending = await kernel.ApprovalRequests.AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.State == ApprovalState.Pending)
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        // Loaded once, only if some request's snapshot lacks a chain. Keyed by approval type —
        // the snapshot chain is already per-type, so the fallback must be too.
        ILookup<string, ChainTier>? livePolicies = null;

        foreach (var request in pending)
        {
            ct.ThrowIfCancellationRequested();

            var chain = ParseChain(request.ThresholdSnapshot);
            if (chain is null or { Count: 0 })
            {
                livePolicies ??= (await kernel.ApprovalPolicies.AsNoTracking()
                        .OrderBy(p => p.Tier).ToListAsync(ct))
                    .ToLookup(p => p.Type, p => new ChainTier(p.Tier, p.Role, p.EscalationMinutes));
                chain = livePolicies[request.Type].ToList();
            }
            if (chain.Count == 0) continue;

            var index = request.EscalatedTier is null
                ? 0
                : chain.FindIndex(t => t.Tier == request.EscalatedTier);
            if (index < 0 || index >= chain.Count - 1) continue;   // unknown tier, or top of chain

            var window = chain[index].EscalationMinutes;
            if (window <= 0) continue;                             // 0 = this tier never escalates

            var waitingSince = request.EscalatedAt ?? request.RequestedAt;
            if (now - waitingSince < TimeSpan.FromMinutes(window)) continue;

            var next = chain[index + 1];

            // State-guarded, tier-guarded UPDATE: only one worker wins, and a request decided
            // between the read and the write is left exactly as its decider left it.
            var affected = await kernel.Database.ExecuteSqlAsync($"""
                UPDATE kernel.approval_request
                SET escalated_tier = {next.Tier}, escalated_at = {now}
                WHERE id = {request.Id} AND state = 'pending'
                  AND escalated_tier IS NOT DISTINCT FROM {request.EscalatedTier}
                """, ct);
            if (affected == 0) continue;

            audit.Append(kernel, request.BranchId, actorId: 0, SystemActor,
                "approval.escalate", "kernel.approval_request", request.Id,
                after: new
                {
                    request.Type,
                    fromTier = chain[index].Tier,
                    toTier = next.Tier,
                    toRole = next.Role,
                    waitedMinutes = (long)(now - waitingSince).TotalMinutes,
                });
            await kernel.SaveChangesAsync(ct);
        }
    }

    // The snapshot is written as `new { requesterRole, threshold, chain = [{ Tier, Role,
    // EscalationMinutes }] }` — mixed casing, so deserialization must be case-insensitive.
    private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNameCaseInsensitive = true };

    private static List<ChainTier>? ParseChain(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(snapshotJson, SnapshotJson);
            return snapshot?.Chain;
        }
        catch (JsonException)
        {
            return null;   // unreadable snapshot → live-policy fallback
        }
    }
}
