using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Lis;

public sealed record ReportValue(string Name, string Value, string Unit, string Flag, string Range);
public sealed record ReportSection(
    string TestName, IReadOnlyList<ReportValue> Values, string? Narrative,
    bool Verified, string? VerifiedBy, DateTimeOffset? VerifiedAt, int Version, string? EsignHash,
    string? SignerName, string? SignerDegrees, string? SignerBmdc, int? SupersedesVersion);

/// <summary>
/// The investigation report (§8 N8 — hospitals treat report appearance as brand identity).
/// An unverified result has no final print: the page shows a provisional watermark instead of a
/// signature block, because "unverified reports unprintable as final" is a rule, not advice (§7 U7).
/// </summary>
[Authorize(Policy = Perm.LisWorklistRead)]
public class ReportModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public string OrderNo { get; private set; } = "";
    public string PatientName { get; private set; } = "";
    public string Uhid { get; private set; } = "";
    public string Age { get; private set; } = "";
    public char Sex { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CollectedAt { get; private set; }
    public long Due { get; private set; }
    public IReadOnlyList<ReportSection> Sections { get; private set; } = [];

    public bool FullyVerified => Sections.Count > 0 && Sections.All(s => s.Verified);

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var found = await tx.RunAsync(async s =>
        {
            var all = await LabBoard.LoadAsync(s, take: 500);
            var card = all.FirstOrDefault(c => c.OrderId == id);
            if (card is null) return false;

            OrderNo = card.OrderNo;
            PatientName = card.PatientName;
            Uhid = card.Uhid;
            CreatedAt = card.CreatedAt;
            Due = card.Due;

            var patient = await s.Reg.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Uhid == card.Uhid);
            if (patient is not null)
            {
                Sex = patient.Sex;
                Age = Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today);
            }

            var sampleIds = card.Samples.Select(x => x.Id).ToList();
            CollectedAt = await s.Lis.Samples.AsNoTracking()
                .Where(x => sampleIds.Contains(x.Id) && x.CollectedAt != null)
                .OrderBy(x => x.CollectedAt)
                .Select(x => x.CollectedAt)
                .FirstOrDefaultAsync();

            var catalogIds = card.Tests.Select(t => t.CatalogId).ToList();
            var catalog = await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => catalogIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

            var orderTestIds = card.Tests.Select(t => t.OrderTestId).ToList();
            var results = await s.Lis.Results.AsNoTracking()
                .Where(r => orderTestIds.Contains(r.OrderTestId)).ToListAsync();
            var verifierIds = results.Where(r => r.VerifiedBy != null)
                .Select(r => r.VerifiedBy!.Value).Distinct().ToList();
            var verifiers = await s.Auth.Users.AsNoTracking()
                .Where(u => verifierIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            // 5A-R1: the signature block is a master reference — the report reproduces the
            // consultant's credentials as they stood, not a free-text scribble.
            var consultants = await s.Adm.ReportingConsultants.AsNoTracking().ToDictionaryAsync(c => c.Id);

            var sections = new List<ReportSection>();
            foreach (var t in card.Tests)
            {
                var latest = results.Where(r => r.OrderTestId == t.OrderTestId)
                    .OrderByDescending(r => r.Version).FirstOrDefault();
                if (latest is null) continue;

                var template = ResultTemplates.Parse(catalog.GetValueOrDefault(t.CatalogId)?.Template);
                var stored = ResultValues.Parse(latest.Values);
                var values = (template?.Parameters ?? [])
                    .Where(p => stored.ContainsKey(p.Code))
                    .Select(p => new ReportValue(p.Name, stored[p.Code].Value, stored[p.Code].Unit,
                        stored[p.Code].Flag, stored[p.Code].RefUsed))
                    .ToList();

                Hms.Admin.Data.ReportingConsultant? signer = null;
                if (latest.SignatureImageRef?.StartsWith("consultant:") == true &&
                    long.TryParse(latest.SignatureImageRef["consultant:".Length..], out var cid))
                    consultants.TryGetValue(cid, out signer);

                sections.Add(new ReportSection(t.Name, values, latest.Narrative,
                    latest.VerifiedAt is not null,
                    latest.VerifiedBy is { } v ? verifiers.GetValueOrDefault(v) : null,
                    latest.VerifiedAt, latest.Version, latest.EsignHash,
                    signer?.Name, signer?.Degrees, signer?.BmdcNo, latest.SupersedesVersion));
            }
            Sections = sections;
            return true;
        });

        return found ? Page() : NotFound();
    }
}
