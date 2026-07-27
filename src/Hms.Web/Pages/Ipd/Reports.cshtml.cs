using Hms.Ipd.Data;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record MovementRow(string AdmissionNo, string Patient, string What, DateTimeOffset At);
public sealed record CensusRow(string Ward, string Class, int Beds, int Occupied);

/// <summary>§5 M6 [M] reports: admissions/discharges in a period, occupancy, department census.</summary>
[Authorize(Policy = Perm.IpdRead)]
public class ReportsModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public string? From { get; set; }
    public string? To { get; set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }

    public IReadOnlyList<MovementRow> Admitted { get; private set; } = [];
    public IReadOnlyList<MovementRow> Left { get; private set; } = [];
    public IReadOnlyList<CensusRow> Census { get; private set; } = [];
    public int InHouseCount { get; private set; }

    public async Task OnGetAsync(string? from, string? to)
    {
        From = from;
        To = to;
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        FromDate = FlexibleDate.TryParse(from, out var f) ? f : today;
        ToDate = FlexibleDate.TryParse(to, out var t) ? t : today;
        if (ToDate < FromDate) (FromDate, ToDate) = (ToDate, FromDate);

        // Npgsql only writes UTC instants; Dhaka day boundaries become UTC before querying.
        var fromAt = new DateTimeOffset(FromDate, TimeOnly.MinValue, TimeSpan.FromHours(6)).ToUniversalTime();
        var toAt = new DateTimeOffset(ToDate.AddDays(1), TimeOnly.MinValue, TimeSpan.FromHours(6)).ToUniversalTime();

        await tx.RunAsync(async s =>
        {
            var admitted = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => a.AdmittedAt >= fromAt && a.AdmittedAt < toAt
                            && a.State != AdmissionState.Reserved)
                .OrderByDescending(a => a.AdmittedAt).Take(200).ToListAsync();
            var left = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => a.DischargedAt != null && a.DischargedAt >= fromAt && a.DischargedAt < toAt)
                .OrderByDescending(a => a.DischargedAt).Take(200).ToListAsync();

            var patientIds = admitted.Select(a => a.PatientId)
                .Union(left.Select(a => a.PatientId)).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName);

            Admitted = admitted.Select(a => new MovementRow(a.AdmissionNo,
                patients.GetValueOrDefault(a.PatientId, "—"), a.Source, a.AdmittedAt)).ToList();
            Left = left.Select(a => new MovementRow(a.AdmissionNo,
                patients.GetValueOrDefault(a.PatientId, "—"), Ui.Humanize(a.State),
                a.DischargedAt!.Value)).ToList();

            var wards = await s.Ipd.Wards.AsNoTracking().Where(w => w.Active).ToListAsync();
            var beds = await s.Ipd.Beds.AsNoTracking().ToListAsync();
            Census = wards.Select(w =>
            {
                var wardBeds = beds.Where(b => b.WardId == w.Id).ToList();
                return new CensusRow(w.Name, w.Class, wardBeds.Count,
                    wardBeds.Count(b => b.State == BedState.Occupied));
            }).ToList();
            InHouseCount = beds.Count(b => b.State == BedState.Occupied);
            return 0;
        });
    }
}
