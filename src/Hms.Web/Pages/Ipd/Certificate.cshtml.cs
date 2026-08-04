using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

/// <summary>
/// Spec 0047: the certificate as a document. The screen IS the preview (same contract as the
/// ID card and the lab report), and it renders the frozen jsonb body verbatim — an old
/// certificate shows exactly what was printed then, never a recomputation. Reprint counting
/// stays on the list page's handler; this page only reads.
/// </summary>
[Authorize(Policy = Perm.IpdSettle)]
public class CertificateModel(HmsTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    public bool Missing { get; private set; }
    public string CertNo { get; private set; } = "—";
    public string Kind { get; private set; } = "discharge";
    public DateTimeOffset IssuedAt { get; private set; }
    public int PrintCount { get; private set; }
    public string? Patient { get; private set; }
    public string? Uhid { get; private set; }
    public string? AdmissionNo { get; private set; }
    public string? Admitted { get; private set; }
    public string? Closed { get; private set; }
    public string? Summary { get; private set; }
    public string? Extra { get; private set; }
    public string? FollowUp { get; private set; }
    public string? IssuedOn { get; private set; }

    public string DocTitle => Kind switch
    {
        "death" => "Death Certificate",
        "birth" => "Birth Certificate",
        _ => "Discharge Certificate",
    };

    public async Task OnGetAsync()
    {
        await tx.RunAsync(async s =>
        {
            var cert = await s.Ipd.Certificates.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == Id);
            if (cert is null) { Missing = true; return 0; }
            CertNo = cert.CertNo;
            Kind = cert.Kind;
            IssuedAt = cert.IssuedAt;
            PrintCount = cert.PrintCount;
            using var body = JsonDocument.Parse(cert.Body);
            string? Str(string name) => body.RootElement.TryGetProperty(name, out var el)
                                        && el.ValueKind == JsonValueKind.String
                ? el.GetString() : null;
            Patient = Str("patient");
            Uhid = Str("uhid");
            AdmissionNo = Str("admissionNo");
            Admitted = Str("admitted");
            Closed = Str("closed");
            Summary = Str("summary");
            Extra = Str("extra");
            FollowUp = Str("followUp");      // absent on certificates issued before 0047
            IssuedOn = Str("issuedOn");
            return 0;
        });
        if (Missing) Fail("No such certificate.");
    }
}
