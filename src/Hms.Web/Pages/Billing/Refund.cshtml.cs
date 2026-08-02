using System.ComponentModel.DataAnnotations;
using Hms.Billing;
using Hms.Billing.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record RefundCandidate(
    long InvoiceId, string InvoiceNo, string PatientName, string Uhid,
    long Net, long Paid, long Balance, string State, DateTimeOffset CreatedAt);

public sealed record PendingReversal(
    long RequestId, string Type, long InvoiceId, string InvoiceNo, long? Amount,
    string Reason, string State, string RequestedBy);

/// <summary>
/// 05 §5 screen 7 and §11's two money exits: <b>Cancelled⚿</b> before any payment, and
/// <b>Refunded⚿</b> after. Both are approval-gated (§12) and neither deletes anything — a
/// cancellation marks the invoice, a refund writes a negative receipt beside the original
/// (hard rule 4). The operator sees the consequence in plain English before committing (§7 U8).
/// </summary>
[Authorize(Policy = Perm.BillingReceiptCreate)]
public class RefundModel(
    HmsTx tx, BillingService billing, ApprovalEngine approvals,
    Hms.Pharmacy.StockService stock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public long InvoiceId { get; set; }
    [BindProperty, Money] public long Amount { get; set; }
    [BindProperty, StringLength(Bounds.Note)] public string? Reason { get; set; }
    [BindProperty] public string Action { get; set; } = "refund";

    /// <summary>
    /// How the money physically goes back. The operator says; the system does not guess from
    /// what the patient paid weeks ago (spec 0032, M4-F2). It defaults to cash because that is
    /// what a hospital counter hands over, and cash is the tender the drawer is counted against.
    /// </summary>
    [BindProperty] public string Tender { get; set; } = Tenders.Cash;

    public OpenSession? Session { get; private set; }
    public IReadOnlyList<RefundCandidate> Candidates { get; private set; } = [];
    public IReadOnlyList<PendingReversal> Pending { get; private set; } = [];
    public IReadOnlyList<PendingReversal> Approved { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            Session = await CounterContext.FindOpenAsync(s.Bill, ActorId);

            // The search contract (ADR-0020): predicate in SQL, so any invoice is findable
            // however old. Patient terms resolve to ids in reg first (ADR-0003 boundary).
            var term = Q?.Trim();
            var invQuery = s.Bill.Invoices.AsNoTracking()
                .Where(i => i.State != InvoiceState.Cancelled);
            if (!string.IsNullOrWhiteSpace(term))
            {
                var like = $"%{term}%";
                var matchedPatients = await s.Reg.Patients.AsNoTracking()
                    .Matching(term)                                    // spec 0020
                    .Select(p => p.Id).Take(500).ToListAsync();
                invQuery = invQuery.Where(i =>
                    EF.Functions.ILike(i.InvoiceNo, like) || matchedPatients.Contains(i.PatientId));
            }
            var invoices = await invQuery
                .OrderByDescending(i => i.Id).Take(25)
                .Select(i => new { i.Id, i.InvoiceNo, i.PatientId, i.Net, i.State, i.CreatedAt })
                .ToListAsync();

            var ids = invoices.Select(i => i.Id).ToList();
            var paid = (await s.Bill.Receipts.AsNoTracking()
                    .Where(r => r.InvoiceId != null && ids.Contains(r.InvoiceId.Value))
                    .Select(r => new { InvoiceId = r.InvoiceId!.Value, r.Amount }).ToListAsync())
                .GroupBy(r => r.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
            var dues = await s.Bill.Dues.AsNoTracking()
                .Where(d => ids.Contains(d.InvoiceId)).ToDictionaryAsync(d => d.InvoiceId, d => d.Balance);

            var patientIds = invoices.Select(i => i.PatientId).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            Candidates = invoices.Select(i =>
            {
                patients.TryGetValue(i.PatientId, out var p);
                return new RefundCandidate(i.Id, i.InvoiceNo, p?.FullName ?? "(unknown)",
                    p?.Uhid ?? "—", i.Net, paid.GetValueOrDefault(i.Id),
                    dues.GetValueOrDefault(i.Id), i.State, i.CreatedAt);
            }).ToList();

            // The Request Center view (5A-21): what this operator has raised and where it stands.
            var types = new[] { "refund", "cancel" };
            var requests = await s.Kernel.ApprovalRequests.AsNoTracking()
                .Where(a => types.Contains(a.Type) && a.SourceTable == "bill.invoice")
                .OrderByDescending(a => a.Id).Take(40).ToListAsync();
            var reqInvoiceIds = requests.Select(a => a.SourceId).Distinct().ToList();
            var reqInvoices = await s.Bill.Invoices.AsNoTracking()
                .Where(i => reqInvoiceIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.InvoiceNo);
            var userIds = requests.Select(a => a.RequestedBy).Distinct().ToList();
            var users = await s.Auth.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            PendingReversal Map(ApprovalRequest a) => new(
                a.Id, a.Type, a.SourceId, reqInvoices.GetValueOrDefault(a.SourceId, "—"),
                a.Amount, a.Reason, a.State, users.GetValueOrDefault(a.RequestedBy, "—"));

            Pending = requests.Where(a => a.State == ApprovalState.Pending).Select(Map).ToList();

            // Approved but not yet carried out — money moves at a counter, not in the inbox.
            var spentIds = await s.Bill.Receipts.AsNoTracking()
                .Where(r => r.ApprovalId != null).Select(r => r.ApprovalId!.Value).ToListAsync();
            Approved = requests
                .Where(a => a.State == ApprovalState.Approved && !spentIds.Contains(a.Id))
                .Select(Map).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostRequestAsync()
    {
        await LoadAsync();
        // The screen offers exactly two things that can happen to an invoice (AUD-VAL-07g).
        // Anything else in this field came from outside the form — refuse it by name.
        if (Action is not ("refund" or "cancel"))
        { Fail("Choose what is happening — a refund or a cancellation. Nothing else can be done to an invoice."); return Page(); }
        var invoice = Candidates.FirstOrDefault(c => c.InvoiceId == InvoiceId);
        if (invoice is null) { Fail("Find the invoice first."); return Page(); }
        if (string.IsNullOrWhiteSpace(Reason))
        { Fail("A reason is required — it prints on the reversal and lands in the audit trail."); return Page(); }

        // A reversed invoice is finished. Without this, a full refund leaves net-paid at zero,
        // which would otherwise look exactly like an unpaid invoice and let it be cancelled too.
        if (invoice.State is InvoiceState.Refunded or InvoiceState.Cancelled)
        { Fail($"{invoice.InvoiceNo} has already been reversed — it cannot be reversed twice."); return Page(); }

        var isCancel = Action == "cancel";
        if (isCancel && invoice.Paid > 0)
        { Fail("Money has been taken on this invoice — request a refund instead of a cancellation."); return Page(); }
        if (!isCancel && invoice.Paid <= 0)
        { Fail("Nothing has been paid on this invoice — cancel it instead."); return Page(); }
        if (!isCancel && (Amount <= 0 || Amount > invoice.Paid))
        { Fail($"Enter an amount between ৳1 and {Ui.Money(invoice.Paid)} — that is what was actually paid."); return Page(); }

        if (!isCancel && !Tenders.IsKnown(Tender))
        { Fail("Choose how the money goes back to the patient."); return Page(); }

        var amount = isCancel ? invoice.Net : Amount;
        var raise = await tx.RunAsync(s => approvals.RaiseAsync(
            s.Kernel, BranchId, isCancel ? "cancel" : "refund", "bill.invoice", InvoiceId,
            ActorId, ActorRole, Reason!.Trim(), amount));

        if (raise.AutoApproved)
        {
            var done = await ExecuteAsync(InvoiceId, isCancel, amount, Reason!.Trim(), raise.ApprovalId!.Value, Tender);
            if (done is not null) { Fail(done); return Page(); }
            Toast(isCancel ? $"{invoice.InvoiceNo} cancelled" : $"Refunded {Ui.Money(amount)}", "currency_exchange");
            return Redirect($"/billing/invoice/{InvoiceId}");
        }

        Toast("Sent to the supervisor's approvals inbox — nothing has changed yet", "fact_check");
        return Redirect("/billing/refund");
    }

    /// <summary>
    /// Carrying out an already-approved reversal, at a counter with a drawer. The tender is asked
    /// for HERE and not on the request, because this is the moment the money actually moves and
    /// this is the operator whose drawer it moves out of.
    /// </summary>
    public async Task<IActionResult> OnPostExecuteAsync(long requestId, string? tender)
    {
        await LoadAsync();
        var req = Approved.FirstOrDefault(a => a.RequestId == requestId);
        if (req is null) { Fail("That request is no longer awaiting action — refresh."); return Page(); }
        if (req.Type != "cancel" && tender is not null && !Tenders.IsKnown(tender))
        { Fail("Choose how the money goes back to the patient."); return Page(); }

        var error = await ExecuteAsync(req.InvoiceId, req.Type == "cancel",
            req.Amount ?? 0, req.Reason, req.RequestId, tender ?? Tenders.Cash);
        if (error is not null) { Fail(error); return Page(); }

        Toast(req.Type == "cancel"
            ? $"{req.InvoiceNo} cancelled"
            : $"Refunded {Ui.Money(req.Amount ?? 0)} on {req.InvoiceNo}", "currency_exchange");
        return Redirect($"/billing/invoice/{req.InvoiceId}");
    }

    private async Task<string?> ExecuteAsync(
        long invoiceId, bool isCancel, long amount, string reason, long approvalId, string tender)
    {
        if (!isCancel && Session is null)
            return "Refunds move money, so they need an open counter session. Open your counter first.";
        try
        {
            await tx.RunAsync(async s =>
            {
                if (isCancel)
                    await billing.CancelInvoiceAsync(s.Bill, s.Kernel, BranchId, invoiceId,
                        reason, ActorId, ActorName, approvalId);
                else
                    await billing.RefundAsync(s.Bill, s.Kernel, BranchId, invoiceId, Session!.Id,
                        amount, tender, reason, ActorId, ActorName, approvalId);

                // A reversed pharmacy sale is a goods return (§5 M11 [M]): put the stock back
                // on the exact batches the sale allocated (spec 0016; ADR-0021 #5). The amount
                // decides how much comes back — a partial refund restocks proportionally, a
                // cancellation reverses the whole document (AUD-M11-01).
                var restockBudget = isCancel ? long.MaxValue : amount;
                await stock.RestockAsync(s.Pharm, BranchId, invoiceId, restockBudget, ActorId);
                return 0;
            });
            return null;
        }
        catch (BillingException e) { return e.Message; }
    }
}
