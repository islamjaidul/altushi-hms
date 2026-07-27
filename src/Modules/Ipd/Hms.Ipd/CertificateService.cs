using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Ipd;

/// <summary>
/// §5 M6: discharge/death/birth certificates carry sequential numbers from the kernel series
/// and every reprint is counted and audited — a certificate is a legal document, so the body
/// is a frozen jsonb snapshot of exactly what was printed, never recomputed.
/// </summary>
public sealed class CertificateService(
    NumberSeriesService numbers, AuditWriter audit, FiscalCalendar fiscal, TimeProvider clock)
{
    private static readonly Dictionary<string, (string Series, string Pattern)> Kinds = new()
    {
        ["discharge"] = ("cert-discharge", "DIS-{fy}-{n:D4}"),
        ["death"] = ("cert-death", "DTH-{fy}-{n:D4}"),
        ["birth"] = ("cert-birth", "BIR-{fy}-{n:D4}"),
    };

    public async Task<Certificate> IssueAsync(
        IpdDbContext ipd, KernelDbContext kernel, long branchId, long admissionId, string kind,
        string bodyJson, long actorId, string actorName, CancellationToken ct = default)
    {
        if (!Kinds.TryGetValue(kind, out var series))
            throw new IpdException("Certificate kind must be discharge, death or birth.");

        var admission = await ipd.Admissions.AsNoTracking()
                            .SingleOrDefaultAsync(a => a.Id == admissionId, ct)
                        ?? throw new IpdException("Unknown admission.");
        if (kind == "death" && admission.State != AdmissionState.Death)
            throw new IpdException("A death certificate needs a death-recorded admission.");
        if (kind == "discharge" && admission.State is not
                (AdmissionState.FinanciallySettled or AdmissionState.Discharged))
            throw new IpdException("A discharge certificate needs a settled admission.");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (_, certNo) = await numbers.IssueAsync(
            kernel, branchId, series.Series, fiscal.FiscalYearOf(today), series.Pattern, ct);

        var cert = new Certificate
        {
            BranchId = branchId, AdmissionId = admissionId, Kind = kind, CertNo = certNo,
            Body = bodyJson, IssuedAt = clock.GetUtcNow(), IssuedBy = actorId, PrintCount = 1,
        };
        ipd.Certificates.Add(cert);
        await ipd.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName,
            "ipd.certificate.issue", "ipd.certificate", cert.Id,
            after: new { certNo, kind, admissionId }, tier: 2);
        await kernel.SaveChangesAsync(ct);
        return cert;
    }

    /// <summary>Reprint never re-issues: same number, bumped count, audit row (§5 M6 [M]).</summary>
    public async Task<Certificate> ReprintAsync(
        IpdDbContext ipd, KernelDbContext kernel, long certificateId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var cert = await ipd.Certificates.SingleOrDefaultAsync(c => c.Id == certificateId, ct)
                   ?? throw new IpdException("Unknown certificate.");
        cert.PrintCount++;
        await ipd.SaveChangesAsync(ct);

        audit.Append(kernel, cert.BranchId, actorId, actorName,
            "ipd.certificate.reprint", "ipd.certificate", cert.Id,
            after: new { cert.CertNo, cert.PrintCount });
        await kernel.SaveChangesAsync(ct);
        return cert;
    }
}
