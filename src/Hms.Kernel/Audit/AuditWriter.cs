using System.Text.Json;
using Hms.Kernel.Data;

namespace Hms.Kernel.Audit;

/// <summary>
/// ADR-0011: every Tier-1/2 write appends its audit event in the SAME transaction as the change.
/// The writer only stages the row on the caller's context — the caller's SaveChanges/commit makes
/// both durable or neither (edge 7 discipline). Append-only is additionally enforced in the
/// database (no UPDATE/DELETE grants for the app role — G11).
/// </summary>
public sealed class AuditWriter(TimeProvider clock)
{
    public AuditEvent Append(
        KernelDbContext db, long branchId, long actorId, string actorName,
        string action, string entity, long entityId,
        object? before = null, object? after = null, short tier = 1, Guid? correlationId = null)
    {
        var evt = new AuditEvent
        {
            BranchId = branchId,
            At = clock.GetUtcNow(),
            ActorId = actorId,
            ActorNameSnapshot = actorName,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Tier = tier,
        };
        db.AuditEvents.Add(evt);
        return evt;
    }
}
