using Hms.Radiology.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Radiology;

public sealed record PrintedValue(string Name, string Value, string Unit, string Flag, string Ref);

/// <summary>
/// US10.2 AC: "unsigned reports cannot print as final". They still print — a technician needs a
/// working copy — but marked provisional, so a sheet in a patient's hand is never mistaken for a
/// signed report.
/// </summary>
[Authorize(Policy = Perm.RadiologyWorklistRead)]
public class PrintModel(HmsTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    public Study? Study { get; private set; }
    public string PatientName { get; private set; } = "—";
    public string Uhid { get; private set; } = "—";
    public string AgeSex { get; private set; } = "—";
    public string TestName { get; private set; } = "—";
    public string ModalityName { get; private set; } = "—";
    public string? Narrative { get; private set; }
    public IReadOnlyList<PrintedValue> Values { get; private set; } = [];
    public bool Signed { get; private set; }
    public string? VerifierName { get; private set; }
    public string? VerifierLine { get; private set; }
    public DateTimeOffset At { get; private set; }
    public int Version { get; private set; }

    public async Task<IActionResult> OnGetAsync()
        => await LoadAsync() ? Page() : NotFound();

    private async Task<bool> LoadAsync() => await tx.RunAsync(async s =>
    {
        Study = await s.Radiology.Studies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Id);
        if (Study is null) return false;

        var orderTest = await s.Diag.OrderTests.AsNoTracking()
            .FirstAsync(t => t.Id == Study.OrderTestId);
        TestName = await s.Adm.TestCatalog.AsNoTracking()
            .Where(t => t.Id == orderTest.TestCatalogId).Select(t => t.Name).FirstAsync();
        ModalityName = await s.Radiology.Modalities.AsNoTracking()
            .Where(m => m.Id == Study.ModalityId).Select(m => m.Name).FirstAsync();

        var patient = await s.Reg.Patients.AsNoTracking().FirstAsync(p => p.Id == Study.PatientId);
        PatientName = patient.FullName;
        Uhid = patient.Uhid;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        AgeSex = $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)} · " +
                 Ui.SexWord(patient.Sex);

        var latest = await s.Lis.Results.AsNoTracking()
            .Where(r => r.OrderTestId == Study.OrderTestId)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync();
        if (latest is null)
        {
            At = Study.CreatedAt;
            return true;
        }

        Narrative = latest.Narrative;
        Version = latest.Version;
        Signed = latest.VerifiedAt is not null;
        At = latest.VerifiedAt ?? latest.EnteredAt;
        if (latest.VerifiedBy is { } verifierId)
        {
            VerifierName = await s.Auth.Users.AsNoTracking()
                .Where(u => u.Id == verifierId).Select(u => u.DisplayName).FirstOrDefaultAsync();
            VerifierLine = await s.Adm.ReportingConsultants.AsNoTracking()
                .Where(c => c.Name == VerifierName)
                .Select(c => c.Degrees + " · BMDC " + c.BmdcNo).FirstOrDefaultAsync();
        }

        Values = ResultValues.Parse(latest.Values)
            .Select(kv => new PrintedValue(kv.Key, kv.Value.Value, kv.Value.Unit,
                kv.Value.Flag, kv.Value.RefUsed))
            .ToList();
        return true;
    });
}
