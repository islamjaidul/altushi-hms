using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Radiology.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Radiology;

public sealed class RadiologyException(string message) : Exception(message);

/// <summary>
/// §5 M10. The study's own facts and the modality mapping — nothing about results, because a
/// radiology report is a result and M9 already owns those (spec 0026's central decision).
///
/// Every method expects the caller's ambient transaction (G19).
/// </summary>
public sealed class RadiologyService(AuditWriter audit, TimeProvider clock)
{
    /// <summary>
    /// A study exists for every paid imaging order test. Created on demand and keyed by order
    /// test, so opening the worklist twice does not produce two studies for one film.
    /// </summary>
    public async Task<Study> EnsureStudyAsync(
        RadiologyDbContext radiology, long branchId, long orderTestId, long patientId,
        long modalityId, CancellationToken ct = default)
    {
        var existing = await radiology.Studies
            .FirstOrDefaultAsync(s => s.OrderTestId == orderTestId, ct);
        if (existing is not null) return existing;

        var study = new Study
        {
            BranchId = branchId, OrderTestId = orderTestId, PatientId = patientId,
            ModalityId = modalityId, AccessionNo = AccessionFor(orderTestId),
            CreatedAt = clock.GetUtcNow(),
        };
        radiology.Studies.Add(study);
        await radiology.SaveChangesAsync(ct);
        return study;
    }

    /// <summary>
    /// Deterministic from the order test, so a re-created study keeps its accession number and a
    /// machine that already saw one is not confused by a second.
    /// </summary>
    public static string AccessionFor(long orderTestId) => $"ACC-{orderTestId:D8}";

    /// <summary>
    /// US10.1: the technician marks the study shot. State-guarded, so two technicians on two
    /// screens cannot both claim it (ADR-0015), and attributable because who shot it matters
    /// when an image is questioned.
    /// </summary>
    public async Task MarkDoneAsync(
        RadiologyDbContext radiology, KernelDbContext kernel, long branchId, long studyId,
        string? filmSize, int filmCount, string? note, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (filmCount < 0) throw new RadiologyException("Film count cannot be negative.");

        var now = clock.GetUtcNow();
        var affected = await radiology.Database.ExecuteSqlAsync($"""
            UPDATE radiology.study
            SET state = 'done', performed_at = {now}, performed_by = {actorId},
                film_size = {filmSize}, film_count = {filmCount}, technician_note = {note}
            WHERE id = {studyId} AND state = 'awaiting'
            """, ct);
        if (affected == 0)
            throw new RadiologyException(
                "This study is not waiting to be performed — it may already be done. Reload the worklist.");

        audit.Append(kernel, branchId, actorId, actorName, "radiology.study.done",
            "radiology.study", studyId, after: new { filmSize, filmCount }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Called once a result exists against the order test; idempotent by state guard.</summary>
    public async Task MarkReportedAsync(
        RadiologyDbContext radiology, long studyId, CancellationToken ct = default)
        => await radiology.Database.ExecuteSqlAsync($"""
            UPDATE radiology.study SET state = 'reported'
            WHERE id = {studyId} AND state IN ('awaiting', 'done')
            """, ct);

    public static async Task<Study> GetAsync(
        RadiologyDbContext radiology, long studyId, CancellationToken ct = default)
        => await radiology.Studies.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studyId, ct)
           ?? throw new RadiologyException("Unknown study.");

    /// <summary>The machine that performs a test, or null when nobody has mapped it yet.</summary>
    public static async Task<long?> ModalityForTestAsync(
        RadiologyDbContext radiology, long testCatalogId, CancellationToken ct = default)
        => await radiology.ModalityTests.AsNoTracking()
            .Where(m => m.TestCatalogId == testCatalogId)
            .Select(m => (long?)m.ModalityId).FirstOrDefaultAsync(ct);
}
