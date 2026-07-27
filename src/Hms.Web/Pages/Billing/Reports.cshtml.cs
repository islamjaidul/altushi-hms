using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record CollectionRow(string Label, int Receipts, long Amount);
public sealed record DeptIncomeRow(string Dept, int Items, long Amount);
public sealed record ReferrerRow(string Referrer, int Orders, long Business, short CommissionPct, long Commission);
public sealed record DiscountRegisterRow(
    string InvoiceNo, DateTimeOffset At, string Patient, long Gross, long Discount,
    string Operator, string AuthorisedBy, string Reason);
public sealed record DueAgeRow(string InvoiceNo, string Patient, string? Phone, long Balance, int DaysOld);

/// <summary>
/// §5 M4 [M] "daily collection by counter/operator/method, due list, discount register" and
/// §5 M8 [M] "department-wise income; consultant/referrer-wise business reports" — the
/// statements that were missing behind the day-close. §5 M22 [M]: every one is date-ranged.
/// </summary>
[Authorize(Policy = Perm.BillingSessionClose)]
public class ReportsModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string Range { get; set; } = "today";

    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }

    public IReadOnlyList<CollectionRow> ByMode { get; private set; } = [];
    public IReadOnlyList<CollectionRow> ByCounter { get; private set; } = [];
    public IReadOnlyList<CollectionRow> ByOperator { get; private set; } = [];
    public IReadOnlyList<DeptIncomeRow> ByDept { get; private set; } = [];
    public IReadOnlyList<ReferrerRow> ByReferrer { get; private set; } = [];
    public IReadOnlyList<DiscountRegisterRow> DiscountRegister { get; private set; } = [];
    public IReadOnlyList<DueAgeRow> OutstandingDues { get; private set; } = [];

    public long TotalCollected => ByMode.Sum(r => r.Amount);
    public long TotalGross { get; private set; }
    public long TotalDiscount { get; private set; }
    public long TotalDue => OutstandingDues.Sum(d => d.Balance);
    public int InvoiceCount { get; private set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        (FromDate, ToDate) = Range switch
        {
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "week" => (today.AddDays(-6), today),
            "month" => (new DateOnly(today.Year, today.Month, 1), today),
            "custom" => (From ?? today, To ?? today),
            _ => (today, today),
        };
        if (ToDate < FromDate) (FromDate, ToDate) = (ToDate, FromDate);

        var start = Ui.DhakaMidnightUtc(FromDate);
        var end = Ui.DhakaMidnightUtc(ToDate.AddDays(1));

        await tx.RunAsync(async s =>
        {
            var receipts = await s.Bill.Receipts.AsNoTracking()
                .Where(r => r.At >= start && r.At < end).ToListAsync();
            var invoices = await s.Bill.Invoices.AsNoTracking()
                .Where(i => i.CreatedAt >= start && i.CreatedAt < end).ToListAsync();

            InvoiceCount = invoices.Count;
            TotalGross = invoices.Sum(i => i.Gross);
            TotalDiscount = invoices.Sum(i => i.Discount);

            ByMode = receipts.GroupBy(r => r.Tender)
                .Select(g => new CollectionRow(Ui.Humanize(g.Key), g.Count(), g.Sum(r => r.Amount)))
                .OrderByDescending(r => r.Amount).ToList();

            var sessionIds = receipts.Select(r => r.CounterSessionId).Distinct().ToList();
            var sessions = await s.Bill.Sessions.AsNoTracking()
                .Where(x => sessionIds.Contains(x.Id)).ToListAsync();
            var counters = await s.Bill.Counters.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
            var operatorIds = receipts.Select(r => r.OperatorId).Distinct().ToList();
            var users = await s.Auth.Users.AsNoTracking()
                .Where(u => operatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            ByCounter = receipts
                .GroupBy(r => sessions.FirstOrDefault(x => x.Id == r.CounterSessionId) is { } sess
                    ? counters.GetValueOrDefault(sess.CounterId, "—") : "—")
                .Select(g => new CollectionRow(g.Key, g.Count(), g.Sum(r => r.Amount)))
                .OrderByDescending(r => r.Amount).ToList();

            ByOperator = receipts
                .GroupBy(r => users.GetValueOrDefault(r.OperatorId, "—"))
                .Select(g => new CollectionRow(g.Key, g.Count(), g.Sum(r => r.Amount)))
                .OrderByDescending(r => r.Amount).ToList();

            // Department income: the charge line carries the catalog ref, masters carry the dept.
            var invoiceIds = invoices.Select(i => i.Id).ToList();
            var lines = await s.Bill.ChargeLines.AsNoTracking()
                .Where(c => c.InvoiceId != null && invoiceIds.Contains(c.InvoiceId!.Value))
                .Select(c => new { c.CatalogKind, c.CatalogId, c.Amount, c.ReferrerId })
                .ToListAsync();
            var serviceDepts = await s.Adm.Services.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Dept);
            var testDepts = await s.Adm.TestCatalog.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Dept);

            ByDept = lines
                .GroupBy(l => l.CatalogKind == "service"
                    ? serviceDepts.GetValueOrDefault(l.CatalogId, "Other")
                    : testDepts.GetValueOrDefault(l.CatalogId, "Other"))
                .Select(g => new DeptIncomeRow(g.Key, g.Count(), g.Sum(x => x.Amount)))
                .OrderByDescending(r => r.Amount).ToList();

            // Referrer business (§5 M8 [M]) — commission is shown as an accrual, not a payable;
            // payouts belong to M19, which §9A.3 defers.
            var referrers = await s.Adm.Referrers.AsNoTracking().ToDictionaryAsync(r => r.Id);
            ByReferrer = lines.Where(l => l.ReferrerId is not null)
                .GroupBy(l => l.ReferrerId!.Value)
                .Select(g =>
                {
                    referrers.TryGetValue(g.Key, out var r);
                    var business = g.Sum(x => x.Amount);
                    var pct = r?.CommissionPercent ?? 0;
                    return new ReferrerRow(r?.Name ?? "—", g.Count(), business, pct,
                        (long)Math.Floor(business * pct / 100m + 0.5m));
                })
                .OrderByDescending(r => r.Business).ToList();

            // Discount register (§5 M4 [M] / US4.4): who, how much, and why.
            var discounted = invoices.Where(i => i.Discount > 0).ToList();
            var approvalIds = discounted.Where(i => i.DiscountApprovalId is not null)
                .Select(i => i.DiscountApprovalId!.Value).ToList();
            var approvals = await s.Kernel.ApprovalRequests.AsNoTracking()
                .Where(a => approvalIds.Contains(a.Id)).ToListAsync();
            var actorIds = approvals.SelectMany(a => new[] { a.RequestedBy, a.DecidedBy ?? a.RequestedBy })
                .Concat(discounted.Select(i => i.CreatedBy)).Distinct().ToList();
            var actors = await s.Auth.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
            var patientIds = discounted.Select(i => i.PatientId)
                .Concat(invoices.Select(i => i.PatientId)).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            DiscountRegister = discounted.Select(i =>
            {
                var a = approvals.FirstOrDefault(x => x.Id == i.DiscountApprovalId);
                return new DiscountRegisterRow(i.InvoiceNo, i.CreatedAt,
                    patients.TryGetValue(i.PatientId, out var p) ? p.FullName : "—",
                    i.Gross, i.Discount,
                    actors.GetValueOrDefault(i.CreatedBy, "—"),
                    a?.DecidedBy is { } d ? actors.GetValueOrDefault(d, "—") : "within limit",
                    a?.Reason ?? "—");
            }).OrderByDescending(r => r.At).ToList();

            // Outstanding dues with age — the follow-up list, not a snapshot of today only.
            var open = await (
                from due in s.Bill.Dues
                join inv in s.Bill.Invoices on due.InvoiceId equals inv.Id
                where due.Balance > 0
                select new { inv.InvoiceNo, inv.PatientId, due.Balance, inv.CreatedAt })
                .ToListAsync();
            var duePatientIds = open.Select(o => o.PatientId).Distinct().ToList();
            var duePatients = await s.Reg.Patients.AsNoTracking()
                .Where(p => duePatientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            OutstandingDues = open.Select(o =>
            {
                duePatients.TryGetValue(o.PatientId, out var p);
                var age = (int)(clock.GetUtcNow() - o.CreatedAt).TotalDays;
                return new DueAgeRow(o.InvoiceNo, p?.FullName ?? "—", p?.Phone, o.Balance, Math.Max(0, age));
            }).OrderByDescending(d => d.DaysOld).Take(60).ToList();

            return 0;
        });
    }
}
