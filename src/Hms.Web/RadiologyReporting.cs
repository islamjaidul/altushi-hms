using Hms.Lis;
using Hms.Radiology;
using Hms.Radiology.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// The M10 → M8/M9 seam. A radiology report *is* a result: it is written against the same
/// `diag.order_test`, stored once in `lis.result`, signed by M9's e-sign path and delivered by
/// M8's existing flow. This class is the join — Radiology + Lis + Diagnostics — and therefore
/// lives at the composition root (ADR-0003), like <see cref="IpdBilling"/> and
/// <see cref="OtBilling"/>.
/// </summary>
public static class RadiologyReporting
{
    public sealed record WorklistRow(
        long StudyId, long OrderTestId, string Accession, string Patient, string Uhid,
        string Test, string State, DateTimeOffset OrderedAt, bool HasResult, bool Signed);

    /// <summary>
    /// US10.1: the paid imaging orders for one machine. "Paid" is not decoration — an unpaid
    /// order must not reach the modality, exactly as it must not reach the lab bench.
    /// Studies are created here, on demand and idempotently, so nothing has to be pre-provisioned.
    /// </summary>
    public static async Task<IReadOnlyList<WorklistRow>> WorklistAsync(
        TxScope s, RadiologyService radiology, long branchId, long? modalityId,
        bool includeReported, CancellationToken ct = default)
    {
        // Which tests belong to this machine (or, with no machine chosen, to any machine).
        var mapped = await s.Radiology.ModalityTests.AsNoTracking()
            .Where(m => modalityId == null || m.ModalityId == modalityId)
            .ToListAsync(ct);
        if (mapped.Count == 0) return [];
        var testIds = mapped.Select(m => m.TestCatalogId).ToList();

        // Released orders only: MarkPaidAsync moves an order to in_progress, and reported /
        // delivered are its later lives.
        var released = new[] { "in_progress", "reported", "delivered" };
        var orders = await s.Diag.Orders.AsNoTracking()
            .Where(o => released.Contains(o.State))
            .OrderByDescending(o => o.Id).Take(300).ToListAsync(ct);
        if (orders.Count == 0) return [];

        var orderIds = orders.Select(o => o.Id).ToList();
        var orderTests = await s.Diag.OrderTests.AsNoTracking()
            .Where(t => orderIds.Contains(t.TestOrderId) && testIds.Contains(t.TestCatalogId)
                        && !t.Refunded)
            .ToListAsync(ct);
        if (orderTests.Count == 0) return [];

        foreach (var orderTest in orderTests)
        {
            var order = orders.First(o => o.Id == orderTest.TestOrderId);
            var machine = mapped.First(m => m.TestCatalogId == orderTest.TestCatalogId).ModalityId;
            await radiology.EnsureStudyAsync(s.Radiology, branchId, orderTest.Id,
                order.PatientId, machine, ct);
        }

        var orderTestIds = orderTests.Select(t => t.Id).ToList();
        var studies = await s.Radiology.Studies.AsNoTracking()
            .Where(x => orderTestIds.Contains(x.OrderTestId)).ToListAsync(ct);
        var results = await s.Lis.Results.AsNoTracking()
            .Where(r => orderTestIds.Contains(r.OrderTestId)).ToListAsync(ct);
        var names = await s.Adm.TestCatalog.AsNoTracking()
            .Where(t => testIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var patients = await s.Reg.Patients.AsNoTracking()
            .Where(p => orders.Select(o => o.PatientId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var rows = new List<WorklistRow>();
        foreach (var study in studies.OrderByDescending(x => x.Id))
        {
            var orderTest = orderTests.First(t => t.Id == study.OrderTestId);
            var order = orders.First(o => o.Id == orderTest.TestOrderId);
            var patient = patients.GetValueOrDefault(order.PatientId);
            var latest = results.Where(r => r.OrderTestId == study.OrderTestId)
                .OrderByDescending(r => r.Version).FirstOrDefault();
            if (!includeReported && study.State == StudyState.Reported) continue;

            rows.Add(new WorklistRow(study.Id, study.OrderTestId, study.AccessionNo,
                patient?.FullName ?? "—", patient?.Uhid ?? "—",
                names.GetValueOrDefault(orderTest.TestCatalogId, "—"), study.State,
                order.CreatedAt, latest is not null, latest?.VerifiedAt is not null));
        }
        return rows;
    }

    /// <summary>
    /// Saves the radiologist's report as the order test's result, then marks the study reported.
    /// One store, one audit trail, one delivery path — M9's, unchanged.
    /// </summary>
    public static async Task SaveReportAsync(
        TxScope s, RadiologyService radiology, LisService lis, Study study,
        Dictionary<string, object> values, string narrative, long actorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw new RadiologyException("A report needs findings — an empty report is not a report.");

        await lis.EnterResultAsync(s.Lis, study.OrderTestId, values, narrative, actorId, ct);
        await radiology.MarkReportedAsync(s.Radiology, study.Id, ct);
    }

    /// <summary>Signing is M9's verification: same hash, same audit, same deliverability.</summary>
    public static async Task SignAsync(
        TxScope s, LisService lis, Study study, long verifierId, string verifierRole,
        CancellationToken ct = default)
    {
        var result = await s.Lis.Results.AsNoTracking()
            .Where(r => r.OrderTestId == study.OrderTestId)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct)
            ?? throw new RadiologyException("Write the report before signing it.");
        if (result.VerifiedAt is not null)
            throw new RadiologyException("This report is already signed.");

        await lis.VerifyAsync(s.Lis, result.Id, verifierId, verifierRole, ct);
    }
}
