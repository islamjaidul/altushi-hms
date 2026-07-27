using Hms.Emr.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// The longitudinal read (§5 M5 [M], US5.2). Every source lives in a different schema — notes in
/// emr, orders in diag, results in lis, admissions in ipd, the patient in reg — so the join
/// happens here, in memory, at the composition root (ADR-0003). Read-only by construction.
/// </summary>
public static class EmrRecord
{
    public sealed record ResultRow(string Test, string Parameter, string Value, string Unit,
        string Flag, DateTimeOffset At);

    public sealed record VisitRow(long NoteId, DateTimeOffset At, string Doctor, string? Complaint,
        string? Diagnosis, string? Advice, string State, long? SupersedesId,
        IReadOnlyList<string> Drugs);

    public sealed record AdmissionRow(string AdmissionNo, string State, DateTimeOffset AdmittedAt,
        DateTimeOffset? DischargedAt);

    /// <summary>
    /// Verified results only. An unverified result is a working number inside the lab, not a
    /// clinical fact, and putting it in front of a prescriber would invite acting on it.
    /// </summary>
    public static async Task<IReadOnlyList<Pages.Emr.ResultLine>> RecentResultsAsync(
        TxScope s, long patientId, int take, CancellationToken ct = default)
    {
        var rows = await ResultsAsync(s, patientId, take, ct);
        return rows.Select(r => new Pages.Emr.ResultLine(
            $"{r.Test} · {r.Parameter}",
            string.IsNullOrWhiteSpace(r.Unit) ? r.Value : $"{r.Value} {r.Unit}",
            r.Flag is "high" or "low" or "critical" or "abnormal", r.At)).ToList();
    }

    public static async Task<IReadOnlyList<ResultRow>> ResultsAsync(
        TxScope s, long patientId, int take, CancellationToken ct = default)
    {
        var orderIds = await s.Diag.Orders.AsNoTracking()
            .Where(o => o.PatientId == patientId)
            .OrderByDescending(o => o.Id).Select(o => o.Id).Take(40).ToListAsync(ct);
        if (orderIds.Count == 0) return [];

        var orderTests = await s.Diag.OrderTests.AsNoTracking()
            .Where(t => orderIds.Contains(t.TestOrderId)).ToListAsync(ct);
        var orderTestIds = orderTests.Select(t => t.Id).ToList();

        var results = await s.Lis.Results.AsNoTracking()
            .Where(r => orderTestIds.Contains(r.OrderTestId) && r.VerifiedAt != null)
            .OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
        if (results.Count == 0) return [];

        var catalogIds = orderTests.Select(t => t.TestCatalogId).Distinct().ToList();
        var names = await s.Adm.TestCatalog.AsNoTracking()
            .Where(t => catalogIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var rows = new List<ResultRow>();
        foreach (var result in results)
        {
            var orderTest = orderTests.First(t => t.Id == result.OrderTestId);
            var testName = names.GetValueOrDefault(orderTest.TestCatalogId, "—");
            foreach (var (parameter, value) in ResultValues.Parse(result.Values))
                rows.Add(new ResultRow(testName, parameter, value.Value, value.Unit, value.Flag,
                    result.VerifiedAt!.Value));
        }
        return rows;
    }

    public static async Task<IReadOnlyList<VisitRow>> VisitsAsync(
        TxScope s, long patientId, int take, CancellationToken ct = default)
    {
        var notes = await s.Emr.Notes.AsNoTracking()
            .Where(n => n.PatientId == patientId && n.State != NoteState.Draft)
            .OrderByDescending(n => n.Id).Take(take).ToListAsync(ct);
        if (notes.Count == 0) return [];

        var noteIds = notes.Select(n => n.Id).ToList();
        var drugs = await s.Emr.NoteDrugs.AsNoTracking()
            .Where(d => noteIds.Contains(d.NoteId)).OrderBy(d => d.Ordinal).ToListAsync(ct);
        var doctorIds = notes.Select(n => n.DoctorId).Distinct().ToList();
        var doctors = await s.Auth.Users.AsNoTracking()
            .Where(u => doctorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return notes.Select(n => new VisitRow(
            n.Id, n.FinalisedAt ?? n.CreatedAt, doctors.GetValueOrDefault(n.DoctorId, "—"),
            n.Complaint, n.Diagnosis, n.Advice, n.State, n.SupersedesId,
            drugs.Where(d => d.NoteId == n.Id)
                .Select(d => string.Join(" ", new[] { d.DrugName, d.Dose, d.Frequency, d.Duration }
                    .Where(x => !string.IsNullOrWhiteSpace(x))))
                .ToList())).ToList();
    }

    public static async Task<IReadOnlyList<AdmissionRow>> AdmissionsAsync(
        TxScope s, long patientId, CancellationToken ct = default)
        => (await s.Ipd.Admissions.AsNoTracking()
                .Where(a => a.PatientId == patientId)
                .OrderByDescending(a => a.Id).Take(10).ToListAsync(ct))
            .Select(a => new AdmissionRow(a.AdmissionNo, a.State, a.AdmittedAt, a.DischargedAt))
            .ToList();
}
