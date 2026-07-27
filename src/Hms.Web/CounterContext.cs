using Hms.Billing.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

public sealed record OpenSession(
    long Id, long CounterId, string CounterName, string CounterKind,
    DateOnly BusinessDay, long OpeningFloat)
{
    /// <summary>
    /// §5 M4 [M]: emergency is its own encounter type, not an OPD variant. The counter the
    /// operator opened decides it, so an ER clerk never has to remember to set it.
    /// </summary>
    public string EncounterType => CounterKind == "er" ? "ER" : "OPD";
}

/// <summary>
/// Which counter the operator is signed in at (02 §2.4). Receipts require an open session, so
/// every money screen asks this first and, when the answer is "none", offers the open-counter
/// action instead of a disabled form (§7 U7: illegal actions are absent, not warned about).
/// </summary>
public static class CounterContext
{
    public static async Task<OpenSession?> FindOpenAsync(
        BillDbContext bill, long operatorId, CancellationToken ct = default)
    {
        var row = await (
            from s in bill.Sessions
            join c in bill.Counters on s.CounterId equals c.Id
            where s.OperatorId == operatorId
                  && (s.State == SessionState.Active || s.State == SessionState.Opened
                      || s.State == SessionState.Reopened)
            orderby s.OpenedAt descending
            select new OpenSession(s.Id, c.Id, c.Name, c.Kind, s.BusinessDay, s.OpeningFloat))
            .FirstOrDefaultAsync(ct);
        return row;
    }

    /// <summary>
    /// One encounter per patient per business day per counter (02 §2.3) — the charge lines of a
    /// second visit on the same day join the same encounter rather than orphaning themselves.
    /// </summary>
    public static async Task<Encounter> GetOrCreateEncounterAsync(
        BillDbContext bill, long branchId, long patientId, long counterId, DateOnly onDate,
        string type, long actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var existing = await bill.Encounters.FirstOrDefaultAsync(
            e => e.PatientId == patientId && e.OnDate == onDate
                 && e.CounterId == counterId && e.State == "open", ct);
        if (existing is not null) return existing;

        var encounter = new Encounter
        {
            BranchId = branchId,
            PatientId = patientId,
            OnDate = onDate,
            Type = type,
            CounterId = counterId,
            State = "open",
            CreatedAt = now,
            CreatedBy = actorId,
        };
        bill.Encounters.Add(encounter);
        await bill.SaveChangesAsync(ct);
        return encounter;
    }
}
