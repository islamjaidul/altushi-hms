using Hms.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record DueRow(
    long InvoiceId, string InvoiceNo, string PatientName, string Uhid, string? Phone,
    long Billed, long Balance, DateTimeOffset CreatedAt);

/// <summary>
/// 05 §5 screen 6. Collection locks the due row for the transaction (ADR-0015 #2), so two
/// counters collecting the same invoice at the same moment serialize — over-collection is
/// impossible rather than merely unlikely (§8 N4).
/// </summary>
[Authorize(Policy = Perm.BillingReceiptCreate)]
public class DuesModel(HmsTx tx, BillingService billing, Hms.Lis.LisService lis, TimeProvider clock)
    : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public long InvoiceId { get; set; }
    [BindProperty] public long Amount { get; set; }
    [BindProperty] public string Tender { get; set; } = "cash";

    public OpenSession? Session { get; private set; }
    public IReadOnlyList<DueRow> Rows { get; private set; } = [];
    public long TotalOutstanding { get; private set; }
    public long CollectedToday { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        (Session, Rows, TotalOutstanding, CollectedToday) = await tx.RunAsync(async s =>
        {
            var session = await CounterContext.FindOpenAsync(s.Bill, ActorId);

            // The search contract (ADR-0020): the predicate goes into the SQL query, so an old
            // due is as findable as a new one — never fetch-then-filter. reg.* and bill.* are
            // separate contexts on one connection (ADR-0003), so the patient predicate resolves
            // to ids in reg first, and the display join happens in memory.
            var term = Q?.Trim();
            List<long>? matchedPatients = null;
            if (!string.IsNullOrWhiteSpace(term))
            {
                matchedPatients = await s.Reg.Patients.AsNoTracking()
                    .Matching(term)                                    // spec 0020
                    .Select(p => p.Id).Take(500).ToListAsync();
            }

            var openQuery =
                from d in s.Bill.Dues
                join i in s.Bill.Invoices on d.InvoiceId equals i.Id
                where d.Balance > 0
                select new { i.Id, i.InvoiceNo, i.PatientId, i.Net, d.Balance, i.CreatedAt };
            if (matchedPatients is not null)
            {
                var like = $"%{term}%";
                openQuery = openQuery.Where(x =>
                    EF.Functions.ILike(x.InvoiceNo, like) || matchedPatients.Contains(x.PatientId));
            }
            var open = await openQuery
                .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync();

            var patientIds = open.Select(o => o.PatientId).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            var rows = open
                .Select(o =>
                {
                    patients.TryGetValue(o.PatientId, out var p);
                    return new DueRow(o.Id, o.InvoiceNo, p?.FullName ?? "(unknown)", p?.Uhid ?? "—",
                        p?.Phone, o.Net, o.Balance, o.CreatedAt);
                })
                .ToList();
            var total = await s.Bill.Dues.Where(d => d.Balance > 0).SumAsync(d => (long?)d.Balance) ?? 0;

            long collected = 0;
            if (session is not null)
                collected = await s.Bill.Receipts
                    .Where(r => r.CounterSessionId == session.Id && r.Amount > 0)
                    .SumAsync(r => (long?)r.Amount) ?? 0;

            return (session, (IReadOnlyList<DueRow>)rows, total, collected);
        });
    }

    public async Task<IActionResult> OnPostCollectAsync()
    {
        await LoadAsync();

        if (Session is null) { Fail("Open your counter before collecting."); return Page(); }
        var row = Rows.FirstOrDefault(r => r.InvoiceId == InvoiceId);
        if (row is null) { Fail("That invoice has no outstanding balance any more — refresh."); return Page(); }
        if (Amount <= 0) { Fail("Enter the amount being paid."); return Page(); }

        try
        {
            var released = await tx.RunAsync(async s =>
            {
                await billing.CollectAsync(
                    s.Bill, s.Kernel, BranchId, InvoiceId, Session!.Id, Amount, Tender, null,
                    ActorId, ActorName);

                // Settling the balance is the same release trigger as paying at the counter
                // (§9A.2 seam) — without this a part-paid test order would never reach the lab.
                return await DiagnosticsRelease.ReleasePaidOrdersAsync(
                    s, billing, lis, clock, BranchId, InvoiceId, ActorId);
            });

            Toast(released > 0
                    ? $"Collected {Ui.Money(Amount)} — test order released to the lab, labels ready"
                    : $"Collected {Ui.Money(Amount)} from {row.PatientName}",
                  "payments");
            return Redirect($"/billing/invoice/{InvoiceId}");
        }
        catch (BillingException e)
        {
            Fail(e.Message);
            return Page();
        }
    }
}
