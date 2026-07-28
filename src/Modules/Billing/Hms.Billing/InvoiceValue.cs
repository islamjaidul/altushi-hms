using Hms.Billing.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Billing;

/// <summary>One invoice reduced to what an income figure needs to know about it.</summary>
public sealed record InvoiceMoneyRow(long Id, long PatientId, string State, long Net, long Discount);

/// <summary>
/// What a set of invoices earned. <paramref name="Invoices"/> and <paramref name="Patients"/>
/// count only invoices that were not reversed — a cancelled bill is not a patient billed.
/// </summary>
public sealed record BilledTotals(long Income, long Discount, int Invoices, int Patients);

/// <summary>
/// <b>What an invoice is actually worth after a reversal</b> — the one definition, so that the
/// MD dashboard and the day-close statement cannot disagree about the same money (spec 0032,
/// M4-D1 / M4-D4 / M22-D1).
///
/// <para><b>The invariant.</b> For every invoice, in every state:</para>
/// <code>Σ receipts + due.balance = Realised(state, net, refunded)</code>
/// <para>
/// `Σ receipts` already nets refunds out (a refund is a negative receipt, hard rule 4), so the
/// three terms read as: money in the drawer, money still owed, and what the invoice ended up
/// being worth. LC-BIL-04 states this as the unqualified <c>Σ receipts + due = net</c>, which is
/// true only until something is reversed — a cancelled invoice zeroes its due with nothing to
/// offset the net, and a refunded one hands money back that the net still claims.
/// </para>
///
/// <para><b>Why the due is NOT restored on a refund.</b> The other candidate fix for M4-D1 was to
/// add the refunded amount back onto <c>bill.due.balance</c>, which also makes the arithmetic
/// close. It was rejected: a due is a *receivable*. Restoring it puts the patient back on
/// `/billing/dues` and into the MD's "Outstanding due" tile for money the hospital just decided
/// to give back, so the hospital would chase a refund it granted. A refund reduces what the
/// invoice earned; it does not re-charge the patient. Reversal belongs on the invoice's value,
/// not in the receivables ledger.</para>
///
/// <para><b>The two consumers read different members, deliberately.</b>
/// <list type="bullet">
/// <item><description>The <b>MD dashboard</b> uses <see cref="Realised"/> for its income tile. It
/// has no refunds line, and its whole job is to be comparable with the collected tile — a partial
/// refund must move both or the difference reads like an unpaid due.</description></item>
/// <item><description>The <b>day-close statement</b> uses <see cref="IsReversed"/> to drop
/// reversed invoices from Gross/Discount/Net, and leaves its existing separate `Refunds` line to
/// carry money handed back. The statement prints "Gross billed / Less: discount given / Net
/// billed" as a visible subtraction, so <c>Net = Gross − Discount</c> has to hold on the page;
/// netting a partial refund into `Net` there would break the printed arithmetic and double-count
/// against the `Refunds` line right below it.</description></item>
/// </list></para>
/// </summary>
public static class InvoiceValue
{
    /// <summary>
    /// A reversed invoice is finished: it earned nothing and counts toward no income figure.
    /// `Cancelled` is the pre-payment exit and `Refunded` the post-payment one (§11).
    /// A *partially* refunded invoice is NOT reversed — it is still a live invoice worth less
    /// than its net (see <see cref="Realised"/>).
    /// </summary>
    public static bool IsReversed(string state)
        => state is InvoiceState.Cancelled or InvoiceState.Refunded;

    /// <summary>
    /// The part of an invoice's <paramref name="net"/> that survived reversal.
    /// <paramref name="refunded"/> is the positive total handed back against it — i.e.
    /// <c>Σ |negative receipts|</c>, which <see cref="RefundedByInvoiceAsync"/> computes.
    /// </summary>
    public static long Realised(string state, long net, long refunded)
        => IsReversed(state) ? 0 : net - refunded;

    /// <summary>As <see cref="Realised(string,long,long)"/>, for a row and a refund lookup.</summary>
    public static long Realised(InvoiceMoneyRow row, IReadOnlyDictionary<long, long> refundedByInvoice)
        => Realised(row.State, row.Net, refundedByInvoice.GetValueOrDefault(row.Id));

    /// <summary>
    /// What a set of invoices earned, for a screen that reports a period. Pure, so the caller
    /// keeps the window and the timezone (both presentation concerns) and this keeps the money.
    /// </summary>
    public static BilledTotals Totalise(
        IEnumerable<InvoiceMoneyRow> invoices, IReadOnlyDictionary<long, long> refundedByInvoice)
    {
        var rows = invoices as IReadOnlyCollection<InvoiceMoneyRow> ?? [.. invoices];
        var live = rows.Where(i => !IsReversed(i.State)).ToList();
        return new BilledTotals(
            Income: rows.Sum(i => Realised(i, refundedByInvoice)),
            Discount: live.Sum(i => i.Discount),
            Invoices: live.Count,
            Patients: live.Select(i => i.PatientId).Distinct().Count());
    }

    /// <summary>
    /// Money handed back per invoice, as a positive amount, for the invoices asked about.
    /// Invoices with no refund are absent from the dictionary — read it with
    /// <c>GetValueOrDefault</c>.
    /// </summary>
    public static async Task<Dictionary<long, long>> RefundedByInvoiceAsync(
        BillDbContext bill, IReadOnlyCollection<long> invoiceIds, CancellationToken ct = default)
    {
        if (invoiceIds.Count == 0) return [];
        var ids = invoiceIds as ICollection<long> ?? [.. invoiceIds];
        var rows = await bill.Receipts.AsNoTracking()
            .Where(r => r.Amount < 0 && r.InvoiceId != null && ids.Contains(r.InvoiceId!.Value))
            .Select(r => new { InvoiceId = r.InvoiceId!.Value, r.Amount })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => -g.Sum(x => x.Amount));
    }
}
