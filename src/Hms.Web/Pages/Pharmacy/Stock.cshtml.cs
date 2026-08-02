using System.ComponentModel.DataAnnotations;
using Hms.Kernel.Approvals;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record BatchRow(
    long Id, string Brand, string Strength, string Form, string BatchNo, DateOnly Expiry,
    int OnHand, long Cost, long Mrp, string State, string? StateReason, bool Expired, bool NearExpiry);
public sealed record AuditRow(long Id, string Outlet, string State, DateTimeOffset StartedAt, int Lines, int Variances);

/// <summary>
/// §5 M11 [M] expiry management + 5A-11 damage/expired-medicine management + §11 Stock Batch
/// and Stock Audit machines. Quarantine → return/dispose are the batch's legal exits; the
/// stock count walks Count Started → Variance Listed → Adjustment Approved⚿ → Posted.
/// </summary>
[Authorize(Policy = Perm.PharmacyStockManage)]
public class StockModel(
    HmsTx tx, StockService stock, ApprovalEngine approvals, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string Show { get; set; } = "all";  // all|near|expired|quarantined
    [BindProperty(SupportsGet = true)] public long? OutletId { get; set; }
    [BindProperty] public long BatchId { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public long SupplierId { get; set; }
    [BindProperty] public bool Replacement { get; set; }
    [BindProperty] public long AuditId { get; set; }
    [BindProperty] public long LineId { get; set; }
    /// <summary>AUD-VAL-19: a scalar, so the annotation is the bound — the gate refuses the
    /// unparseable ("abc" once bound to 0 and wrote the batch off the shelf).</summary>
    [BindProperty, Range(0, 9_999_999, ErrorMessage = "The counted quantity must be between 0 and 99,99,999")]
    public int CountedQty { get; set; }

    public IReadOnlyList<Outlet> Outlets { get; private set; } = [];
    public IReadOnlyList<Supplier> Suppliers { get; private set; } = [];
    public IReadOnlyList<BatchRow> Rows { get; private set; } = [];
    public IReadOnlyList<AuditRow> Audits { get; private set; } = [];
    public StockAudit? OpenAudit { get; private set; }
    public IReadOnlyList<(StockAuditLine Line, string Label)> OpenAuditLines { get; private set; } = [];
    public long StockValueAtCost { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var near = today.AddDays(StockService.NearExpiryDays);

        await tx.RunAsync(async s =>
        {
            Outlets = await s.Pharm.Outlets.AsNoTracking().Where(o => o.Active).OrderBy(o => o.Id).ToListAsync();
            OutletId ??= Outlets.FirstOrDefault()?.Id;
            Suppliers = await s.Pharm.Suppliers.AsNoTracking().Where(x => x.Active).ToListAsync();

            var products = await s.Pharm.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);
            var batches = await s.Pharm.Batches.AsNoTracking()
                .Where(b => b.OutletId == OutletId)
                .OrderBy(b => b.Expiry).ToListAsync();

            var all = batches.Select(b =>
            {
                products.TryGetValue(b.ProductId, out var p);
                return new BatchRow(b.Id, p?.Brand ?? "—", p?.Strength ?? "", p?.Form ?? "",
                    b.BatchNo, b.Expiry, b.QtyOnHand, b.Cost, b.Mrp, b.State, b.StateReason,
                    b.Expiry <= today, b.Expiry <= near && b.Expiry > today);
            }).ToList();

            Rows = Show switch
            {
                "near" => all.Where(r => r.NearExpiry && r.State == BatchState.InStock).ToList(),
                "expired" => all.Where(r => r.Expired && r.State == BatchState.InStock).ToList(),
                "quarantined" => all.Where(r => r.State == BatchState.Quarantined).ToList(),
                _ => all,
            };
            StockValueAtCost = all
                .Where(r => r.State == BatchState.InStock && !r.Expired)
                .Sum(r => (long)r.OnHand * r.Cost);

            var audits = await s.Pharm.StockAudits.AsNoTracking()
                .Where(a => a.OutletId == OutletId).OrderByDescending(a => a.Id).Take(10).ToListAsync();
            var auditIds = audits.Select(a => a.Id).ToList();
            var lines = await s.Pharm.StockAuditLines.AsNoTracking()
                .Where(l => auditIds.Contains(l.StockAuditId)).ToListAsync();
            var outletNames = Outlets.ToDictionary(o => o.Id, o => o.Name);
            Audits = audits.Select(a => new AuditRow(a.Id, outletNames.GetValueOrDefault(a.OutletId, "—"),
                a.State, a.StartedAt,
                lines.Count(l => l.StockAuditId == a.Id),
                lines.Count(l => l.StockAuditId == a.Id && l.CountedQty != l.SystemQty))).ToList();

            OpenAudit = audits.FirstOrDefault(a => a.State != AuditState.Posted);
            if (OpenAudit is not null)
                OpenAuditLines = lines.Where(l => l.StockAuditId == OpenAudit.Id)
                    .Select(l =>
                    {
                        var b = batches.FirstOrDefault(x => x.Id == l.BatchId);
                        products.TryGetValue(l.ProductId, out var p);
                        return (l, $"{p?.Brand} {p?.Strength} · batch {b?.BatchNo}");
                    }).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostQuarantineAsync()
    {
        if (string.IsNullOrWhiteSpace(Reason))
        { await LoadAsync(); Fail("Say why — damage and expiry both leave a reasoned trail."); return Page(); }
        try
        {
            await tx.RunAsync(s => stock.QuarantineAsync(
                s.Pharm, s.Kernel, BranchId, BatchId, Reason!.Trim(), ActorId, ActorName));
            Toast("Batch quarantined — it can no longer be sold", "inventory_2");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect($"/pharmacy/stock?Show={Show}&OutletId={OutletId}");
    }

    public async Task<IActionResult> OnPostReturnAsync()
    {
        if (SupplierId == 0) { await LoadAsync(); Fail("Pick the supplier taking it back."); return Page(); }
        try
        {
            await tx.RunAsync(s => stock.ReturnToSupplierAsync(
                s.Pharm, s.Kernel, BranchId, BatchId, SupplierId, Replacement, ActorId, ActorName));
            Toast(Replacement ? "Returned — replacement expected from the supplier"
                              : "Returned — supplier ledger credited", "sync_alt");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect($"/pharmacy/stock?Show=quarantined&OutletId={OutletId}");
    }

    /// <summary>Raise the ⚿ write-off request; disposal happens after approval.</summary>
    public async Task<IActionResult> OnPostRequestWriteoffAsync()
    {
        if (string.IsNullOrWhiteSpace(Reason))
        { await LoadAsync(); Fail("A write-off needs a reason for the approver."); return Page(); }
        var raise = await tx.RunAsync(async s =>
        {
            var batch = await s.Pharm.Batches.AsNoTracking().SingleAsync(b => b.Id == BatchId);
            return await approvals.RaiseAsync(s.Kernel, BranchId, "stock-writeoff", "pharm.batch",
                BatchId, ActorId, ActorRole, Reason!.Trim(), batch.QtyOnHand * batch.Cost);
        });
        Toast(raise.AutoApproved ? "Write-off approved under your limit — you can dispose now"
                                 : "Write-off request sent to the approvals inbox", "fact_check");
        return Redirect($"/pharmacy/stock?Show=quarantined&OutletId={OutletId}");
    }

    public async Task<IActionResult> OnPostDisposeAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var approval = await s.Kernel.ApprovalRequests.AsNoTracking()
                    .Where(a => a.Type == "stock-writeoff" && a.SourceTable == "pharm.batch"
                                && a.SourceId == BatchId && a.State == Hms.Kernel.Data.ApprovalState.Approved)
                    .OrderByDescending(a => a.Id).FirstOrDefaultAsync()
                    ?? throw new PharmacyException("No approved write-off for this batch yet.");
                await stock.DisposeAsync(s.Pharm, s.Kernel, BranchId, BatchId, approval.Id, ActorId, ActorName);
            });
            Toast("Batch disposed — logged with the approval that allowed it", "delete_forever");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect($"/pharmacy/stock?Show=quarantined&OutletId={OutletId}");
    }

    // ---- stock audit (§11) -------------------------------------------------

    public async Task<IActionResult> OnPostStartAuditAsync()
    {
        if (OutletId is null or 0)
        { await LoadAsync(); Fail("Pick the outlet to count first."); return Page(); }
        try
        {
            await tx.RunAsync(async s =>
            {
                var open = await s.Pharm.StockAudits.AnyAsync(a =>
                    a.OutletId == OutletId && a.State != AuditState.Posted);
                if (open) throw new PharmacyException("An audit is already in progress for this outlet.");

                var audit = new StockAudit
                {
                    BranchId = BranchId, OutletId = OutletId!.Value,
                    StartedAt = clock.GetUtcNow(), StartedBy = ActorId,
                };
                s.Pharm.StockAudits.Add(audit);
                await s.Pharm.SaveChangesAsync();

                var batches = await s.Pharm.Batches.AsNoTracking()
                    .Where(b => b.OutletId == OutletId && b.State == BatchState.InStock).ToListAsync();
                foreach (var b in batches)
                    s.Pharm.StockAuditLines.Add(new StockAuditLine
                    {
                        StockAuditId = audit.Id, BatchId = b.Id, ProductId = b.ProductId,
                        SystemQty = b.QtyOnHand, CountedQty = b.QtyOnHand,
                    });
                await s.Pharm.SaveChangesAsync();
                return 0;
            });
        }
        // AUD-VAL-19: a second "Start count" used to escape as a blank 500 — the one handler
        // on this page with no catch.
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Count started — enter what the shelf actually holds", "inventory_2");
        return Redirect($"/pharmacy/stock?OutletId={OutletId}");
    }

    public async Task<IActionResult> OnPostCountAsync()
    {
        var found = await tx.RunAsync(async s =>
        {
            // AUD-VAL-19e: an unknown line id is a stale screen, not a crash.
            var line = await s.Pharm.StockAuditLines.SingleOrDefaultAsync(l => l.Id == LineId);
            if (line is null) return false;
            // No Math.Max coercion: CountedQty arrives already judged by its [Range] — a typo
            // must never silently write the batch down to zero (AUD-VAL-19a).
            line.CountedQty = CountedQty;
            var audit = await s.Pharm.StockAudits.SingleAsync(a => a.Id == line.StockAuditId);
            if (audit.State == AuditState.CountStarted) audit.State = AuditState.VarianceListed;
            await s.Pharm.SaveChangesAsync();
            return true;
        });
        if (!found)
        {
            await LoadAsync();
            Fail("That count line is not part of the open count any more — reload the screen and try again.");
            return Page();
        }
        return Redirect($"/pharmacy/stock?OutletId={OutletId}");
    }

    public async Task<IActionResult> OnPostRequestAdjustAsync()
    {
        var raise = await tx.RunAsync(async s =>
        {
            var lines = await s.Pharm.StockAuditLines.AsNoTracking()
                .Where(l => l.StockAuditId == AuditId && l.CountedQty != l.SystemQty).ToListAsync();
            if (lines.Count == 0) throw new PharmacyException("No variance — nothing to adjust; post directly.");
            return await approvals.RaiseAsync(s.Kernel, BranchId, "stock-writeoff", "pharm.stock_audit",
                AuditId, ActorId, ActorRole, $"stock count adjustment: {lines.Count} variance line(s)", null);
        });
        if (raise.AutoApproved)
            await tx.RunAsync(async s =>
            {
                var audit = await s.Pharm.StockAudits.SingleAsync(a => a.Id == AuditId);
                audit.State = AuditState.Approved;
                audit.ApprovalId = raise.ApprovalId;
                await s.Pharm.SaveChangesAsync();
                return 0;
            });
        Toast(raise.AutoApproved ? "Adjustment approved — post it" : "Adjustment sent for approval", "fact_check");
        return Redirect($"/pharmacy/stock?OutletId={OutletId}");
    }

    public async Task<IActionResult> OnPostPostAuditAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var audit = await s.Pharm.StockAudits.SingleAsync(a => a.Id == AuditId);
                // The variance-free audit needs no approval; a variance does (§11 ⚿).
                var variances = await s.Pharm.StockAuditLines.AnyAsync(l =>
                    l.StockAuditId == AuditId && l.CountedQty != l.SystemQty);
                if (!variances && audit.State == AuditState.CountStarted)
                    audit.State = AuditState.Approved;
                if (audit.State == AuditState.VarianceListed)
                {
                    // An approver may have decided in the inbox since the page rendered.
                    var approval = await s.Kernel.ApprovalRequests.AsNoTracking()
                        .Where(a => a.Type == "stock-writeoff" && a.SourceTable == "pharm.stock_audit"
                                    && a.SourceId == AuditId
                                    && a.State == Hms.Kernel.Data.ApprovalState.Approved)
                        .OrderByDescending(a => a.Id).FirstOrDefaultAsync();
                    if (approval is not null) { audit.State = AuditState.Approved; audit.ApprovalId = approval.Id; }
                }
                await s.Pharm.SaveChangesAsync();
                await stock.PostAuditAsync(s.Pharm, s.Kernel, BranchId, AuditId, ActorId, ActorName);
                return 0;
            });
            Toast("Count posted — adjustments are on the stock ledger", "task_alt");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect($"/pharmacy/stock?OutletId={OutletId}");
    }
}
