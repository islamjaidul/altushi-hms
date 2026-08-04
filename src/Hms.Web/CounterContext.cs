using Hms.Billing.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

public sealed record OpenSession(
    long Id, long CounterId, string CounterName, string CounterKind,
    DateOnly BusinessDay, long OpeningFloat)
{
    /// <summary>
    /// §5 M4 [M]: emergency is its own encounter type, not an OPD variant. The counter the
    /// operator opened decides it, so an ER clerk never has to remember to set it. Encounter-
    /// creating screens resolve their session with <see cref="CounterContext.FindOpenOutdoorAsync"/>,
    /// so an IPD session can never reach this property (spec 0048) — inpatient money is
    /// folio-parented and mints no encounters.
    /// </summary>
    public string EncounterType => CounterKind == "er" ? "ER" : "OPD";
}

/// <summary>
/// Which counter the operator is signed in at (02 §2.4). Receipts require an open session, so
/// every money screen asks this first and, when the answer is "none", offers the open-counter
/// action instead of a disabled form (§7 U7: illegal actions are absent, not warned about).
///
/// Spec 0048 splits the IPD money stream: sessions are unique per counter (not per operator),
/// so one cashier may hold an outdoor drawer and the IPD drawer at once. Encounter-creating
/// screens (OPD invoice, diagnostics, pharmacy) resolve outdoor-only; discharge settlement
/// resolves IPD-only; dues/refunds and folio advances stay kind-agnostic on purpose — the IPD
/// counter may collect IPD dues, and an advance can be taken wherever the family is standing.
/// </summary>
public static class CounterContext
{
    public const string IpdKind = "ipd";

    /// <summary>The operator's most recent open session of any kind — dues, refunds, advances.</summary>
    public static Task<OpenSession?> FindOpenAsync(
        BillDbContext bill, long operatorId, CancellationToken ct = default)
        => FindAsync(bill, operatorId, ipdKind: null, ct);

    /// <summary>The operator's open IPD-counter session — discharge settlement money.</summary>
    public static Task<OpenSession?> FindOpenIpdAsync(
        BillDbContext bill, long operatorId, CancellationToken ct = default)
        => FindAsync(bill, operatorId, ipdKind: true, ct);

    /// <summary>The operator's open non-IPD session — everything that mints encounters.</summary>
    public static Task<OpenSession?> FindOpenOutdoorAsync(
        BillDbContext bill, long operatorId, CancellationToken ct = default)
        => FindAsync(bill, operatorId, ipdKind: false, ct);

    private static async Task<OpenSession?> FindAsync(
        BillDbContext bill, long operatorId, bool? ipdKind, CancellationToken ct)
    {
        var query =
            from s in bill.Sessions
            join c in bill.Counters on s.CounterId equals c.Id
            where s.OperatorId == operatorId
                  && (s.State == SessionState.Active || s.State == SessionState.Opened
                      || s.State == SessionState.Reopened)
            select new { s, c };
        if (ipdKind == true) query = query.Where(x => x.c.Kind == IpdKind);
        if (ipdKind == false) query = query.Where(x => x.c.Kind != IpdKind);

        return await query.OrderByDescending(x => x.s.OpenedAt)
            .Select(x => new OpenSession(
                x.s.Id, x.c.Id, x.c.Name, x.c.Kind, x.s.BusinessDay, x.s.OpeningFloat))
            .FirstOrDefaultAsync(ct);
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
