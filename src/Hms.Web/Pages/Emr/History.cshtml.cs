using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

/// <summary>
/// §5 M5 [M]: the longitudinal record. Everything the hospital knows about one patient in one
/// place — visits, prescriptions, verified results, admissions — so nobody asks the patient to
/// carry paper (US5.2). Strictly read-only.
/// </summary>
[Authorize(Policy = Perm.EmrRead)]
public class HistoryModel(HmsTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public string? PatientName { get; private set; }
    public string? Uhid { get; private set; }
    public string? AgeSex { get; private set; }
    public IReadOnlyList<(long Id, string Uhid, string Name, string? Phone)> Matches { get; private set; } = [];
    public IReadOnlyList<EmrRecord.VisitRow> Visits { get; private set; } = [];
    public IReadOnlyList<EmrRecord.ResultRow> Results { get; private set; } = [];
    public IReadOnlyList<EmrRecord.AdmissionRow> Admissions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await tx.RunAsync(async s =>
        {
            // No patient yet: this is the search screen. The predicate runs in SQL (ADR-0020).
            if (PatientId is null)
            {
                if (!string.IsNullOrWhiteSpace(Q))
                    Matches = (await s.Reg.Patients.AsNoTracking()
                            .Searchable().Matching(Q!)
                            .OrderByDescending(p => p.Id).Take(20).ToListAsync())
                        .Select(p => (p.Id, p.Uhid, p.FullName, p.Phone)).ToList();
                return 0;
            }

            var patient = await s.Reg.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == PatientId);
            if (patient is null) { PatientId = null; return 0; }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            PatientName = patient.FullName;
            Uhid = patient.Uhid;
            AgeSex = $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)} · " +
                     Ui.SexShort(patient.Sex);

            Visits = await EmrRecord.VisitsAsync(s, patient.Id, 25);
            Results = await EmrRecord.ResultsAsync(s, patient.Id, 30);
            Admissions = await EmrRecord.AdmissionsAsync(s, patient.Id);
            return 0;
        });
    }
}
