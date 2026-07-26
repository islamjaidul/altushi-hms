using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record ReceiptLine(string Description, int Qty, long Amount);
public sealed record ReceiptPayment(string ReceiptNo, long Amount, string Tender, DateTimeOffset At);

/// <summary>
/// The money receipt (§9A.2 module 3). It renders from the invoice's frozen lines, never from a
/// recomputation — a price change tomorrow cannot alter what this document says (C6, edge 13).
/// </summary>
[Authorize(Policy = Perm.RegistrationRead)]
public class InvoiceModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public string InvoiceNo { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public string PatientName { get; private set; } = "";
    public string Uhid { get; private set; } = "";
    public string Age { get; private set; } = "";
    public char Sex { get; private set; }
    public string? Phone { get; private set; }
    public string CounterName { get; private set; } = "";
    public string OperatorName { get; private set; } = "";
    public long Gross { get; private set; }
    public long Discount { get; private set; }
    public long Net { get; private set; }
    public long Paid { get; private set; }
    public long Due { get; private set; }
    public string State { get; private set; } = "";
    public IReadOnlyList<ReceiptLine> Lines { get; private set; } = [];
    public IReadOnlyList<ReceiptPayment> Payments { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var found = await tx.RunAsync(async s =>
        {
            var invoice = await s.Bill.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
            if (invoice is null) return false;

            var patient = await s.Reg.Patients.AsNoTracking().SingleAsync(p => p.Id == invoice.PatientId);
            var session = await s.Bill.Sessions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == invoice.CounterSessionId);
            var counter = session is null ? null
                : await s.Bill.Counters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == session.CounterId);
            var op = session is null ? null
                : await s.Auth.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == session.OperatorId);

            InvoiceNo = invoice.InvoiceNo;
            CreatedAt = invoice.CreatedAt;
            Gross = invoice.Gross;
            Discount = invoice.Discount;
            Net = invoice.Net;
            State = invoice.State;
            PatientName = patient.FullName;
            Uhid = patient.Uhid;
            Sex = patient.Sex;
            Phone = patient.Phone;
            Age = Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today);
            CounterName = counter?.Name ?? "—";
            OperatorName = op?.DisplayName ?? "—";

            Lines = await s.Bill.InvoiceLines.AsNoTracking()
                .Where(l => l.InvoiceId == id)
                .Select(l => new ReceiptLine(l.DescriptionSnapshot, l.Qty, l.Amount))
                .ToListAsync();

            Payments = await s.Bill.Receipts.AsNoTracking()
                .Where(r => r.InvoiceId == id)
                .OrderBy(r => r.At)
                .Select(r => new ReceiptPayment(r.ReceiptNo, r.Amount, r.Tender, r.At))
                .ToListAsync();

            Paid = Payments.Sum(p => p.Amount);
            Due = await s.Bill.Dues.AsNoTracking().Where(d => d.InvoiceId == id)
                .Select(d => (long?)d.Balance).FirstOrDefaultAsync() ?? 0;
            return true;
        });

        return found ? Page() : NotFound();
    }
}
