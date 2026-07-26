using System.Text.Json;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Kernel.Approvals;

public sealed record RaiseResult(bool AutoApproved, long? RequestId, long? ApprovalId);

public sealed class ApprovalException(string message) : Exception(message);

/// <summary>
/// C7 / 04 §2.1: the ONE approval machine. Routing, thresholds, delegation and escalation are
/// data (kernel.approval_policy/_delegation) — modules never encode approver logic.
/// Auto-approval under the requester's threshold returns synchronously (billing stays fast, §8 N1).
/// </summary>
public sealed class ApprovalEngine(AuditWriter audit, TimeProvider clock)
{
    /// <summary>
    /// Raises a typed request. If a policy row for (type, requesterRole) sets a ThresholdMin and
    /// the amount is under it, the request is recorded pre-approved (threshold snapshot kept).
    /// </summary>
    public async Task<RaiseResult> RaiseAsync(
        KernelDbContext kernel, long branchId, string type, string sourceTable, long sourceId,
        long requesterId, string requesterRole, string reason, long? amount,
        CancellationToken ct = default)
    {
        var policies = await kernel.ApprovalPolicies
            .Where(p => p.Type == type)
            .OrderBy(p => p.Tier)
            .ToListAsync(ct);

        var ownPolicy = policies.FirstOrDefault(p => p.Role == requesterRole);
        var auto = ownPolicy?.ThresholdMin is not null && amount is not null
                   && amount < ownPolicy.ThresholdMin;

        var request = new ApprovalRequest
        {
            BranchId = branchId,
            Type = type,
            SourceTable = sourceTable,
            SourceId = sourceId,
            RequestedBy = requesterId,
            Reason = reason,
            RequestedAt = clock.GetUtcNow(),
            Amount = amount,
            State = auto ? ApprovalState.Approved : ApprovalState.Pending,
            DecidedBy = auto ? requesterId : null,
            DecidedAt = auto ? clock.GetUtcNow() : null,
            DecisionNote = auto ? "auto-approved under role threshold" : null,
            ThresholdSnapshot = JsonSerializer.Serialize(new
            {
                requesterRole,
                threshold = ownPolicy?.ThresholdMin,
                chain = policies.Select(p => new { p.Tier, p.Role, p.EscalationMinutes }),
            }),
        };
        kernel.ApprovalRequests.Add(request);
        await kernel.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, requesterId, requesterRole,
            auto ? "approval.auto" : "approval.raise", "kernel.approval_request", request.Id,
            after: new { type, sourceTable, sourceId, amount, auto });
        await kernel.SaveChangesAsync(ct);

        return new RaiseResult(auto, auto ? null : request.Id, auto ? request.Id : null);
    }

    /// <summary>
    /// Decides a pending request. The state-guarded UPDATE (ADR-0015 #4) means a concurrent
    /// decision loses with a comprehensible error, never a silent double-decision.
    /// </summary>
    public async Task DecideAsync(
        KernelDbContext kernel, long requestId, bool approve, long deciderId, string deciderName,
        string? note, CancellationToken ct = default)
    {
        var newState = approve ? ApprovalState.Approved : ApprovalState.Rejected;
        var now = clock.GetUtcNow();
        var affected = await kernel.Database.ExecuteSqlAsync($"""
            UPDATE kernel.approval_request
            SET state = {newState}, decided_by = {deciderId}, decided_at = {now}, decision_note = {note}
            WHERE id = {requestId} AND state = 'pending'
            """, ct);
        if (affected == 0)
            throw new ApprovalException("This request was already decided (or expired) — refresh the inbox.");

        var request = await kernel.ApprovalRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, ct);
        audit.Append(kernel, request.BranchId, deciderId, deciderName,
            approve ? "approval.approve" : "approval.reject", "kernel.approval_request", requestId,
            after: new { request.Type, request.SourceTable, request.SourceId, note });
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Active delegation: requests routed to `fromUser` may be decided by `toUser` (P4, edge 19).</summary>
    public Task<bool> IsDelegateAsync(
        KernelDbContext kernel, long fromUser, long toUser, string type, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return kernel.ApprovalDelegations.AnyAsync(d =>
            d.FromUser == fromUser && d.ToUser == toUser &&
            d.ValidFrom <= now && now < d.ValidTo &&
            d.Types.Contains(type), ct);
    }
}
