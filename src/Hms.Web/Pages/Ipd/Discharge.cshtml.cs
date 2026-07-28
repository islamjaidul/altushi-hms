using Hms.Billing;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Hms.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record DraftLine(string Description, string Source, int Qty, long Amount);

/// <summary>
/// US6.2: the discharge walk — initiate (clinical summary) → clinically clear → settlement
/// draft (bed days caught up, service-charge % posted, folio frozen) → confirm (invoice born
/// with advances applied) → collect on the invoice → discharge (gate pass). Discounts ride
/// the same approval engine as OPD billing; the folio locks at settlement (§11).
/// </summary>
[Authorize(Policy = Perm.IpdRead)]
public class DischargeModel(
    HmsTx tx, BillingService billing, FolioService folios, IpdService ipd, RateResolver rates,
    ApprovalEngine approvals, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long AdmissionId { get; set; }
    [BindProperty] public string? ClinicalSummary { get; set; }
    [BindProperty] public long DiscountFlat { get; set; }
    [BindProperty] public string? DiscountReason { get; set; }
    /// <summary>Spec 0020: releasing a patient who still owes money is a stated decision.</summary>
    [BindProperty] public string? OutstandingReason { get; set; }
    /// <summary>Spec 0021: one prepared settlement, one invoice.</summary>
    [BindProperty] public Guid SubmissionToken { get; set; }

    public Admission? Admission { get; private set; }
    public Folio? Folio { get; private set; }
    public string? PatientName { get; private set; }
    public IReadOnlyList<DraftLine> Draft { get; private set; } = [];
    public long Gross => Draft.Sum(l => l.Amount);
    public long AdvanceHeld { get; private set; }
    public OpenSession? Session { get; private set; }
    public long ApprovedDiscountId { get; private set; }
    public long ApprovedDiscountAmount { get; private set; }
    public bool DiscountPending { get; private set; }
    public long SettledDue { get; private set; }
    public string? SettledInvoiceNo { get; private set; }
    /// <summary>Everything the patient owes across the hospital, not just this admission.</summary>
    public IReadOnlyList<IpdBilling.OutstandingInvoice> Outstanding { get; private set; } = [];
    public long OutstandingTotal => Outstanding.Sum(o => o.Balance);
    public bool CanSettle => Can("ipd.settle");
    public bool CanManage => Can("ipd.manage");

    public async Task<IActionResult> OnGetAsync()
    {
        SubmissionToken = Submission.NewToken();
        return await LoadAsync() ? Page() : NotFound();
    }

    private async Task<bool> LoadAsync()
    {
        return await tx.RunAsync(async s =>
        {
            Admission = await s.Ipd.Admissions.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == AdmissionId);
            if (Admission is null) return false;
            Folio = await s.Ipd.Folios.AsNoTracking()
                .SingleOrDefaultAsync(f => f.AdmissionId == AdmissionId);
            PatientName = await s.Reg.Patients.AsNoTracking()
                .Where(p => p.Id == Admission.PatientId).Select(p => p.FullName).FirstOrDefaultAsync();
            Session = await CounterContext.FindOpenAsync(s.Bill, ActorId);

            if (Folio is not null)
            {
                Draft = await s.Bill.ChargeLines.AsNoTracking()
                    .Where(c => c.FolioId == Folio.Id && c.InvoiceId == null).OrderBy(c => c.Id)
                    .Select(c => new DraftLine(c.DescriptionSnapshot, c.SourceModule, c.Qty, c.Amount))
                    .ToListAsync();
                AdvanceHeld = await billing.AdvanceHeldAsync(s.Bill, Folio.Id);

                if (Folio.SettlementInvoiceId is long invId)
                {
                    SettledInvoiceNo = await s.Bill.Invoices.AsNoTracking()
                        .Where(i => i.Id == invId).Select(i => i.InvoiceNo).FirstOrDefaultAsync();
                    SettledDue = await s.Bill.Dues.AsNoTracking()
                        .Where(d => d.InvoiceId == invId).Select(d => d.Balance).FirstOrDefaultAsync();
                }

                // Spec 0020: the whole position, including outdoor invoices this admission
                // never knew about — nobody leaves the gate owing money unnoticed.
                Outstanding = await IpdBilling.OutstandingForPatientAsync(
                    s, Admission.PatientId, Folio.SettlementInvoiceId);

                // A discount already approved against this folio and not yet spent (OPD pattern).
                var approved = await s.Kernel.ApprovalRequests.AsNoTracking()
                    .Where(a => a.Type == "discount" && a.SourceTable == "ipd.folio"
                                && a.SourceId == Folio.Id && a.State == ApprovalState.Approved)
                    .OrderByDescending(a => a.Id)
                    .Select(a => new { a.Id, a.Amount }).ToListAsync();
                if (approved.Count > 0)
                {
                    var candidateIds = approved.Select(a => a.Id).ToList();
                    var spent = await s.Bill.Invoices.AsNoTracking()
                        .Where(i => i.DiscountApprovalId != null
                                    && candidateIds.Contains(i.DiscountApprovalId!.Value))
                        .Select(i => i.DiscountApprovalId!.Value).ToListAsync();
                    var usable = approved.FirstOrDefault(a => !spent.Contains(a.Id));
                    if (usable is not null)
                    {
                        ApprovedDiscountId = usable.Id;
                        ApprovedDiscountAmount = usable.Amount ?? 0;
                    }
                }
                DiscountPending = await s.Kernel.ApprovalRequests.AnyAsync(a =>
                    a.Type == "discount" && a.SourceTable == "ipd.folio" && a.SourceId == Folio.Id
                    && a.State == ApprovalState.Pending);
            }
            return true;
        });
    }

    private async Task<IActionResult> Reshow(string message)
    {
        await LoadAsync();
        Fail(message);
        return Page();
    }

    public async Task<IActionResult> OnPostInitiateAsync()
    {
        if (!CanManage && !CanSettle) return Forbid();
        if (string.IsNullOrWhiteSpace(ClinicalSummary))
            return await Reshow("Discharge starts with the clinical summary (§5 M6).");
        try
        {
            await tx.RunAsync(s => ipd.InitiateDischargeAsync(
                s.Ipd, s.Kernel, AdmissionId, ClinicalSummary.Trim(), ActorId, ActorName));
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast("Discharge initiated", "send");
        return Redirect($"/ipd/discharge/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        if (!CanManage && !CanSettle) return Forbid();
        try
        {
            await tx.RunAsync(s => ipd.ClinicallyClearAsync(s.Ipd, s.Kernel, AdmissionId, ActorId, ActorName));
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast("Clinically cleared — prepare the settlement", "task_alt");
        return Redirect($"/ipd/discharge/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostPrepareAsync()
    {
        if (!CanSettle) return Forbid();
        try
        {
            await tx.RunAsync(s => IpdBilling.PrepareSettlementAsync(
                s, billing, folios, rates, clock, BranchId, AdmissionId, ActorId));
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        catch (BillingException e) { return await Reshow(e.Message); }
        catch (RateResolutionException e) { return await Reshow(e.Message); }
        Toast("Settlement draft ready — the folio is frozen", "receipt_long");
        return Redirect($"/ipd/discharge/{AdmissionId}");
    }

    /// <summary>A late charge must still land: draft → open, post it, prepare again.</summary>
    public async Task<IActionResult> OnPostReopenAsync()
    {
        if (!CanSettle) return Forbid();
        try
        {
            await tx.RunAsync(async s =>
            {
                var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.AdmissionId == AdmissionId);
                await folios.ReopenDraftAsync(s.Ipd, s.Kernel, folio.Id, ActorId, ActorName);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast("Draft reopened — post the missing line, then prepare again", "arrow_back");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        if (!CanSettle) return Forbid();
        await LoadAsync();
        if (Session is null) return await Reshow("Open your counter before settling — money moves here.");

        var discount = Math.Max(0, Math.Min(Gross, DiscountFlat));
        long? approvalId = null;
        if (discount > 0 && string.IsNullOrWhiteSpace(DiscountReason))
            return await Reshow("A discount needs a reason — it shows on the MD dashboard.");
        if (discount > 0)
        {
            if (ApprovedDiscountId > 0 && ApprovedDiscountAmount >= discount)
            {
                approvalId = ApprovedDiscountId;
            }
            else
            {
                var raise = await tx.RunAsync(async s =>
                {
                    var folio = await s.Ipd.Folios.AsNoTracking()
                        .SingleAsync(f => f.AdmissionId == AdmissionId);
                    return await approvals.RaiseAsync(s.Kernel, BranchId, "discount", "ipd.folio",
                        folio.Id, ActorId, ActorRole, DiscountReason!.Trim(), discount);
                });
                if (!raise.AutoApproved)
                {
                    return await Reshow($"A discount of {Ui.Money(discount)} is above your limit. " +
                        "The request has gone to the approvals inbox — settle once it is decided.");
                }
                approvalId = raise.ApprovalId;
            }
        }

        try
        {
            var result = await tx.RunAsync(s => IpdBilling.ConfirmSettlementAsync(
                s, billing, folios, ipd, BranchId, AdmissionId, Session!.Id,
                0m, discount, approvalId, ActorId, ActorName, submissionToken: SubmissionToken));
            Toast(result.AdvanceReturned > 0
                    ? $"Settled — return {Ui.Money(result.AdvanceReturned)} excess advance from the drawer"
                    : "Settled — collect the balance on the invoice", "receipt_long");
            return Redirect($"/billing/invoice/{result.InvoiceId}");
        }
        catch (DbUpdateException e) when (Submission.IsDuplicateSubmission(e))
        {
            var existing = await tx.RunAsync(s => Submission.ExistingAsync(s, SubmissionToken));
            if (existing is not null) return Redirect($"/billing/invoice/{existing.Id}");
            return await Reshow("That settlement was just saved — reload to see it.");
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        catch (BillingException e) { return await Reshow(e.Message); }
    }

    public async Task<IActionResult> OnPostDischargeAsync()
    {
        if (!CanManage && !CanSettle) return Forbid();
        try
        {
            await tx.RunAsync(async s =>
            {
                // Recomputed inside the transaction: the screen's figure is a display, the
                // guard's figure is the truth (a payment may have landed in between).
                var folio = await s.Ipd.Folios.AsNoTracking()
                    .SingleAsync(f => f.AdmissionId == AdmissionId);
                var admission = await s.Ipd.Admissions.AsNoTracking()
                    .SingleAsync(a => a.Id == AdmissionId);
                var outstanding = await IpdBilling.OutstandingForPatientAsync(
                    s, admission.PatientId, folio.SettlementInvoiceId);
                await ipd.DischargeAsync(s.Ipd, s.Kernel, AdmissionId,
                    outstanding.Sum(o => o.Balance), OutstandingReason, ActorId, ActorName);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast(string.IsNullOrWhiteSpace(OutstandingReason)
            ? "Discharged — gate pass below, bed sent to cleaning"
            : "Discharged with a due — the reason is on the record with your name", "task_alt");
        return Redirect($"/ipd/discharge/{AdmissionId}");
    }
}
