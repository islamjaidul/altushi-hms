using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Ot.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Ot;

public sealed class OtException(string message) : Exception(message);

public sealed record TeamSlot(string Role, long PersonId, string PersonName, long? FeeServiceId);

/// <summary>
/// §5 M7. The theatre's own rules: what may be scheduled when, who is in the room, and the §11
/// state machine. It knows nothing about money — completion's folio posting spans Billing and
/// Ipd, so it lives at the composition root (ADR-0003).
///
/// Every method expects the caller's ambient transaction (G19).
/// </summary>
public sealed class OtService(
    NumberSeriesService numbers, AuditWriter audit, FiscalCalendar fiscal, TimeProvider clock)
{
    public const string CaseSeries = "ot-case";
    public const string CaseFormat = "OT-{fy}-{n:D4}";

    /// <summary>
    /// US7.1: "double-booking a theatre is impossible". The theatre row is locked first, so two
    /// schedulers racing for the same slot serialise here rather than both winning (ADR-0015 #1);
    /// the surgeon check rides the same lock because a surgeon in two theatres is the same defect
    /// wearing a different hat.
    /// </summary>
    public async Task<OtCase> ScheduleAsync(
        OtDbContext ot, KernelDbContext kernel, long branchId, long patientId, long? folioId,
        long? encounterId, long theatreId, long operationServiceId, string operationName,
        DateTimeOffset from, DateTimeOffset to, IReadOnlyList<TeamSlot> team,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if ((folioId is null) == (encounterId is null))
            throw new OtException("A case belongs to exactly one admission folio or one visit.");
        if (to <= from)
            throw new OtException("The finish time must be after the start time.");
        if (team.Count == 0 || team.All(t => t.Role != TeamRole.Surgeon))
            throw new OtException("Every operation needs a surgeon on the team.");

        await LockTheatreAsync(ot, theatreId, ct);

        if (await ClashingCaseAsync(ot, theatreId, from, to, null, ct) is { } theatreClash)
            throw new OtException(
                $"Theatre is already booked for {theatreClash.CaseNo} " +
                $"({Local(theatreClash.ScheduledFrom):HH:mm}–{Local(theatreClash.ScheduledTo):HH:mm}). " +
                "Choose another time or another theatre.");

        foreach (var member in team.Where(t => t.Role is TeamRole.Surgeon or TeamRole.Anaesthetist))
            if (await ClashingForPersonAsync(ot, member.PersonId, from, to, null, ct) is { } personClash)
                throw new OtException(
                    $"{member.PersonName} is already in {personClash.CaseNo} at that time " +
                    $"({Local(personClash.ScheduledFrom):HH:mm}–{Local(personClash.ScheduledTo):HH:mm}).");

        var now = clock.GetUtcNow();
        var (_, caseNo) = await numbers.IssueAsync(kernel, branchId, CaseSeries,
            fiscal.FiscalYearOf(DateOnly.FromDateTime(now.UtcDateTime)), CaseFormat, ct);

        var otCase = new OtCase
        {
            BranchId = branchId, CaseNo = caseNo, PatientId = patientId, FolioId = folioId,
            EncounterId = encounterId, TheatreId = theatreId,
            OperationServiceId = operationServiceId, OperationName = operationName,
            ScheduledFrom = from, ScheduledTo = to, CreatedAt = now, CreatedBy = actorId,
        };
        ot.Cases.Add(otCase);
        await ot.SaveChangesAsync(ct);

        foreach (var slot in team)
            ot.Team.Add(new CaseTeamMember
            {
                CaseId = otCase.Id, Role = slot.Role, PersonId = slot.PersonId,
                PersonName = slot.PersonName, FeeServiceId = slot.FeeServiceId,
            });
        await ot.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "ot.case.schedule", "ot.ot_case",
            otCase.Id, after: new { caseNo, theatreId, operationName, from, to }, tier: 2);
        await kernel.SaveChangesAsync(ct);
        return otCase;
    }

    /// <summary>
    /// §11's forward transitions. State-guarded in SQL, so a stale screen changes no rows and is
    /// told what actually happened rather than being obeyed (ADR-0015).
    /// </summary>
    public async Task MarkPatientReadyAsync(OtDbContext ot, long caseId, CancellationToken ct = default)
        => await MoveAsync(ot, caseId, CaseState.Scheduled, CaseState.PatientReady,
            "This case is not waiting to be sent for — reload the board.", ct);

    public async Task StartAsync(OtDbContext ot, long caseId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var affected = await ot.Database.ExecuteSqlAsync($"""
            UPDATE ot.ot_case SET state = 'in_theatre', started_at = {now}
            WHERE id = {caseId} AND state = 'patient_ready'
            """, ct);
        if (affected == 0)
            throw new OtException("The patient has not been sent for yet — reload the board.");
    }

    /// <summary>
    /// Records what happened. Completion's *charges* are posted by the composition root in the
    /// same transaction; this method moves the state and stamps the record, and refuses a second
    /// completion so nothing is ever billed twice.
    /// </summary>
    public async Task CompleteAsync(
        OtDbContext ot, KernelDbContext kernel, long caseId, long branchId, string? findings,
        string? procedurePerformed, string? anaesthesiaType, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(procedurePerformed))
            throw new OtException("Record what was done before completing the case.");

        var now = clock.GetUtcNow();
        var affected = await ot.Database.ExecuteSqlAsync($"""
            UPDATE ot.ot_case
            SET state = 'completed', finished_at = {now}, completed_at = {now},
                completed_by = {actorId}, findings = {findings},
                procedure_performed = {procedurePerformed}, anaesthesia_type = {anaesthesiaType}
            WHERE id = {caseId} AND state = 'in_theatre'
            """, ct);
        if (affected == 0)
            throw new OtException(
                "This case is not in theatre — it may already be completed. Reload the board.");

        audit.Append(kernel, branchId, actorId, actorName, "ot.case.complete", "ot.ot_case",
            caseId, after: new { procedurePerformed, anaesthesiaType }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>A cancelled case bills nothing and says why (§11 Cancelled(reason)).</summary>
    public async Task CancelAsync(
        OtDbContext ot, KernelDbContext kernel, long caseId, long branchId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new OtException("Say why the operation was cancelled — the register keeps it.");

        var affected = await ot.Database.ExecuteSqlAsync($"""
            UPDATE ot.ot_case SET state = 'cancelled', cancel_reason = {reason}
            WHERE id = {caseId} AND state IN ('scheduled', 'patient_ready')
            """, ct);
        if (affected == 0)
            throw new OtException(
                "A case that has entered theatre cannot be cancelled — complete it and correct the charges.");

        audit.Append(kernel, branchId, actorId, actorName, "ot.case.cancel", "ot.ot_case", caseId,
            after: new { reason }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Postponement returns the slot; rescheduling is a new case with a new window.</summary>
    public async Task PostponeAsync(
        OtDbContext ot, KernelDbContext kernel, long caseId, long branchId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new OtException("Say why the operation was postponed.");

        var affected = await ot.Database.ExecuteSqlAsync($"""
            UPDATE ot.ot_case SET state = 'postponed', cancel_reason = {reason}
            WHERE id = {caseId} AND state IN ('scheduled', 'patient_ready')
            """, ct);
        if (affected == 0)
            throw new OtException("Only a case that has not entered theatre can be postponed.");

        audit.Append(kernel, branchId, actorId, actorName, "ot.case.postpone", "ot.ot_case", caseId,
            after: new { reason }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    // ---- helpers -------------------------------------------------------------

    public static async Task<OtCase> GetAsync(OtDbContext ot, long caseId, CancellationToken ct = default)
        => await ot.Cases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caseId, ct)
           ?? throw new OtException("Unknown case.");

    /// <summary>Booked-out states — a cancelled or postponed case frees its slot.</summary>
    private static readonly string[] Holding =
        [CaseState.Scheduled, CaseState.PatientReady, CaseState.InTheatre];

    public static async Task<OtCase?> ClashingCaseAsync(
        OtDbContext ot, long theatreId, DateTimeOffset from, DateTimeOffset to, long? ignoreCaseId,
        CancellationToken ct = default)
        => await ot.Cases.AsNoTracking().FirstOrDefaultAsync(c =>
            c.TheatreId == theatreId && Holding.Contains(c.State)
            && (ignoreCaseId == null || c.Id != ignoreCaseId)
            && c.ScheduledFrom < to && from < c.ScheduledTo, ct);

    public static async Task<OtCase?> ClashingForPersonAsync(
        OtDbContext ot, long personId, DateTimeOffset from, DateTimeOffset to, long? ignoreCaseId,
        CancellationToken ct = default)
    {
        // Two contexts are not in play here (team and case share the ot schema), so this is one
        // query, not an in-memory join.
        var caseIds = await ot.Team.AsNoTracking()
            .Where(t => t.PersonId == personId).Select(t => t.CaseId).ToListAsync(ct);
        if (caseIds.Count == 0) return null;
        return await ot.Cases.AsNoTracking().FirstOrDefaultAsync(c =>
            caseIds.Contains(c.Id) && Holding.Contains(c.State)
            && (ignoreCaseId == null || c.Id != ignoreCaseId)
            && c.ScheduledFrom < to && from < c.ScheduledTo, ct);
    }

    private static async Task LockTheatreAsync(OtDbContext ot, long theatreId, CancellationToken ct)
    {
        var rows = await ot.Database
            .SqlQuery<long>($"SELECT id AS \"Value\" FROM ot.theatre WHERE id = {theatreId} FOR UPDATE")
            .ToListAsync(ct);
        if (rows.Count == 0) throw new OtException("Unknown theatre.");
    }

    private static async Task MoveAsync(
        OtDbContext ot, long caseId, string from, string to, string refusal, CancellationToken ct)
    {
        var affected = await ot.Database.ExecuteSqlAsync($"""
            UPDATE ot.ot_case SET state = {to} WHERE id = {caseId} AND state = {from}
            """, ct);
        if (affected == 0) throw new OtException(refusal);
    }

    private static DateTimeOffset Local(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka"));
}
