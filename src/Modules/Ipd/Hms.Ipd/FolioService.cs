using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Ipd;

/// <summary>
/// The folio gate (§11 Patient Folio machine). Every posting path — services, indents,
/// investigations, bed days — comes through <see cref="EnsurePostableAsync"/>, which locks the
/// folio row for the transaction and enforces the state rules: blocked folios refuse (R4),
/// settlement drafts are frozen, locked folios demand the Billing-Supervisor late-post
/// approval (§12). Money itself is posted by the composition root through the billing spine.
/// </summary>
public sealed class FolioService(AuditWriter audit, BusinessDayCalendar businessDay, TimeProvider clock)
{
    public async Task<Folio> GetAsync(IpdDbContext ipd, long folioId, CancellationToken ct = default)
        => await ipd.Folios.SingleOrDefaultAsync(f => f.Id == folioId, ct)
           ?? throw new IpdException("Unknown folio.");

    /// <summary>SELECT … FOR UPDATE — the serialization point for everything folio.</summary>
    public async Task<string> LockAsync(IpdDbContext ipd, long folioId, CancellationToken ct = default)
    {
        var states = await ipd.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM ipd.folio WHERE id = {folioId} FOR UPDATE
            """).ToListAsync(ct);
        if (states.Count == 0) throw new IpdException("Unknown folio.");
        return states[0];
    }

    /// <summary>Locks the folio and returns only if posting is allowed right now.</summary>
    public async Task EnsurePostableAsync(
        IpdDbContext ipd, KernelDbContext kernel, long folioId, long? latePostApprovalId = null,
        CancellationToken ct = default)
    {
        var state = await LockAsync(ipd, folioId, ct);
        switch (state)
        {
            case FolioState.Open:
                return;
            case FolioState.Blocked:
                throw new IpdException(
                    "This patient is blocked for dues (R4) — clear or release before charging.");
            case FolioState.SettlementDraft:
                throw new IpdException("Settlement is being prepared — the folio is frozen.");
            case FolioState.Locked when latePostApprovalId is long approvalId:
                await IpdService.VerifyApprovalAsync(kernel, approvalId, "folio-late-post", ct);
                return;
            case FolioState.Locked:
                throw new IpdException(
                    "This folio is locked (settled). Post-lock entries need a Billing Supervisor approval.");
            default:
                throw new IpdException($"Folio is in an unexpected state ({state}).");
        }
    }

    // ---- bed days (P18 rule) -----------------------------------------------

    public sealed record UnpostedBedDays(long BedId, long TariffServiceId, IReadOnlyList<DateOnly> Dates);

    /// <summary>
    /// The dates owed but not yet charged: admission's business day through today's, minus
    /// what bed_day already holds. The whole span charges at the CURRENT bed — transfers
    /// re-point charging from the first unposted date (P18; posted money is never reversed).
    /// </summary>
    public async Task<UnpostedBedDays> ComputeUnpostedBedDaysAsync(
        IpdDbContext ipd, long admissionId, CancellationToken ct = default)
    {
        var admission = await ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId, ct);
        var stay = await ipd.BedStays.AsNoTracking()
                       .SingleOrDefaultAsync(s => s.AdmissionId == admissionId && s.ToAt == null, ct)
                   ?? await ipd.BedStays.AsNoTracking().Where(s => s.AdmissionId == admissionId)
                       .OrderByDescending(s => s.FromAt).FirstOrDefaultAsync(ct)
                   ?? throw new IpdException("No bed stay for this admission.");

        var bed = await ipd.Beds.AsNoTracking().SingleAsync(b => b.Id == stay.BedId, ct);

        var firstDay = businessDay.BusinessDayOf(admission.AdmittedAt);
        var lastDay = admission.DischargedAt is DateTimeOffset gone
            ? businessDay.BusinessDayOf(gone)
            : businessDay.BusinessDayOf(clock.GetUtcNow());
        if (lastDay < firstDay) lastDay = firstDay;                    // same-day minimum: 1 day

        var posted = await ipd.BedDays.AsNoTracking()
            .Where(d => d.AdmissionId == admissionId)
            .Select(d => d.OnDate).ToListAsync(ct);
        var postedSet = posted.ToHashSet();

        var dates = new List<DateOnly>();
        for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
            if (!postedSet.Contains(d))
                dates.Add(d);
        return new UnpostedBedDays(bed.Id, bed.TariffServiceId, dates);
    }

    /// <summary>Records one charged bed day. The UNIQUE index turns a concurrent double-post
    /// into a constraint error the caller surfaces comprehensibly.</summary>
    public void RecordBedDay(IpdDbContext ipd, long admissionId, DateOnly onDate, long bedId, long chargeLineId)
        => ipd.BedDays.Add(new BedDay
        {
            AdmissionId = admissionId, OnDate = onDate, BedId = bedId, ChargeLineId = chargeLineId,
        });

    // ---- settlement state steps (invoiced by the composition root) ---------

    public async Task BeginSettlementAsync(IpdDbContext ipd, long folioId, CancellationToken ct = default)
    {
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.folio SET state = 'settlement_draft' WHERE id = {folioId} AND state = 'open'
            """, ct);
        if (affected == 0)
            throw new IpdException("Only an open folio can enter settlement (blocked ones need release).");
    }

    /// <summary>Back out of a draft (e.g. late charges must still land) — draft → open.</summary>
    /// <summary>
    /// A settlement draft back to open, so a late charge can still land before the bill issues.
    ///
    /// Audited at tier 2 (P24). The money is traceable without this — every charge line carries
    /// whoever posted it, and the invoice is audited when the folio locks — but "this discharge
    /// bill was assembled, withdrawn, and assembled again" was not recoverable from anywhere,
    /// and that is the shape of a discount dispute at a counter. A confirmed settlement is not
    /// reachable from here at all: once locked, the guard below refuses, and a post-lock charge
    /// needs a Billing Supervisor's approval (see <see cref="EnsurePostableAsync"/>).
    ///
    /// Tier 1 with a before/after image, not tier 2: ADR-0011 puts money on tier 1, and this is
    /// the exact inverse of <see cref="LockAtSettlementAsync"/> beside it. A pair of transitions
    /// that undo each other belongs in one tier, or the timeline reads as if only half of it
    /// happened.
    /// </summary>
    public async Task ReopenDraftAsync(
        IpdDbContext ipd, KernelDbContext kernel, long folioId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.folio SET state = 'open' WHERE id = {folioId} AND state = 'settlement_draft'
            """, ct);
        if (affected == 0) throw new IpdException("The folio is not in settlement draft.");

        var folio = await GetAsync(ipd, folioId, ct);
        audit.Append(kernel, folio.BranchId, actorId, actorName,
            "ipd.settlement.reopen", "ipd.folio", folioId,
            before: new { state = FolioState.SettlementDraft },
            after: new { state = FolioState.Open });
        await kernel.SaveChangesAsync(ct);
    }

    public async Task LockAtSettlementAsync(
        IpdDbContext ipd, KernelDbContext kernel, long folioId, long settlementInvoiceId,
        long advanceApplied, long actorId, string actorName, CancellationToken ct = default)
    {
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.folio SET state = 'locked', settlement_invoice_id = {settlementInvoiceId},
                advance_applied = {advanceApplied}
            WHERE id = {folioId} AND state = 'settlement_draft'
            """, ct);
        if (affected == 0) throw new IpdException("Settlement must be prepared before it locks.");

        var folio = await GetAsync(ipd, folioId, ct);
        audit.Append(kernel, folio.BranchId, actorId, actorName,
            "ipd.folio.lock", "ipd.folio", folioId,
            after: new { settlementInvoiceId, advanceApplied });
        await kernel.SaveChangesAsync(ct);
    }

    // ---- medicine indents (5A-9) -------------------------------------------

    public async Task<Indent> CreateIndentAsync(
        IpdDbContext ipd, KernelDbContext kernel, long branchId, long admissionId,
        IReadOnlyList<(long ProductId, int Qty)> items, string? note,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (items.Count == 0) throw new IpdException("An indent needs at least one item.");
        if (items.Any(i => i.Qty <= 0)) throw new IpdException("Quantities must be positive.");

        var folio = await ipd.Folios.SingleOrDefaultAsync(f => f.AdmissionId == admissionId, ct)
                    ?? throw new IpdException("No folio — is the patient admitted?");
        if (folio.State == FolioState.Blocked)
            throw new IpdException("This patient is blocked for dues (R4) — no new requisitions.");
        if (folio.State != FolioState.Open)
            throw new IpdException("The folio no longer accepts requisitions.");

        var indent = new Indent
        {
            BranchId = branchId, AdmissionId = admissionId, FolioId = folio.Id,
            Note = note, RequestedBy = actorId, RequestedAt = clock.GetUtcNow(),
        };
        ipd.Indents.Add(indent);
        await ipd.SaveChangesAsync(ct);

        foreach (var (productId, qty) in items)
            ipd.IndentItems.Add(new IndentItem
            {
                IndentId = indent.Id, ProductId = productId, QtyRequested = qty,
            });
        await ipd.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "ipd.indent.create", "ipd.indent", indent.Id,
            after: new { admissionId, items = items.Count });
        await kernel.SaveChangesAsync(ct);
        return indent;
    }

    public async Task CancelIndentAsync(
        IpdDbContext ipd, KernelDbContext kernel, long indentId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.indent SET state = 'cancelled' WHERE id = {indentId} AND state = 'requested'
            """, ct);
        if (affected == 0) throw new IpdException("Only a requested indent can be cancelled.");
        var indent = await ipd.Indents.AsNoTracking().SingleAsync(i => i.Id == indentId, ct);
        audit.Append(kernel, indent.BranchId, actorId, actorName,
            "ipd.indent.cancel", "ipd.indent", indentId, after: null);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>State-guarded claim: two pharmacists issuing the same indent — one wins.</summary>
    public async Task ClaimIndentForIssueAsync(IpdDbContext ipd, long indentId, long actorId,
        CancellationToken ct = default)
    {
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.indent SET state = 'issued', issued_by = {actorId}, issued_at = {clock.GetUtcNow()}
            WHERE id = {indentId} AND state = 'requested'
            """, ct);
        if (affected == 0) throw new IpdException("This indent was already issued or cancelled — refresh.");
    }
}
