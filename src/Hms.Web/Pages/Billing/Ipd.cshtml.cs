using Hms.Billing;
using Hms.Ipd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record IpdQueueRow(
    long AdmissionId, string AdmissionNo, string Patient, string State,
    long DraftCharges, long AdvanceHeld, long SettledDue);
public sealed record IpdInvoiceRow(long Id, string InvoiceNo, string Patient, long Net, long Balance);

/// <summary>
/// Spec 0048: the IPD cashier's workspace. The folio keeps the running truth and the discharge
/// screen moves the states — this page is where the cashier stands: the IPD drawer's status
/// and the discharge pipeline it serves, with the money position per admission. It writes
/// nothing; every action is a pointer into the screen that owns it (same posture as the
/// nursing station).
/// </summary>
[Authorize(Policy = Perm.IpdSettle)]
public class IpdModel(HmsTx tx, BillingService billing) : HmsPageModel
{
    public OpenSession? Session { get; private set; }
    public IReadOnlyList<IpdQueueRow> Queue { get; private set; } = [];
    public IReadOnlyList<IpdInvoiceRow> Invoices { get; private set; } = [];

    public async Task OnGetAsync() => await tx.RunAsync(async s =>
    {
        Session = await CounterContext.FindOpenIpdAsync(s.Bill, ActorId);

        // ---- the discharge pipeline, with each admission's money position ------------------
        var pipeline = new[]
        {
            AdmissionState.DischargeInitiated, AdmissionState.ClinicallyCleared,
            AdmissionState.FinanciallySettled,
        };
        var admissions = await s.Ipd.Admissions.AsNoTracking()
            .Where(a => pipeline.Contains(a.State))
            .OrderBy(a => a.Id).Take(60).ToListAsync();
        var admissionIds = admissions.Select(a => a.Id).ToList();
        var folios = await s.Ipd.Folios.AsNoTracking()
            .Where(f => admissionIds.Contains(f.AdmissionId)).ToListAsync();
        var patientIds = admissions.Select(a => a.PatientId).Distinct().ToList();
        var patients = await s.Reg.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName);

        var folioIds = folios.Select(f => f.Id).ToList();
        var draftSums = await s.Bill.ChargeLines.AsNoTracking()
            .Where(c => c.FolioId != null && folioIds.Contains(c.FolioId!.Value)
                        && c.InvoiceId == null)
            .GroupBy(c => c.FolioId!.Value)
            .Select(g => new { FolioId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.FolioId, x => x.Amount);

        var rows = new List<IpdQueueRow>();
        foreach (var a in admissions)
        {
            var folio = folios.FirstOrDefault(f => f.AdmissionId == a.Id);
            long advance = 0, due = 0;
            if (folio is not null)
            {
                advance = await billing.AdvanceHeldAsync(s.Bill, folio.Id);
                if (folio.SettlementInvoiceId is long inv)
                    due = await s.Bill.Dues.AsNoTracking()
                        .Where(d => d.InvoiceId == inv).Select(d => d.Balance).FirstOrDefaultAsync();
            }
            rows.Add(new IpdQueueRow(a.Id, a.AdmissionNo,
                patients.GetValueOrDefault(a.PatientId, "—"), a.State,
                folio is null ? 0 : draftSums.GetValueOrDefault(folio.Id, 0), advance, due));
        }
        Queue = rows;

        // ---- recent settlement invoices (folio-parented = IPD by definition) ---------------
        var invoices = await s.Bill.Invoices.AsNoTracking()
            .Where(i => i.FolioId != null)
            .OrderByDescending(i => i.Id).Take(15).ToListAsync();
        var invIds = invoices.Select(i => i.Id).ToList();
        var balances = await s.Bill.Dues.AsNoTracking()
            .Where(d => invIds.Contains(d.InvoiceId))
            .ToDictionaryAsync(d => d.InvoiceId, d => d.Balance);
        var invFolioIds = invoices.Select(i => i.FolioId!.Value).Distinct().ToList();
        var invFolios = await s.Ipd.Folios.AsNoTracking()
            .Where(f => invFolioIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.PatientId);
        var morePatientIds = invFolios.Values.Distinct()
            .Where(id => !patients.ContainsKey(id)).ToList();
        if (morePatientIds.Count > 0)
            foreach (var extra in await s.Reg.Patients.AsNoTracking()
                         .Where(p => morePatientIds.Contains(p.Id))
                         .Select(p => new { p.Id, p.FullName }).ToListAsync())
                patients[extra.Id] = extra.FullName;

        Invoices = invoices.Select(i => new IpdInvoiceRow(i.Id, i.InvoiceNo,
            invFolios.TryGetValue(i.FolioId!.Value, out var pid)
                ? patients.GetValueOrDefault(pid, "—") : "—",
            i.Net, balances.GetValueOrDefault(i.Id, 0))).ToList();
        return 0;
    });
}
