namespace Hms.Kernel.Data;

// Kernel schema entities per 03-data-model.md §1. Money is bigint whole taka (C3).
// Append-only postures (audit) are enforced by grants + triggers in migration SQL, not only here.

public class Branch
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>ADR-0004: gap-free per-scope counters, incremented under row lock in the issuing transaction.</summary>
public class NumberSeries
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string DocType { get; set; }
    public required string FiscalYear { get; set; }
    public long NextValue { get; set; } = 1;
    /// <summary>e.g. "INV-{fy}-{n:D6}"; applied by NumberSeriesService.</summary>
    public required string DisplayFormat { get; set; }
}

public static class ApprovalState
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
}

/// <summary>C7: the single approval machine for every ⚿ flow (02 §2.5).</summary>
public class ApprovalRequest
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Type { get; set; }          // discount|refund|edit|reset|reopen|late_post|rate_change|merge|carry_close
    public required string SourceTable { get; set; }
    public long SourceId { get; set; }
    public long RequestedBy { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string State { get; set; } = ApprovalState.Pending;
    public long? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public long? Amount { get; set; }
    /// <summary>jsonb snapshot of the policy row that routed this request.</summary>
    public string? ThresholdSnapshot { get; set; }
    /// <summary>Spec 0039 WP6 (AUD-XCUT-02): the tier this request has been escalated to.
    /// Null = still at the base of its chain. Bumped by the background worker when a pending
    /// request outlives its tier's <see cref="ApprovalPolicy.EscalationMinutes"/> (P4).</summary>
    public int? EscalatedTier { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
}

public class ApprovalPolicy
{
    public long Id { get; set; }
    public required string Type { get; set; }
    public int Tier { get; set; }                      // escalation order (P4 chain)
    public required string Role { get; set; }
    /// <summary>Requests with Amount below this auto-approve for the requester's own role (02 §2.5).</summary>
    public long? ThresholdMin { get; set; }
    public int EscalationMinutes { get; set; } = 10;   // P4 default
}

public class ApprovalDelegation
{
    public long Id { get; set; }
    public long FromUser { get; set; }
    public long ToUser { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public required string[] Types { get; set; }
}

/// <summary>ADR-0011 Tier 1/2: append-only, written in the same transaction as the change.</summary>
public class AuditEvent
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateTimeOffset At { get; set; }
    public long ActorId { get; set; }
    public required string ActorNameSnapshot { get; set; }
    public required string Action { get; set; }
    public required string Entity { get; set; }
    public long EntityId { get; set; }
    public string? Before { get; set; }                // jsonb
    public string? After { get; set; }                 // jsonb
    public Guid CorrelationId { get; set; }
    public short Tier { get; set; } = 1;
}

/// <summary>ADR-0002: the job queue lives in Postgres; drained with FOR UPDATE SKIP LOCKED.</summary>
public class Job
{
    public long Id { get; set; }
    public required string Type { get; set; }
    public required string Payload { get; set; }       // jsonb
    public DateTimeOffset RunAfter { get; set; }
    public int Attempts { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>02 §4: transactional outbox — a crash cannot lose a cross-module side effect.</summary>
public class OutboxMessage
{
    public long Id { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }       // jsonb
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}

public class Setting
{
    public required string Key { get; set; }
    public required string Value { get; set; }         // jsonb
}

/// <summary>ADR-0010: one committed import = one audited batch; rollback is a reversal batch.</summary>
public class ImportBatch
{
    public long Id { get; set; }
    public required string Kind { get; set; }
    public required string SourceFile { get; set; }
    public required string Sha256 { get; set; }
    public required string Mapping { get; set; }       // jsonb saved column-mapping template
    public long CommittedBy { get; set; }
    public DateTimeOffset CommittedAt { get; set; }
    public long? ReversedByBatch { get; set; }
}
