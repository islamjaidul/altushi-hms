using System.ComponentModel.DataAnnotations;
using Hms.Admin;
using Hms.Appointments.Data;
using Hms.Billing;
using Hms.Billing.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record CatalogItem(long Id, string Code, string Name, string Dept, long Price, long RateVersionId);
public sealed record CartLine(long CatalogId, string Name, long Price);
public sealed record UnbilledLine(string Description, long Amount, string Source);

/// <summary>
/// Today's serial for the selected patient, and the consultation it implies (spec 0043). The
/// receptionist already recorded patient → doctor → serial; without this the cashier searched the
/// whole hospital catalogue for a line the hospital had already decided on.
/// <para>
/// <paramref name="ServiceId"/> is null when the doctor has no consultation service configured
/// (<c>/admin/people</c>) or has no rate effective today — the banner then names the doctor and
/// offers no button, rather than guessing a charge.
/// </para>
/// </summary>
public sealed record ApptSuggestion(
    int SerialNo, string DoctorName, string State,
    long? ServiceId, string? ServiceName, long Price)
{
    public bool CanAdd => ServiceId is > 0;
}

/// <summary>
/// 05 §5 screen 4 — the POS template. The cart lives in the form, not in the database: nothing
/// is written until the operator saves, so an abandoned bill leaves no trace. Prices are always
/// re-resolved server-side from the effective-dated rate plan (C6) — the browser never sets a price.
/// </summary>
[Authorize(Policy = Perm.BillingInvoiceCreate)]
public class OpdModel(
    HmsTx tx, BillingService billing, RateResolver rates, ApprovalEngine approvals,
    TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    /// <summary>Catalog ids in the cart, carried between round trips as hidden fields.</summary>
    [BindProperty] public List<long> Items { get; set; } = [];
    [BindProperty, Money] public long DiscountFlat { get; set; }
    [BindProperty, Money] public long PaidNow { get; set; }
    [BindProperty] public string Tender { get; set; } = "cash";
    /// <summary>5A-4 [Must]: a second tender on the same invoice — cash + card/mobile.</summary>
    [BindProperty, Money] public long PaidNow2 { get; set; }
    [BindProperty] public string Tender2 { get; set; } = "card";
    [BindProperty, StringLength(Bounds.Code)] public string? TenderRef2 { get; set; }
    /// <summary>§5 M4 [M]: discounts carry an operator-stated reason, not a generated one.</summary>
    [BindProperty, StringLength(Bounds.Note)] public string? DiscountReason { get; set; }
    /// <summary>Spec 0021: one prepared bill, one invoice — survives a double-click.</summary>
    [BindProperty] public Guid SubmissionToken { get; set; }

    public OpenSession? Session { get; private set; }
    public IReadOnlyList<CatalogItem> Catalog { get; private set; } = [];
    public IReadOnlyList<CartLine> Cart { get; private set; } = [];
    public IReadOnlyList<UnbilledLine> Unbilled { get; private set; } = [];
    public string? PatientName { get; private set; }
    /// <summary>Set when the selected patient is lying in a bed right now (spec 0020).</summary>
    public (long AdmissionId, string AdmissionNo, string Bed)? Admitted { get; private set; }
    public long ApprovedDiscountId { get; private set; }
    public long ApprovedDiscountAmount { get; private set; }
    public bool DiscountPending { get; private set; }
    /// <summary>Set when this patient holds a live serial today (spec 0043).</summary>
    public ApptSuggestion? Suggestion { get; private set; }

    public long CartTotal => Cart.Sum(c => c.Price);
    public long UnbilledTotal => Unbilled.Sum(u => u.Amount);
    public long Gross => CartTotal + UnbilledTotal;

    public async Task OnGetAsync()
    {
        SubmissionToken = Submission.NewToken();      // one per visit to the screen
        await LoadAsync();
    }

    /// <summary>Patient change / catalogue filter: re-render with the cart intact.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        await tx.RunAsync(async s =>
        {
            Session = await CounterContext.FindOpenAsync(s.Bill, ActorId);

            var services = await s.Adm.Services.AsNoTracking()
                .Where(x => x.Active)
                .OrderBy(x => x.Dept).ThenBy(x => x.Name)
                .ToListAsync();

            var catalog = new List<CatalogItem>();
            foreach (var svc in services)
            {
                // A service with no effective rate today is not sellable today — leave it out of
                // the catalogue entirely rather than fail at save (§7 U7, edge 11).
                try
                {
                    var rate = await rates.ResolveAsync(s.Adm, "service", svc.Id, today);
                    catalog.Add(new CatalogItem(svc.Id, svc.Code, svc.Name, svc.Dept, rate.Price, rate.RateVersionId));
                }
                catch (RateResolutionException) { }
            }

            Catalog = string.IsNullOrWhiteSpace(Q)
                ? catalog
                : catalog.Where(c =>
                    c.Name.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                    c.Code.Contains(Q, StringComparison.OrdinalIgnoreCase)).ToList();

            var byId = catalog.ToDictionary(c => c.Id);
            Cart = Items.Where(byId.ContainsKey)
                .Select(id => new CartLine(id, byId[id].Name, byId[id].Price))
                .ToList();

            if (PatientId is { } pid and > 0)
            {
                var patient = await s.Reg.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid);
                PatientName = patient?.FullName;
                // Spec 0020 gap 3: an outdoor invoice for an in-house patient is a due that
                // discharge never sees — say so before it is saved.
                Admitted = await IpdBilling.FindOpenAdmissionAsync(s, pid);

                var encounter = await s.Bill.Encounters.AsNoTracking().FirstOrDefaultAsync(
                    e => e.PatientId == pid && e.OnDate == today && e.State == "open");
                if (encounter is not null)
                {
                    // The §9A.2 seam: charges another counter or the doctor raised are already
                    // here — the operator never re-types what the hospital already knows.
                    Unbilled = await s.Bill.ChargeLines.AsNoTracking()
                        .Where(c => c.EncounterId == encounter.Id && c.InvoiceId == null)
                        .Select(c => new UnbilledLine(c.DescriptionSnapshot, c.Amount, c.SourceModule))
                        .ToListAsync();
                }

                // A discount already approved for this patient and not yet spent on an invoice.
                // kernel.* and bill.* are separate contexts (ADR-0003), so "not yet spent" is a
                // second query rather than a sub-select across the module boundary.
                var approvedForPatient = await s.Kernel.ApprovalRequests.AsNoTracking()
                    .Where(a => a.Type == "discount" && a.SourceTable == "reg.patient"
                                && a.SourceId == pid && a.State == ApprovalState.Approved)
                    .OrderByDescending(a => a.Id)
                    .Select(a => new { a.Id, a.Amount })
                    .ToListAsync();

                if (approvedForPatient.Count > 0)
                {
                    var candidateIds = approvedForPatient.Select(a => a.Id).ToList();
                    var spent = await s.Bill.Invoices.AsNoTracking()
                        .Where(i => i.DiscountApprovalId != null
                                    && candidateIds.Contains(i.DiscountApprovalId!.Value))
                        .Select(i => i.DiscountApprovalId!.Value)
                        .ToListAsync();

                    var usable = approvedForPatient.FirstOrDefault(a => !spent.Contains(a.Id));
                    if (usable is not null)
                    {
                        ApprovedDiscountId = usable.Id;
                        ApprovedDiscountAmount = usable.Amount ?? 0;
                    }
                }

                DiscountPending = await s.Kernel.ApprovalRequests.AnyAsync(a =>
                    a.Type == "discount" && a.SourceTable == "reg.patient" && a.SourceId == pid
                    && a.State == ApprovalState.Pending);

                Suggestion = await BuildSuggestionAsync(s, pid, today, byId);
            }
            return 0;
        });
    }

    /// <summary>
    /// The seam this screen was missing (spec 0043): the appointments module is written to by the
    /// receptionist and, until now, read by nobody downstream — so the cashier re-selected by hand
    /// the consultation the hospital had already named. Same principle as <see cref="Unbilled"/>
    /// above: the operator never re-types what the hospital already knows.
    /// <para>
    /// Suggest-only. Nothing enters the cart without the cashier pressing the button, because a
    /// charge that appears on its own is a charge nobody chose.
    /// </para>
    /// </summary>
    private async Task<ApptSuggestion?> BuildSuggestionAsync(
        TxScope s, long patientId, DateOnly today, IReadOnlyDictionary<long, CatalogItem> catalogById)
    {
        // A serial that is done, cancelled or a no-show is not a consultation waiting to be paid
        // for. `done` is excluded deliberately: the doctor has seen the patient, and on this
        // product's flow that only happens after billing, so re-offering it would double-charge.
        string[] live = [AppointmentState.Booked, AppointmentState.Arrived, AppointmentState.InChamber];

        var appt = await s.Appt.Appointments.AsNoTracking()
            .Where(a => a.PatientId == patientId && a.OnDate == today && live.Contains(a.State))
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();
        if (appt is null) return null;

        var doctor = await s.Appt.Doctors.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == appt.DoctorId);
        // The schedule carries the display snapshot; fall back to it so an appointment always
        // renders a name even if the master row was deactivated.
        var name = doctor?.Name
            ?? (await s.Appt.Schedules.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.DoctorId == appt.DoctorId))?.DoctorName
            ?? "Doctor";

        if (doctor?.ConsultationServiceId is not { } serviceId)
            return new ApptSuggestion(appt.SerialNo, name, appt.State, null, null, 0);

        // Already in the cart, or already raised by another counter and sitting in Unbilled —
        // either way the patient is being charged for it once, and offering it again is how a
        // double charge gets made by an operator who trusts the screen.
        if (Items.Contains(serviceId)) return null;

        if (!catalogById.TryGetValue(serviceId, out var item))
            // Inactive, or no rate effective today. Name the doctor, offer nothing.
            return new ApptSuggestion(appt.SerialNo, name, appt.State, null, null, 0);

        if (Unbilled.Any(u => u.Description == item.Name)) return null;

        return new ApptSuggestion(appt.SerialNo, name, appt.State, item.Id, item.Name, item.Price);
    }

    /// <summary>
    /// Accepting the suggestion. Deliberately not <see cref="OnPostAddAsync"/>: same effect, but a
    /// distinct handler means the audit trail can tell a line the system proposed from one the
    /// cashier hunted down, which is the only way to know later whether this helped.
    /// </summary>
    public async Task<IActionResult> OnPostAddSuggestionAsync(long catalogId)
    {
        Items.Add(catalogId);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(long catalogId)
    {
        Items.Add(catalogId);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int index)
    {
        if (index >= 0 && index < Items.Count) Items.RemoveAt(index);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadAsync();

        if (Session is null) { Fail("Open your counter before billing."); return Page(); }
        if (PatientId is null or 0) { Fail("Select a patient first."); return Page(); }
        if (Cart.Count == 0 && Unbilled.Count == 0) { Fail("Add at least one service."); return Page(); }
        // The displayed cart is Items filtered to the sellable catalogue; a mismatch means the
        // form carried an id that is not sellable today. Refuse rather than bill a subset of
        // what the operator believes is in the cart (AUD-VAL-05b).
        if (Cart.Count != Items.Count)
        {
            Fail("A service in this cart is not in today's catalogue any more — remove it and add it again.");
            return Page();
        }
        if (!Tenders.IsKnown(Tender) || (PaidNow2 > 0 && !Tenders.IsKnown(Tender2)))
        {
            Fail($"That is not a way money can be taken. Use one of: {string.Join(", ", Tenders.All)}.");
            return Page();
        }

        var gross = Gross;
        var discount = Math.Max(0, Math.Min(gross, DiscountFlat));
        long? approvalId = null;

        if (discount > 0 && string.IsNullOrWhiteSpace(DiscountReason))
        {
            Fail("A discount needs a reason — it is attributed to you and shows on the MD dashboard.");
            return Page();
        }

        if (discount > 0)
        {
            if (ApprovedDiscountId > 0 && ApprovedDiscountAmount >= discount)
            {
                approvalId = ApprovedDiscountId;
            }
            else
            {
                // §12/C7: routing and thresholds are policy data. Under the role's threshold this
                // returns approved synchronously, so ordinary small discounts never wait (§8 N1).
                var raise = await tx.RunAsync(s => approvals.RaiseAsync(
                    s.Kernel, BranchId, "discount", "reg.patient", PatientId!.Value,
                    ActorId, ActorRole, DiscountReason!.Trim(), discount));

                if (!raise.AutoApproved)
                {
                    await LoadAsync();
                    DiscountPending = true;
                    Fail($"A discount of {Ui.Money(discount)} is above your limit. " +
                         "The request has gone to the supervisor's approvals inbox — " +
                         "the bill is held here until it is decided.");
                    return Page();
                }
                approvalId = raise.ApprovalId;
            }
        }

        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var net = gross - discount;
        // Split tender: each row becomes its own receipt against the one invoice, so the
        // day-close tender breakdown stays truthful about what actually entered the drawer.
        var tenders = new List<(long Amount, string Mode, string? Reference)>();
        if (PaidNow > 0) tenders.Add((PaidNow, Tender, null));
        if (PaidNow2 > 0) tenders.Add((PaidNow2, Tender2, TenderRef2));
        if (tenders.Sum(x => x.Amount) > net)
        {
            Fail($"The payment adds up to more than the {Ui.Money(net)} payable. " +
                 "Reduce a line — change is handled at the drawer, not on the invoice.");
            return Page();
        }
        var cartIds = Items.ToList();

        try
        {
            var invoiceId = await tx.RunAsync(async s =>
            {
                // Spec 0021: a re-post of this same prepared bill lands on its invoice.
                if (await Submission.ExistingAsync(s, SubmissionToken) is { } already)
                    return already.Id;

                // R4 (spec 0017): a due-blocked patient takes no new charges at any counter.
                await IpdBilling.EnsureNotBlockedAsync(s, PatientId!.Value);

                var encounter = await CounterContext.GetOrCreateEncounterAsync(
                    s.Bill, BranchId, PatientId!.Value, Session!.CounterId, today, Session.EncounterType,
                    ActorId, clock.GetUtcNow());

                foreach (var id in cartIds)
                {
                    var svc = await s.Adm.Services.AsNoTracking().SingleAsync(x => x.Id == id);
                    var rate = await rates.ResolveAsync(s.Adm, "service", id, today);
                    await billing.PostChargeAsync(s.Bill, BranchId, encounter.Id, "Billing",
                        new NewChargeLine("service", id, svc.Name, 1, rate.Price, rate.RateVersionId),
                        ActorId);
                }

                var invoice = await billing.CreateInvoiceAsync(
                    s.Bill, s.Kernel, BranchId, encounter.Id, Session.Id, PatientId.Value,
                    0m, discount, approvalId, ActorId, ActorName,
                    submissionToken: SubmissionToken);

                foreach (var (amount, mode, reference) in tenders)
                    await billing.CollectAsync(s.Bill, s.Kernel, BranchId, invoice.Id, Session.Id,
                        amount, mode, reference, ActorId, ActorName);

                // WP1.4 (AUD-VAL-04): a save that claims a payment produces a receipt or the
                // whole transaction fails — asserted here, not only trusted to the binder.
                var claimed = tenders.Sum(x => x.Amount);
                if (claimed > 0)
                {
                    var receipted = await s.Bill.Receipts
                        .Where(r => r.InvoiceId == invoice.Id)
                        .SumAsync(r => (long?)r.Amount) ?? 0;
                    if (receipted != claimed)
                        throw new BillingException(
                            "The payment could not be receipted, so the bill was not saved. Try again.");
                }

                return invoice.Id;
            });

            Toast("Invoice saved — money receipt ready", "receipt_long");
            return Redirect($"/billing/invoice/{invoiceId}");
        }
        catch (DbUpdateException e) when (Submission.IsDuplicateSubmission(e))
        {
            // Lost the race to a concurrent post of the same submission: the winner's invoice
            // is the right answer, not a second bill.
            var existing = await tx.RunAsync(s => Submission.ExistingAsync(s, SubmissionToken));
            if (existing is not null) return Redirect($"/billing/invoice/{existing.Id}");
            Fail("That bill was just saved — reload the screen to see it.");
            return Page();
        }
        catch (BillingException e)
        {
            Fail(e.Message);
            return Page();
        }
        catch (Hms.Ipd.IpdException e)
        {
            Fail(e.Message);
            return Page();
        }
        catch (RateResolutionException e)
        {
            Fail(e.Message);
            return Page();
        }
    }
}
