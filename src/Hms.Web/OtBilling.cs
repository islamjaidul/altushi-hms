using Hms.Admin;
using Hms.Billing;
using Hms.Ipd;
using Hms.Ot.Data;
using Hms.Pharmacy;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// The M7 → M6/M4/M11 seam. An operation's money spans three modules — the folio (Ipd), the
/// charge lines (Billing) and the consumable stock (Pharmacy) — so it is composed here, exactly
/// as <see cref="IpdBilling"/> and <see cref="EmrOrdering"/> are (ADR-0003).
///
/// US7.2: "surgery revenue is never under-billed" — the posting derives from the operation
/// catalogue and the team setup, so nobody re-types a fee at the counter.
/// </summary>
public static class OtBilling
{
    public sealed record PostedCharge(string Description, long Amount);

    /// <summary>
    /// Posts the operation fee and one line per team role that carries a fee service. Runs inside
    /// the caller's transaction alongside <c>OtService.CompleteAsync</c>, so a case is either
    /// completed *and* billed, or neither.
    /// </summary>
    public static async Task<IReadOnlyList<PostedCharge>> PostCompletionChargesAsync(
        TxScope s, BillingService billing, FolioService folios, RateResolver rates,
        TimeProvider clock, long branchId, OtCase otCase, long actorId,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var posted = new List<PostedCharge>();

        var operationRate = await rates.ResolveAsync(s.Adm, "service", otCase.OperationServiceId, today);
        if (operationRate.Price > 0)
        {
            await PostAsync(s, billing, folios, branchId, otCase,
                otCase.OperationServiceId, $"{otCase.OperationName} ({otCase.CaseNo})",
                operationRate.Price, operationRate.RateVersionId, actorId, ct);
            posted.Add(new PostedCharge(otCase.OperationName, operationRate.Price));
        }

        var team = await s.Ot.Team.Where(t => t.CaseId == otCase.Id).ToListAsync(ct);
        foreach (var member in team.Where(t => t.FeeServiceId is not null))
        {
            var rate = await rates.ResolveAsync(s.Adm, "service", member.FeeServiceId!.Value, today);
            if (rate.Price <= 0) continue;

            var line = await PostAsync(s, billing, folios, branchId, otCase,
                member.FeeServiceId.Value, $"{Ui.Humanize(member.Role)} — {member.PersonName}",
                rate.Price, rate.RateVersionId, actorId, ct, doctorId: member.PersonId);

            // US7.3: what each team member earned is recorded on the case, so M17 computes
            // payouts from the record rather than from a month-end spreadsheet.
            member.AmountPosted = rate.Price;
            member.ChargeLineId = line;
            posted.Add(new PostedCharge($"{Ui.Humanize(member.Role)} — {member.PersonName}", rate.Price));
        }
        await s.Ot.SaveChangesAsync(ct);
        return posted;
    }

    /// <summary>
    /// Issues a consumable to the case: FEFO off the pharmacy outlet, then a folio (or visit)
    /// charge at that batch's price, in one transaction. Same kernel the POS uses (ADR-0021).
    /// </summary>
    public static async Task<IReadOnlyList<PostedCharge>> IssueConsumableAsync(
        TxScope s, BillingService billing, FolioService folios, StockService stock,
        TimeProvider clock, long branchId, OtCase otCase, long outletId, long productId, int qty,
        long actorId, CancellationToken ct = default)
    {
        if (qty <= 0) throw new OtBillingException("How many?");
        if (otCase.State is not (CaseState.InTheatre or CaseState.PatientReady))
            throw new OtBillingException(
                "Consumables are issued to a case that is in theatre — not before, not after.");

        var product = await s.Pharm.Products.AsNoTracking().SingleAsync(p => p.Id == productId, ct);
        var allocations = await stock.AllocateFefoAsync(
            s.Pharm, outletId, productId, qty, "ot.ot_case", otCase.Id, actorId);

        var posted = new List<PostedCharge>();
        foreach (var a in allocations)
        {
            var description = $"{product.Brand} {product.Strength} (batch {a.BatchNo})";
            var lineId = await PostAsync(s, billing, folios, branchId, otCase, product.Id,
                description, a.UnitMrp, null, actorId, ct, qty: a.Qty, kind: "medicine");

            s.Ot.Consumables.Add(new CaseConsumable
            {
                CaseId = otCase.Id, ProductId = product.Id,
                ProductName = $"{product.Brand} {product.Strength}", Qty = a.Qty,
                BatchNo = a.BatchNo, UnitPrice = a.UnitMrp, ChargeLineId = lineId,
                At = clock.GetUtcNow(), IssuedBy = actorId,
            });
            posted.Add(new PostedCharge(description, a.UnitMrp * a.Qty));
        }
        await s.Ot.SaveChangesAsync(ct);
        return posted;
    }

    /// <summary>
    /// One charge, to whichever parent the case has. The folio path goes through the Ipd gate so
    /// a locked or blocked folio refuses here exactly as it refuses a ward service (spec 0017).
    /// </summary>
    private static async Task<long> PostAsync(
        TxScope s, BillingService billing, FolioService folios, long branchId, OtCase otCase,
        long catalogId, string description, long unitPrice, long? rateVersionId, long actorId,
        CancellationToken ct, long? doctorId = null, int qty = 1, string kind = "service")
    {
        var line = new NewChargeLine(kind, catalogId, description, qty, unitPrice,
            rateVersionId, doctorId);

        if (otCase.FolioId is { } folioId)
        {
            await folios.EnsurePostableAsync(s.Ipd, s.Kernel, folioId, null, ct);
            var charge = await billing.PostFolioChargeAsync(
                s.Bill, branchId, folioId, "Ot", line, actorId, ct: ct);
            return charge.Id;
        }

        var encounterCharge = await billing.PostChargeAsync(
            s.Bill, branchId, otCase.EncounterId!.Value, "Ot", line, actorId, ct);
        return encounterCharge.Id;
    }
}

public sealed class OtBillingException(string message) : Exception(message);
