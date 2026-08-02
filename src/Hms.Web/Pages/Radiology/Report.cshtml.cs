using System.Globalization;
using Hms.Lis;
using Hms.Radiology;
using Hms.Radiology.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Radiology;

public sealed record ReportParameter(string Code, string Name, string Unit, string? Value);

/// <summary>
/// US10.2: open the study, apply the exam template, edit findings, sign — one flow. The report
/// is stored as the order test's result, so everything M9 guarantees about results (amendment
/// versions, e-sign, delivery) applies without a second implementation.
/// </summary>
[Authorize(Policy = Perm.RadiologyReportWrite)]
public class ReportModel(
    HmsTx tx, RadiologyService radiology, LisService lis,
    Hms.Notifications.SmsQueue sms, OrgIdentity hospital) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty] public Dictionary<string, string> Values { get; set; } = [];
    [BindProperty] public string? Findings { get; set; }
    [BindProperty] public string? Impression { get; set; }

    public Study? Study { get; private set; }
    public string PatientName { get; private set; } = "—";
    public string Uhid { get; private set; } = "—";
    public string TestName { get; private set; } = "—";
    public string ModalityName { get; private set; } = "—";
    public IReadOnlyList<ReportParameter> Parameters { get; private set; } = [];
    public bool HasResult { get; private set; }
    public bool Signed { get; private set; }
    public string? ExistingNarrative { get; private set; }

    public async Task<IActionResult> OnGetAsync()
        => await LoadAsync() ? Page() : NotFound();

    private async Task<bool> LoadAsync() => await tx.RunAsync(async s =>
    {
        Study = await s.Radiology.Studies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Id);
        if (Study is null) return false;

        var orderTest = await s.Diag.OrderTests.AsNoTracking()
            .FirstAsync(t => t.Id == Study.OrderTestId);
        var catalogItem = await s.Adm.TestCatalog.AsNoTracking()
            .FirstAsync(t => t.Id == orderTest.TestCatalogId);
        TestName = catalogItem.Name;
        ModalityName = await s.Radiology.Modalities.AsNoTracking()
            .Where(m => m.Id == Study.ModalityId).Select(m => m.Name).FirstAsync();

        var patient = await s.Reg.Patients.AsNoTracking().FirstAsync(p => p.Id == Study.PatientId);
        PatientName = patient.FullName;
        Uhid = patient.Uhid;

        var latest = await s.Lis.Results.AsNoTracking()
            .Where(r => r.OrderTestId == Study.OrderTestId)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync();
        HasResult = latest is not null;
        Signed = latest?.VerifiedAt is not null;
        ExistingNarrative = latest?.Narrative;

        // 5A-10: the exam's own parameter grid, edited in admin, applied here.
        var template = ResultTemplates.Parse(catalogItem.Template);
        var stored = ResultValues.Parse(latest?.Values);
        Parameters = (template?.Parameters ?? [])
            .Select(p => new ReportParameter(p.Code, p.Name, p.Unit,
                stored.TryGetValue(p.Code, out var v) ? v.Value : null))
            .ToList();
        return true;
    });

    public async Task<IActionResult> OnPostSaveAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var study = await RadiologyService.GetAsync(s.Radiology, Id);
                var orderTest = await s.Diag.OrderTests.AsNoTracking()
                    .FirstAsync(t => t.Id == study.OrderTestId);
                var catalogItem = await s.Adm.TestCatalog.AsNoTracking()
                    .FirstAsync(t => t.Id == orderTest.TestCatalogId);
                var patient = await s.Reg.Patients.AsNoTracking()
                    .FirstAsync(p => p.Id == study.PatientId);

                var template = ResultTemplates.Parse(catalogItem.Template);
                var values = new Dictionary<string, object>();
                foreach (var p in template?.Parameters ?? [])
                {
                    if (!Values.TryGetValue(p.Code, out var raw) || string.IsNullOrWhiteSpace(raw))
                        continue;
                    decimal? numeric = decimal.TryParse(raw, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : null;
                    var band = p.BandFor(patient.Sex, patient.AgeYears);
                    values[p.Code] = new
                    {
                        value = raw.Trim(),
                        unit = p.Unit,
                        flag = ResultTemplates.Flag(numeric, band),
                        ref_used = band is null ? "—" : $"{band.Text} ({band.Label})",
                    };
                }

                await RadiologyReporting.SaveReportAsync(s, radiology, lis, study, values,
                    Narrative(), ActorId);
                return 0;
            });
            Toast("Report saved — not signed yet", "save");
            return Redirect($"/radiology/report/{Id}");
        }
        catch (Exception e) when (e is RadiologyException or LisException)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSignAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var study = await RadiologyService.GetAsync(s.Radiology, Id);
                await RadiologyReporting.SignAsync(s, lis, study, ActorId, "reporting_consultant");

                // WP4 (AUD-M10-01): ReportReady used to fire only from /lis/verify, so a
                // radiologist signing on their own screen notified nobody. Same event, same
                // template, same switch — the patient cannot tell which door the report
                // left by, and should not have to.
                var orderTest = await s.Diag.OrderTests.AsNoTracking()
                    .FirstAsync(t => t.Id == study.OrderTestId);
                var patient = await s.Reg.Patients.AsNoTracking()
                    .FirstAsync(p => p.Id == study.PatientId);
                await SmsSender.SendAsync(s, sms, BranchId,
                    Hms.Notifications.Data.SmsEvent.ReportReady, patient.Phone,
                    new Dictionary<string, string?>
                    {
                        ["hospital"] = hospital.Name,
                        ["patient"] = patient.FullName,
                        ["order"] = "LB-" + orderTest.TestOrderId.ToString("D5"),
                    });
                return 0;
            });
            Toast("Report signed — it is now deliverable, patient notified", "verified");
            return Redirect($"/radiology/print/{Id}");
        }
        catch (Exception e) when (e is RadiologyException or LisException)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    /// <summary>Findings and impression are one narrative on the sheet, and read as one.</summary>
    private string Narrative()
    {
        var findings = (Findings ?? "").Trim();
        var impression = (Impression ?? "").Trim();
        if (impression.Length == 0) return findings;
        return findings.Length == 0
            ? $"IMPRESSION: {impression}"
            : $"{findings}\n\nIMPRESSION: {impression}";
    }
}
