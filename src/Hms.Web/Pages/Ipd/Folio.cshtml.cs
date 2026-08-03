using System.ComponentModel.DataAnnotations;
using Hms.Admin;
using Hms.Billing;
using Hms.Billing.Data;
using Hms.Emr.Data;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Hms.Lis;
using Hms.Pharmacy;
using Hms.Web.Pages.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record FolioLine(
    long Id, string Description, string Source, int Qty, long Amount, DateTimeOffset At,
    string PostedBy, bool Billed);
public sealed record AdvanceRow(string ReceiptNo, long Amount, string Tender, DateTimeOffset At);
public sealed record StayRow(string Bed, DateTimeOffset From, DateTimeOffset? To);
public sealed record IndentItemRow(
    long ProductId, string Product, int Requested, int Issued, int Returned)
{
    /// <summary>What is still returnable at discharge (0016 deferral #11 path).</summary>
    public bool Returnable => Issued > Returned;
}

/// <summary>A signed indoor prescription the indent card can prefill from (spec 0042).</summary>
public sealed record RxNoteChoice(long Id, DateTimeOffset At, int OnFormulary, int FreeText);

public sealed record IndentRow(
    long Id, string State, DateTimeOffset RequestedAt, IReadOnlyList<IndentItemRow> Items);

public sealed record ProductChoice(long Id, string Name);
public sealed record TestChoice(long Id, string Name, long Price);

/// <summary>
/// The patient folio (§5 M6 [M]) — the running truth of an admission. Viewing it catches up
/// owed bed days (idempotent, P18); posting services, advances, indents and indoor test
/// orders all pass the folio gate. US6.1: the poster picks patient → service → qty and never
/// touches a price.
/// </summary>
[Authorize(Policy = Perm.IpdRead)]
public class FolioModel(
    HmsTx tx, BillingService billing, FolioService folios, IpdService ipd, StockService stock,
    RateResolver rates, LisService lis, ApprovalEngine approvals, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long AdmissionId { get; set; }
    [BindProperty] public long ServiceId { get; set; }
    /// <summary>AUD-VAL-14g: quantity 999999 once posted 29,99,99,700 Tk to a folio.</summary>
    [BindProperty, Qty] public int Qty { get; set; } = 1;
    [BindProperty] public long? VisitDoctorId { get; set; }
    [BindProperty, Money] public long AdvanceAmount { get; set; }
    [BindProperty] public string AdvanceTender { get; set; } = "cash";
    [BindProperty] public long ToBedId { get; set; }
    // Parallel lists — element quantities are judged in the indent handler, never by a
    // collection-level annotation (spec 0039 WP1, AUD-VAL-16).
    [BindProperty] public List<long> ProductIds { get; set; } = [];
    [BindProperty] public List<int> ItemQtys { get; set; } = [];
    [BindProperty, StringLength(Bounds.Note)] public string? IndentNote { get; set; }
    [BindProperty] public List<long> TestIds { get; set; } = [];
    [BindProperty] public long IndentId { get; set; }
    [BindProperty] public long RxNoteId { get; set; }
    [BindProperty] public long ReturnProductId { get; set; }
    [BindProperty] public int ReturnQty { get; set; }
    [BindProperty] public string? Reason { get; set; }

    public Admission? Admission { get; private set; }
    public Folio? Folio { get; private set; }
    public string? PatientName { get; private set; }
    public string? PatientUhid { get; private set; }
    public IReadOnlyList<FolioLine> Lines { get; private set; } = [];
    public IReadOnlyList<AdvanceRow> Advances { get; private set; } = [];
    public IReadOnlyList<StayRow> Stays { get; private set; } = [];
    public IReadOnlyList<IndentRow> Indents { get; private set; } = [];
    public IReadOnlyList<RxNoteChoice> RxNotes { get; private set; } = [];
    public IReadOnlyList<CatalogItem> Services { get; private set; } = [];
    public IReadOnlyList<DoctorChoice> Doctors { get; private set; } = [];
    public IReadOnlyList<ProductChoice> Products { get; private set; } = [];
    public IReadOnlyList<TestChoice> Tests { get; private set; } = [];
    public IReadOnlyList<BedChoice> FreeBeds { get; private set; } = [];
    public OpenSession? Session { get; private set; }
    public long? LatePostApprovalId { get; private set; }
    public long SettledDue { get; private set; }
    public string? SettledInvoiceNo { get; private set; }

    public long UnbilledTotal => Lines.Where(l => !l.Billed).Sum(l => l.Amount);
    public long AdvanceHeld => Advances.Sum(a => a.Amount);
    public bool CanPost => Can("ipd.service.post");
    public bool CanSettle => Can("ipd.settle");
    public bool CanManage => Can("ipd.manage");

    public async Task<IActionResult> OnGetAsync()
    {
        var found = await LoadAsync();
        return found ? Page() : NotFound();
    }

    private async Task<bool> LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        return await tx.RunAsync(async s =>
        {
            Admission = await s.Ipd.Admissions.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == AdmissionId);
            if (Admission is null) return false;
            Folio = await s.Ipd.Folios.AsNoTracking()
                .SingleOrDefaultAsync(f => f.AdmissionId == AdmissionId);

            // Catch up owed bed days on view (idempotent; only for staff who may post money).
            if (Folio is not null && Folio.State == FolioState.Open && (CanPost || CanSettle))
            {
                await IpdBilling.CatchUpBedDaysAsync(
                    s, billing, folios, rates, BranchId, AdmissionId, ActorId);
            }

            var patient = await s.Reg.Patients.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == Admission.PatientId);
            PatientName = patient?.FullName;
            PatientUhid = patient?.Uhid;

            var stays = await s.Ipd.BedStays.AsNoTracking()
                .Where(x => x.AdmissionId == AdmissionId).OrderBy(x => x.FromAt).ToListAsync();
            var bedIds = stays.Select(x => x.BedId).Distinct().ToList();
            var beds = await s.Ipd.Beds.AsNoTracking()
                .Where(b => bedIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Code);
            Stays = stays.Select(x => new StayRow(beds.GetValueOrDefault(x.BedId, "—"), x.FromAt, x.ToAt))
                .ToList();

            if (Folio is not null)
            {
                var lines = await s.Bill.ChargeLines.AsNoTracking()
                    .Where(c => c.FolioId == Folio.Id).OrderBy(c => c.Id).ToListAsync();
                var posterIds = lines.Select(l => l.CreatedBy).Distinct().ToList();
                var posters = await s.Auth.Users.AsNoTracking()
                    .Where(u => posterIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
                Lines = lines.Select(l => new FolioLine(l.Id, l.DescriptionSnapshot, l.SourceModule,
                    l.Qty, l.Amount, l.CreatedAt, posters.GetValueOrDefault(l.CreatedBy, "?"),
                    l.InvoiceId != null)).ToList();

                Advances = await s.Bill.Receipts.AsNoTracking()
                    .Where(r => r.FolioId == Folio.Id).OrderBy(r => r.Id)
                    .Select(r => new AdvanceRow(r.ReceiptNo, r.Amount, r.Tender, r.At))
                    .ToListAsync();

                if (Folio.SettlementInvoiceId is long invId)
                {
                    var invoice = await s.Bill.Invoices.AsNoTracking()
                        .SingleOrDefaultAsync(i => i.Id == invId);
                    SettledInvoiceNo = invoice?.InvoiceNo;
                    SettledDue = await s.Bill.Dues.AsNoTracking()
                        .Where(d => d.InvoiceId == invId).Select(d => d.Balance).FirstOrDefaultAsync();
                }

                if (Folio.State == FolioState.Locked)
                {
                    LatePostApprovalId = await s.Kernel.ApprovalRequests.AsNoTracking()
                        .Where(r => r.Type == "folio-late-post" && r.SourceTable == "ipd.folio"
                                    && r.SourceId == Folio.Id && r.State == ApprovalState.Approved)
                        .OrderByDescending(r => r.Id).Select(r => (long?)r.Id).FirstOrDefaultAsync();
                }

                var indents = await s.Ipd.Indents.AsNoTracking()
                    .Where(i => i.AdmissionId == AdmissionId).OrderByDescending(i => i.Id).ToListAsync();
                var indentIds = indents.Select(i => i.Id).ToList();
                var items = await s.Ipd.IndentItems.AsNoTracking()
                    .Where(i => indentIds.Contains(i.IndentId)).ToListAsync();
                var productIds = items.Select(i => i.ProductId).Distinct().ToList();
                var productNames = await s.Pharm.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => $"{p.Brand} {p.Strength}");
                Indents = indents.Select(i => new IndentRow(i.Id, i.State, i.RequestedAt,
                    items.Where(x => x.IndentId == i.Id)
                        .Select(x => new IndentItemRow(x.ProductId,
                            productNames.GetValueOrDefault(x.ProductId, "—"),
                            x.QtyRequested, x.QtyIssued, x.QtyReturned)).ToList())).ToList();

                // Spec 0042: signed indoor prescriptions whose lines carry a formulary product —
                // what "raise indent from prescription" can actually resolve to stock. Cross
                // context (emr → pharm) joined here, in memory, like everything else on this page.
                var signedNotes = await s.Emr.Notes.AsNoTracking()
                    .Where(n => n.AdmissionId == AdmissionId && n.State == NoteState.Final)
                    .OrderByDescending(n => n.Id).Take(5)
                    .Select(n => new { n.Id, n.FinalisedAt, n.CreatedAt }).ToListAsync();
                var signedIds = signedNotes.Select(n => n.Id).ToList();
                var rxDrugs = await s.Emr.NoteDrugs.AsNoTracking()
                    .Where(d => signedIds.Contains(d.NoteId)).ToListAsync();
                RxNotes = signedNotes
                    .Select(n => new RxNoteChoice(n.Id, n.FinalisedAt ?? n.CreatedAt,
                        rxDrugs.Count(d => d.NoteId == n.Id && d.ProductId != null),
                        rxDrugs.Count(d => d.NoteId == n.Id && d.ProductId == null)))
                    .Where(n => n.OnFormulary > 0).ToList();
            }

            // Pickers (only when the viewer can act — §7 U7: absent, not disabled).
            if (CanPost || CanSettle)
            {
                var services = await s.Adm.Services.AsNoTracking().Where(x => x.Active)
                    .OrderBy(x => x.Dept).ThenBy(x => x.Name).ToListAsync();
                var catalog = new List<CatalogItem>();
                foreach (var svc in services)
                {
                    try
                    {
                        var rate = await rates.ResolveAsync(s.Adm, "service", svc.Id, today);
                        catalog.Add(new CatalogItem(svc.Id, svc.Code, svc.Name, svc.Dept,
                            rate.Price, rate.RateVersionId));
                    }
                    catch (RateResolutionException) { }
                }
                Services = catalog;

                Doctors = (await s.Appt.Schedules.AsNoTracking().ToListAsync())
                    .GroupBy(x => x.DoctorId)
                    .Select(g => new DoctorChoice(g.Key, g.First().DoctorName))
                    .OrderBy(d => d.Name).ToList();

                Products = (await s.Pharm.Products.AsNoTracking().Where(p => p.Active)
                        .OrderBy(p => p.Brand).ToListAsync())
                    .Select(p => new ProductChoice(p.Id, $"{p.Brand} {p.Strength} {p.Form}")).ToList();

                var testCatalog = await s.Adm.TestCatalog.AsNoTracking().Where(t => t.Active)
                    .OrderBy(t => t.Name).ToListAsync();
                var testChoices = new List<TestChoice>();
                foreach (var t in testCatalog)
                {
                    try
                    {
                        var rate = await rates.ResolveAsync(s.Adm, "test", t.Id, today);
                        testChoices.Add(new TestChoice(t.Id, t.Name, rate.Price));
                    }
                    catch (RateResolutionException) { }
                }
                Tests = testChoices;
            }

            if (CanManage)
            {
                var free = await s.Ipd.Beds.AsNoTracking()
                    .Where(b => b.State == BedState.Free).OrderBy(b => b.Code).ToListAsync();
                var wards = await s.Ipd.Wards.AsNoTracking().ToDictionaryAsync(w => w.Id);
                FreeBeds = free.Select(b => new BedChoice(b.Id, b.Code,
                    wards.GetValueOrDefault(b.WardId)?.Name ?? "—",
                    wards.GetValueOrDefault(b.WardId)?.Class ?? "—", 0)).ToList();
            }

            Session = await CounterContext.FindOpenAsync(s.Bill, ActorId);
            return true;
        });
    }

    private async Task<IActionResult> Reshow(string message)
    {
        await LoadAsync();
        Fail(message);
        return Page();
    }

    public async Task<IActionResult> OnPostServiceAsync()
    {
        if (!CanPost && !CanSettle) return Forbid();
        if (ServiceId == 0 || Qty <= 0) return await Reshow("Pick a service and a positive quantity.");
        try
        {
            await tx.RunAsync(async s =>
            {
                var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.AdmissionId == AdmissionId);
                await IpdBilling.PostServiceAsync(s, billing, folios, rates, clock, BranchId,
                    folio.Id, ServiceId, Qty, VisitDoctorId,
                    await FindLatePostApprovalAsync(s, folio.Id), ActorId);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        catch (BillingException e) { return await Reshow(e.Message); }
        catch (RateResolutionException e) { return await Reshow(e.Message); }
        Toast("Posted to the folio — your name is on the line", "task_alt");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostAdvanceAsync()
    {
        if (!CanSettle) return Forbid();
        if (AdvanceAmount <= 0) return await Reshow("The advance must be a positive amount.");
        // AUD-VAL-15f: "bitcoin" once entered the drawer — a tender the day-close cannot classify.
        if (!Tenders.IsKnown(AdvanceTender))
            return await Reshow(
                $"That is not a way money can be taken. Use one of: {string.Join(", ", Tenders.All)}.");
        try
        {
            await tx.RunAsync(async s =>
            {
                var session = await CounterContext.FindOpenAsync(s.Bill, ActorId)
                              ?? throw new BillingException("Open your counter before taking money.");
                var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.AdmissionId == AdmissionId);
                var state = await folios.LockAsync(s.Ipd, folio.Id);
                if (state == FolioState.Locked)
                    throw new IpdException("The folio is settled — collect on the invoice instead.");
                await billing.CollectAdvanceAsync(s.Bill, s.Kernel, BranchId, folio.Id, session.Id,
                    AdvanceAmount, AdvanceTender, null, ActorId, ActorName);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        catch (BillingException e) { return await Reshow(e.Message); }
        Toast("Advance received into the drawer", "payments");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostTransferAsync()
    {
        if (!CanManage) return Forbid();
        if (ToBedId == 0) return await Reshow("Pick the target bed.");
        try
        {
            await tx.RunAsync(s => ipd.TransferAsync(s.Ipd, s.Kernel, AdmissionId, ToBedId, ActorId, ActorName));
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast("Transferred — charging follows from the next unposted day (P18)", "sync_alt");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostIndentAsync()
    {
        if (!CanPost) return Forbid();
        // AUD-VAL-16d: N products and M quantities is a crafted post — refuse, never zip-and-guess.
        if (ProductIds.Count != ItemQtys.Count)
            return await Reshow("The medicine lines came back garbled — re-enter them and try again.");
        var items = new List<(long ProductId, int Qty)>();
        for (var i = 0; i < ProductIds.Count; i++)
        {
            if (ProductIds[i] == 0 && ItemQtys[i] <= 0) continue;      // untouched blank row
            if (ProductIds[i] == 0)
                return await Reshow($"Line {i + 1}: pick the medicine for the quantity you entered.");
            if (ItemQtys[i] is < 1 or > 9_999)
                return await Reshow($"Line {i + 1}: the quantity must be between 1 and 9,999.");
            items.Add((ProductIds[i], ItemQtys[i]));
        }
        if (items.Count == 0) return await Reshow("Add at least one medicine line.");
        try
        {
            await tx.RunAsync(async s =>
            {
                // AUD-VAL-16c: an indent line names a pharmacy product — an id the pharmacy
                // catalogue does not have must not reach the issue queue. Cross-module existence
                // check, at the composition root where both schemas are readable.
                var ids = items.Select(x => x.ProductId).Distinct().ToList();
                var known = await s.Pharm.Products.AsNoTracking()
                    .Where(p => ids.Contains(p.Id) && p.Active).CountAsync();
                if (known != ids.Count)
                    throw new IpdException(
                        "A medicine on this indent is not in the pharmacy catalogue — pick it from the list again.");
                return await folios.CreateIndentAsync(s.Ipd, s.Kernel, BranchId, AdmissionId,
                    items, IndentNote, ActorId, ActorName);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast("Indent raised — pharmacy will issue it", "receipt_long");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    /// <summary>
    /// Spec 0042: turn a signed indoor prescription into a ward indent. Only lines that carry a
    /// formulary product resolve — free-text lines are named back to the operator for manual
    /// entry, never guessed at. Quantity defaults to 1 per line (the pharmacist issues against
    /// the prescription's own dose text); the operator can always edit the raised indent's
    /// follow-up manually.
    /// </summary>
    public async Task<IActionResult> OnPostIndentFromRxAsync()
    {
        if (!CanPost) return Forbid();
        List<string> skipped = [];
        try
        {
            await tx.RunAsync(async s =>
            {
                var note = await s.Emr.Notes.AsNoTracking()
                    .FirstOrDefaultAsync(n => n.Id == RxNoteId && n.AdmissionId == AdmissionId
                                              && n.State == NoteState.Final)
                    ?? throw new IpdException("That prescription is not on this admission — reload.");
                var drugs = await s.Emr.NoteDrugs.AsNoTracking()
                    .Where(d => d.NoteId == note.Id).OrderBy(d => d.Ordinal).ToListAsync();
                skipped = drugs.Where(d => d.ProductId is null).Select(d => d.DrugName).ToList();
                var items = drugs.Where(d => d.ProductId is not null)
                    .GroupBy(d => d.ProductId!.Value)
                    .Select(g => (ProductId: g.Key, Qty: g.Count())).ToList();
                if (items.Count == 0)
                    throw new IpdException(
                        "No line on that prescription names a formulary product — enter the indent manually.");
                return await folios.CreateIndentAsync(s.Ipd, s.Kernel, BranchId, AdmissionId,
                    items, $"From prescription #{note.Id}", ActorId, ActorName);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast(skipped.Count > 0
                ? $"Indent raised from the prescription · add manually: {string.Join(", ", skipped)}"
                : "Indent raised from the prescription — pharmacy will issue it",
            "receipt_long");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostOrderTestsAsync()
    {
        if (!CanPost && !CanSettle) return Forbid();
        if (TestIds.Count == 0) return await Reshow("Pick at least one test.");
        try
        {
            await tx.RunAsync(async s =>
            {
                var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.AdmissionId == AdmissionId);
                await IpdBilling.OrderTestsAsync(s, billing, folios, rates, lis, clock,
                    BranchId, folio.Id, TestIds, VisitDoctorId, ActorId);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        catch (BillingException e) { return await Reshow(e.Message); }
        catch (RateResolutionException e) { return await Reshow(e.Message); }
        Toast("Ordered — charges on the folio, samples raised for the lab", "biotech");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostReturnItemAsync()
    {
        if (!CanPost && !CanSettle) return Forbid();
        if (IndentId == 0 || ReturnProductId == 0 || ReturnQty <= 0)
            return await Reshow("Pick the indent line and a positive return quantity.");
        try
        {
            await tx.RunAsync(async s =>
            {
                var indent = await s.Ipd.Indents.AsNoTracking().SingleAsync(i => i.Id == IndentId);
                await IpdBilling.ReturnIndentItemAsync(s, billing, folios, stock,
                    BranchId, IndentId, ReturnProductId, ReturnQty,
                    await FindLatePostApprovalAsync(s, indent.FolioId), ActorId);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        catch (PharmacyException e) { return await Reshow(e.Message); }
        catch (BillingException e) { return await Reshow(e.Message); }
        Toast("Returned — stock is back on its batches, folio reversed", "task_alt");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    /// <summary>Raise the ⚿ late-post request on a locked folio (§12 cross-role table).</summary>
    public async Task<IActionResult> OnPostRaiseLatePostAsync()
    {
        if (!CanPost && !CanSettle) return Forbid();
        if (string.IsNullOrWhiteSpace(Reason)) return await Reshow("A late posting needs a reason.");
        var raise = await tx.RunAsync(async s =>
        {
            var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.AdmissionId == AdmissionId);
            return await approvals.RaiseAsync(s.Kernel, BranchId, "folio-late-post", "ipd.folio",
                folio.Id, ActorId, ActorRole, Reason.Trim(), null);
        });
        Toast(raise.AutoApproved ? "Late posting approved — post the line now"
            : "Late-post request sent to the supervisor", "fact_check");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostDeathAsync()
    {
        if (!CanManage) return Forbid();
        int openWork;
        try
        {
            openWork = await tx.RunAsync(async s =>
            {
                await ipd.RecordDeathAsync(s.Ipd, s.Kernel, AdmissionId, ActorId, ActorName);
                return await OpenWardWorkAsync(s, AdmissionId);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        // Death is unannounced — the ward is guaranteed to hold open work at this instant
        // (spec 0042 F8). Say so now, while someone is looking.
        Toast(openWork > 0
                ? $"Death recorded — {openWork} open dose(s)/task(s) remain on the chart; "
                  + "close them with a reason"
                : "Death recorded — issue the certificate from the certificates screen",
            "verified");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostAbscondedAsync()
    {
        if (!CanManage) return Forbid();
        int openWork;
        try
        {
            openWork = await tx.RunAsync(async s =>
            {
                await ipd.RecordAbscondedAsync(s.Ipd, s.Kernel, AdmissionId, ActorId, ActorName);
                return await OpenWardWorkAsync(s, AdmissionId);
            });
        }
        catch (IpdException e) { return await Reshow(e.Message); }
        Toast(openWork > 0
                ? $"Marked absconded — {openWork} open dose(s)/task(s) remain on the chart; "
                  + "close them with a reason"
                : "Marked absconded — dues stay on record for follow-up",
            "warning");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    /// <summary>Open clinical items still hanging on this admission (spec 0042 F8).</summary>
    private static async Task<int> OpenWardWorkAsync(TxScope s, long admissionId)
        => await s.Emr.MarDoses.AsNoTracking()
               .CountAsync(d => d.AdmissionId == admissionId
                                && d.State == Hms.Emr.Data.DoseState.Scheduled)
           + await s.Emr.CareTasks.AsNoTracking()
               .CountAsync(t => t.AdmissionId == admissionId
                                && t.State == Hms.Emr.Data.TaskState.Open);

    private static Task<long?> FindLatePostApprovalAsync(TxScope s, long folioId)
        => s.Kernel.ApprovalRequests.AsNoTracking()
            .Where(r => r.Type == "folio-late-post" && r.SourceTable == "ipd.folio"
                        && r.SourceId == folioId && r.State == ApprovalState.Approved)
            .OrderByDescending(r => r.Id).Select(r => (long?)r.Id).FirstOrDefaultAsync();
}
