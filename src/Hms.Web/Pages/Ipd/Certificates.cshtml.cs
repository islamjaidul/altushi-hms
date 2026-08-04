using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Hms.Ipd;
using Hms.Ipd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record CertRow(
    long Id, string CertNo, string Kind, string Patient, string AdmissionNo,
    DateTimeOffset IssuedAt, int PrintCount, string Body);
public sealed record EligibleAdmission(
    long Id, string AdmissionNo, string Patient, string State, string Kinds);

/// <summary>
/// §5 M6 [M]: discharge/death/birth certificates — sequential numbers from the kernel series,
/// frozen jsonb body, and a counted, audited reprint (never a re-issue).
/// </summary>
[Authorize(Policy = Perm.IpdSettle)]
public class CertificatesModel(HmsTx tx, CertificateService certs, TimeProvider clock) : HmsPageModel
{
    [BindProperty] public long AdmissionId { get; set; }
    [BindProperty] public string Kind { get; set; } = "discharge";
    [BindProperty] public string? Extra { get; set; }
    // Spec 0047: the operator reviews and edits what the document will say, pre-issue.
    // Blank falls back to the summary recorded at discharge; post-issue stays frozen.
    [BindProperty, StringLength(10000, ErrorMessage = "Clinical summary — at most 10000 characters")]
    public string? ClinicalSummary { get; set; }
    [BindProperty] public DateOnly? FollowUpOn { get; set; }
    [BindProperty] public long CertificateId { get; set; }

    public IReadOnlyList<CertRow> Rows { get; private set; } = [];
    public IReadOnlyList<EligibleAdmission> Eligible { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var certificates = await s.Ipd.Certificates.AsNoTracking()
                .OrderByDescending(c => c.Id).Take(50).ToListAsync();
            var admissionIds = certificates.Select(c => c.AdmissionId).Distinct().ToList();
            var admissions = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => admissionIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
            var patientIds = admissions.Values.Select(a => a.PatientId).Distinct().ToList();

            // Certificates can be issued for any settled/closed admission.
            var eligibleStates = new[]
            {
                AdmissionState.FinanciallySettled, AdmissionState.Discharged,
                AdmissionState.Death, AdmissionState.Absconded, AdmissionState.Admitted,
                AdmissionState.DischargeInitiated, AdmissionState.ClinicallyCleared,
            };
            var eligible = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => eligibleStates.Contains(a.State))
                .OrderByDescending(a => a.Id).Take(50).ToListAsync();
            patientIds = patientIds.Union(eligible.Select(a => a.PatientId)).Distinct().ToList();

            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName);

            Rows = certificates.Select(c =>
            {
                var admission = admissions.GetValueOrDefault(c.AdmissionId);
                return new CertRow(c.Id, c.CertNo, c.Kind,
                    admission is not null ? patients.GetValueOrDefault(admission.PatientId, "—") : "—",
                    admission?.AdmissionNo ?? "—", c.IssuedAt, c.PrintCount, c.Body);
            }).ToList();

            Eligible = eligible.Select(a => new EligibleAdmission(a.Id, a.AdmissionNo,
                patients.GetValueOrDefault(a.PatientId, "—"), a.State, KindsFor(a.State))).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostIssueAsync()
    {
        if (AdmissionId == 0) { await LoadAsync(); Fail("Pick the admission."); return Page(); }
        long certId;
        try
        {
            certId = await tx.RunAsync(async s =>
            {
                var admission = await s.Ipd.Admissions.AsNoTracking()
                                    .SingleOrDefaultAsync(a => a.Id == AdmissionId)
                                ?? throw new IpdException("Unknown admission.");
                // Spec 0047: the same rules CertificateService enforces, phrased as guidance
                // before the operator commits — the dropdown is filtered too (data-kinds), so
                // this only fires on a stale or hand-crafted post.
                if (Kind == "discharge" && admission.State is not
                        (AdmissionState.FinanciallySettled or AdmissionState.Discharged))
                    throw new IpdException("A discharge certificate needs a settled admission — "
                                           + "finish settlement at the IPD billing counter first.");
                if (Kind == "death" && admission.State != AdmissionState.Death)
                    throw new IpdException("A death certificate needs a death-recorded admission.");

                var patient = await s.Reg.Patients.AsNoTracking()
                    .SingleAsync(p => p.Id == admission.PatientId);
                var body = JsonSerializer.Serialize(new
                {
                    patient = patient.FullName,
                    uhid = patient.Uhid,
                    admissionNo = admission.AdmissionNo,
                    admitted = Ui.Local(admission.AdmittedAt).ToString("dd MMM yyyy"),
                    closed = admission.DischargedAt is DateTimeOffset gone
                        ? Ui.Local(gone).ToString("dd MMM yyyy") : null,
                    summary = string.IsNullOrWhiteSpace(ClinicalSummary)
                        ? admission.ClinicalSummary : ClinicalSummary.Trim(),
                    extra = string.IsNullOrWhiteSpace(Extra) ? null : Extra.Trim(),
                    followUp = FollowUpOn?.ToString("dd MMM yyyy"),
                    issuedOn = Ui.Local(clock.GetUtcNow()).ToString("dd MMM yyyy"),
                });
                var cert = await certs.IssueAsync(s.Ipd, s.Kernel, BranchId, AdmissionId, Kind,
                    body, ActorId, ActorName);
                return cert.Id;
            });
        }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Certificate issued with its sequential number", "verified");
        // The screen IS the preview — land on the document, ready to print (spec 0047).
        return Redirect($"/ipd/certificates/{certId}");
    }

    public async Task<IActionResult> OnPostReprintAsync()
    {
        try
        {
            await tx.RunAsync(s => certs.ReprintAsync(s.Ipd, s.Kernel, CertificateId, ActorId, ActorName));
        }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Reprint counted and audited", "print");
        return Redirect($"/ipd/certificates/{CertificateId}");
    }

    /// <summary>Which certificate kinds this admission state can legally receive (spec 0047) —
    /// mirrors CertificateService's rules so the dropdown never offers a refused combination.</summary>
    private static string KindsFor(string state) => state switch
    {
        AdmissionState.Death => "death birth",
        AdmissionState.FinanciallySettled or AdmissionState.Discharged => "discharge birth",
        _ => "birth",
    };
}
