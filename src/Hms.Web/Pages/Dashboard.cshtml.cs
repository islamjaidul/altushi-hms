using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages;

public sealed record DayBar(DateOnly Day, long Net);
public sealed record DeptSlice(string Dept, long Amount);
public sealed record VarianceRow(string Counter, string Operator, DateOnly Day, long Variance);
public sealed record ConsultantRank(string Name, int Patients, long Income);
public sealed record DiscountRow(
    string InvoiceNo, string PatientName, long Discount, string ApprovedBy, string Reason);

/// <summary>
/// 05 §5 screen 16 — the closing screen of the demo. Every number is a link to the register it
/// came from: an MD who cannot drill into a figure does not trust it (§9A.2 module 6).
/// </summary>
[Authorize(Policy = Perm.DashboardRead)]
public class DashboardModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public DateOnly Today { get; private set; }
    public long IncomeToday { get; private set; }
    public long CollectedToday { get; private set; }
    public long OutstandingDue { get; private set; }
    public long DiscountToday { get; private set; }
    public int PatientsToday { get; private set; }
    public int InvoicesToday { get; private set; }
    public long VarianceToday { get; private set; }
    public int OpenCounters { get; private set; }
    public int PendingApprovals { get; private set; }
    public int LabInProgress { get; private set; }

    public IReadOnlyList<DayBar> Trend { get; private set; } = [];
    public IReadOnlyList<DeptSlice> DeptSplit { get; private set; } = [];
    public IReadOnlyList<VarianceRow> Variances { get; private set; } = [];
    public IReadOnlyList<DiscountRow> Discounts { get; private set; } = [];
    public IReadOnlyList<ConsultantRank> Ranking { get; private set; } = [];
    public long YesterdayNet { get; private set; }
    public long YesterdayCollected { get; private set; }

    public long TrendMax => Trend.Count == 0 ? 1 : Math.Max(1, Trend.Max(t => t.Net));
    public long DeptMax => DeptSplit.Count == 0 ? 1 : Math.Max(1, DeptSplit.Max(d => d.Amount));

    public async Task OnGetAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var from = Today.AddDays(-11);
        // Dhaka midnight expressed in UTC — Npgsql only binds UTC instants to timestamptz.
        var dayStart = Ui.DhakaMidnightUtc(Today);
        var windowStart = Ui.DhakaMidnightUtc(from);

        await tx.RunAsync(async s =>
        {
            var invoices = await s.Bill.Invoices.AsNoTracking()
                .Where(i => i.CreatedAt >= windowStart)
                .Select(i => new { i.Id, i.InvoiceNo, i.PatientId, i.Net, i.Discount, i.CreatedAt, i.DiscountApprovalId })
                .ToListAsync();

            var todays = invoices.Where(i => i.CreatedAt >= dayStart).ToList();
            IncomeToday = todays.Sum(i => i.Net);
            DiscountToday = todays.Sum(i => i.Discount);
            InvoicesToday = todays.Count;
            PatientsToday = todays.Select(i => i.PatientId).Distinct().Count();

            CollectedToday = await s.Bill.Receipts.AsNoTracking()
                .Where(r => r.At >= dayStart).SumAsync(r => (long?)r.Amount) ?? 0;

            OutstandingDue = await s.Bill.Dues.AsNoTracking()
                .Where(d => d.Balance > 0).SumAsync(d => (long?)d.Balance) ?? 0;

            // Twelve days, including the empty ones — a gap in a bar chart is information (edge 4).
            var byDay = invoices
                .GroupBy(i => DateOnly.FromDateTime(Ui.Local(i.CreatedAt).DateTime))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Net));
            Trend = Enumerable.Range(0, 12)
                .Select(offset => from.AddDays(offset))
                .Select(day => new DayBar(day, byDay.GetValueOrDefault(day)))
                .ToList();

            // Department split: charge lines carry the catalog reference, masters carry the dept.
            var invoiceIds = todays.Select(i => i.Id).ToList();
            var lines = await s.Bill.ChargeLines.AsNoTracking()
                .Where(c => c.InvoiceId != null && invoiceIds.Contains(c.InvoiceId!.Value))
                .Select(c => new { c.CatalogKind, c.CatalogId, c.Amount })
                .ToListAsync();
            var serviceDepts = await s.Adm.Services.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.Dept);
            var testDepts = await s.Adm.TestCatalog.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.Dept);

            DeptSplit = lines
                .GroupBy(l => l.CatalogKind == "service"
                    ? serviceDepts.GetValueOrDefault(l.CatalogId, "Other")
                    : testDepts.GetValueOrDefault(l.CatalogId, "Other"))
                .Select(g => new DeptSlice(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(d => d.Amount)
                .Take(7)
                .ToList();

            var openStates = new[] { "active", "opened", "reopened" };
            OpenCounters = await s.Bill.Sessions.CountAsync(x => openStates.Contains(x.State));

            PendingApprovals = await s.Kernel.ApprovalRequests.CountAsync(a => a.State == "pending");

            var labStates = new[] { "in_progress", "invoiced" };
            LabInProgress = await s.Diag.Orders.CountAsync(o => labStates.Contains(o.State));

            // Counter variance — the F2 fear, answered by name (§9A.1).
            var closes = await s.Bill.DayCloses.AsNoTracking()
                .Where(x => x.BusinessDay >= from)
                .OrderByDescending(x => x.Id).Take(10).ToListAsync();
            VarianceToday = closes.Where(c => c.BusinessDay == Today).Sum(c => c.Variance);

            var sessionIds = closes.Select(c => c.CounterSessionId).ToList();
            var sessions = await s.Bill.Sessions.AsNoTracking()
                .Where(x => sessionIds.Contains(x.Id)).ToListAsync();
            var counters = await s.Bill.Counters.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
            var opIds = sessions.Select(x => x.OperatorId).Distinct().ToList();
            var users = await s.Auth.Users.AsNoTracking()
                .Where(u => opIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            Variances = closes.Where(c => c.Variance != 0).Select(c =>
            {
                var sess = sessions.FirstOrDefault(x => x.Id == c.CounterSessionId);
                return new VarianceRow(
                    sess is null ? "—" : counters.GetValueOrDefault(sess.CounterId, "—"),
                    sess is null ? "—" : users.GetValueOrDefault(sess.OperatorId, "—"),
                    c.BusinessDay, c.Variance);
            }).ToList();

            // §5 M22 [M]: consultant ranking by income volume. Referring/ordering doctors carry
            // the charge line, so the ranking comes from the same money the dashboard totals.
            var doctorLines = await s.Bill.ChargeLines.AsNoTracking()
                .Where(c => c.InvoiceId != null && invoiceIds.Contains(c.InvoiceId!.Value) && c.DoctorId != null)
                .Select(c => new { DoctorId = c.DoctorId!.Value, c.Amount, c.EncounterId })
                .ToListAsync();
            var schedules = await s.Appt.Schedules.AsNoTracking()
                .Select(x => new { x.DoctorId, x.DoctorName }).ToListAsync();
            var doctorNames = schedules.GroupBy(x => x.DoctorId)
                .ToDictionary(g => g.Key, g => g.First().DoctorName);

            // Serials are the other half of a consultant's day — a doctor with no billed line
            // still saw patients, and the MD wants that visible.
            var todaysAppts = await s.Appt.Appointments.AsNoTracking()
                .Where(a => a.OnDate == Today && a.State != "cancelled")
                .Select(a => new { a.DoctorId, a.PatientId }).ToListAsync();

            Ranking = doctorNames.Keys
                .Union(todaysAppts.Select(a => a.DoctorId))
                .Select(id => new ConsultantRank(
                    doctorNames.GetValueOrDefault(id, "Doctor " + id),
                    todaysAppts.Count(a => a.DoctorId == id),
                    doctorLines.Where(l => l.DoctorId == id).Sum(l => l.Amount)))
                .Where(r => r.Patients > 0 || r.Income > 0)
                .OrderByDescending(r => r.Income).ThenByDescending(r => r.Patients)
                .Take(8).ToList();

            // §5 M22 [M] end-of-day digest: yesterday in one line, for the morning tea read.
            var yStart = Ui.DhakaMidnightUtc(Today.AddDays(-1));
            var yEnd = dayStart;
            YesterdayNet = invoices.Where(i => i.CreatedAt >= yStart && i.CreatedAt < yEnd).Sum(i => i.Net);
            YesterdayCollected = await s.Bill.Receipts.AsNoTracking()
                .Where(r => r.At >= yStart && r.At < yEnd).SumAsync(r => (long?)r.Amount) ?? 0;

            // Who discounted what, attributed by name — F2 again.
            var discounted = todays.Where(i => i.Discount > 0).Take(10).ToList();
            var approvalIds = discounted.Where(i => i.DiscountApprovalId is not null)
                .Select(i => i.DiscountApprovalId!.Value).ToList();
            var approvals = await s.Kernel.ApprovalRequests.AsNoTracking()
                .Where(a => approvalIds.Contains(a.Id)).ToListAsync();
            var deciderIds = approvals.Where(a => a.DecidedBy is not null)
                .Select(a => a.DecidedBy!.Value).Distinct().ToList();
            var deciders = await s.Auth.Users.AsNoTracking()
                .Where(u => deciderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
            var patientIds = discounted.Select(i => i.PatientId).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName);

            Discounts = discounted.Select(i =>
            {
                var approval = approvals.FirstOrDefault(a => a.Id == i.DiscountApprovalId);
                var by = approval?.DecidedBy is { } d ? deciders.GetValueOrDefault(d, "—") : "within limit";
                // US4.4: a discount is tied to a named person AND the reason they gave.
                return new DiscountRow(i.InvoiceNo, patients.GetValueOrDefault(i.PatientId, "—"),
                    i.Discount, by, approval?.Reason ?? "—");
            }).ToList();

            return 0;
        });
    }
}
