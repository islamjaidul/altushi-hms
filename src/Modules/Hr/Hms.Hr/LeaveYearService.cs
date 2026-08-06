using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// The leave year, closed and reopened (module PRD §7.6 — G33, G34, G35, G37).
/// <para>
/// <c>LeaveBalance</c> has carried opening, accrued, adjustment, availed and encashed columns since
/// the module shipped. Nothing has ever rolled them: no accrual, no carry-forward, no lapse, and the
/// new year never opened. <c>LeaveEncashment</c> is a table with no writer, and
/// <c>LeaveBalance.AdjustmentBp</c> is the third column this programme has found that is modelled,
/// exposed and never written.
/// </para>
/// <para>
/// "Never partially applied" (§7.6) is the requirement that shapes everything here: the preview is
/// <b>stored</b>, and the commit walks it inside the caller's one transaction. A close that half-ran
/// would leave some people with next year's entitlement and some without, and nothing to say which.
/// </para>
/// </summary>
public sealed class LeaveYearService(PolicyResolver policies, AuditWriter audit, TimeProvider clock)
{
    /// <summary>What one employee-and-type does at the close. Pure, so the arithmetic is pinned.</summary>
    public sealed record CloseMovement(int AccrueBp, int CarryBp, int LapseBp, int EncashBp, string Basis);

    /// <summary>
    /// The close arithmetic for one balance, given the employer's policy.
    /// <para>
    /// Order matters and is stated: what is left at year end is carried up to the cap, then whatever
    /// the policy encashes is encashed out of the carry, and <b>everything still standing lapses</b>.
    /// Lapsing first would silently destroy days the employer meant to carry.
    /// </para>
    /// </summary>
    public static CloseMovement Close(
        int closingBalanceBp, int entitlementBp, int maxCarryBp, bool encashable, int maxEncashBp)
    {
        if (closingBalanceBp <= 0)
        {
            return new CloseMovement(entitlementBp, 0, 0, 0,
                entitlementBp > 0
                    ? $"nothing left to carry; {entitlementBp / 10000m:0.##} day(s) accrued for the new year"
                    : "nothing left to carry, and no entitlement is configured");
        }

        var carry = Math.Min(closingBalanceBp, Math.Max(0, maxCarryBp));
        var encash = encashable ? Math.Min(carry, Math.Max(0, maxEncashBp)) : 0;
        carry -= encash;
        var lapse = closingBalanceBp - carry - encash;

        var parts = new List<string> { $"{closingBalanceBp / 10000m:0.##} day(s) at year end" };
        if (carry > 0) parts.Add($"{carry / 10000m:0.##} carried");
        if (encash > 0) parts.Add($"{encash / 10000m:0.##} encashed");
        if (lapse > 0) parts.Add($"{lapse / 10000m:0.##} lapsed");
        if (entitlementBp > 0) parts.Add($"{entitlementBp / 10000m:0.##} accrued for the new year");

        return new CloseMovement(entitlementBp, carry, lapse, encash, string.Join(", ", parts));
    }

    /// <summary>Proposes a close for a leave year. Nothing moves until it is previewed and committed.</summary>
    public async Task<LeaveYearClose> ProposeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly closingYear,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var live = await hr.LeaveYearCloses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.BranchId == branchId && c.ClosingYear == closingYear
                                      && c.State != LeaveCloseState.Cancelled, ct);
        if (live is not null)
        {
            throw new HrException(
                $"{closingYear:yyyy} is already {LeaveCloseState.Label(live.State).ToLowerInvariant()}.");
        }

        var close = new LeaveYearClose
        {
            BranchId = branchId,
            ClosingYear = closingYear,
            OpeningYear = closingYear.AddYears(1),
            State = LeaveCloseState.Proposed,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
            CreatedByName = actorName,
        };
        hr.LeaveYearCloses.Add(close);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.leave_close.propose",
            "hr.leave_year_close", close.Id, after: new { closingYear, close.OpeningYear }, tier: 1);

        return close;
    }

    /// <summary>
    /// Works out what the close would do, per employee per leave type, and stores it. The approver
    /// sees exactly what will apply rather than a figure recomputed from data that may have moved.
    /// </summary>
    public async Task PreviewAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long closeId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var close = await LoadAsync(hr, closeId, ct);
        Require(close, LeaveCloseState.Proposed, LeaveCloseState.Previewed);

        var existing = await hr.LeaveCloseLines.Where(l => l.CloseId == closeId).ToListAsync(ct);
        hr.LeaveCloseLines.RemoveRange(existing);

        var yearEnd = close.OpeningYear.AddDays(-1);

        var balances = await hr.LeaveBalances.AsNoTracking()
            .Where(b => b.LeaveYear == close.ClosingYear.Year)
            .Select(b => new
            {
                b.EmployeeId, b.LeaveTypeId,
                Available = b.OpeningBp + b.AccruedBp + b.AdjustmentBp - b.AvailedBp - b.EncashedBp,
            })
            .ToListAsync(ct);

        if (balances.Count == 0)
        {
            close.State = LeaveCloseState.Previewed;
            close.PreviewedAt = clock.GetUtcNow();
            close.EmployeeCount = 0;
            await hr.SaveChangesAsync(ct);
            return;
        }

        // Assignments drive the grade-specific leave policy, so they are resolved once.
        var employeeIds = balances.Select(b => b.EmployeeId).Distinct().ToList();
        var grades = await hr.Assignments.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.EffectiveFrom <= yearEnd
                        && (a.EffectiveTo == null || a.EffectiveTo >= yearEnd))
            .Select(a => new { a.EmployeeId, a.GradeId })
            .ToListAsync(ct);
        var gradeOf = grades.GroupBy(g => g.EmployeeId)
            .ToDictionary(g => g.Key, g => (long?)g.First().GradeId);

        int accrued = 0, carried = 0, lapsed = 0, encashed = 0;

        foreach (var balance in balances)
        {
            var policy = await policies.LeaveAsync(
                hr, branchId, balance.LeaveTypeId, gradeOf.GetValueOrDefault(balance.EmployeeId),
                yearEnd, ct);

            // D2: no policy means no rule, and the honest close carries nothing and lapses nothing.
            // Inventing a carry-forward cap would be inventing an entitlement.
            var movement = policy is null
                ? new CloseMovement(0, balance.Available, 0, 0,
                    "no leave policy is configured for this type, so the balance is carried unchanged")
                // No separate encashment cap exists on the policy, so what may be encashed is what
                // may be carried: a day the employer will not carry is not a day it will buy.
                : Close(balance.Available, policy.AnnualEntitlementBp, policy.MaxCarryForwardBp,
                    policy.Encashable, policy.MaxCarryForwardBp);

            hr.LeaveCloseLines.Add(new LeaveCloseLine
            {
                BranchId = branchId,
                CloseId = closeId,
                EmployeeId = balance.EmployeeId,
                LeaveTypeId = balance.LeaveTypeId,
                ClosingBalanceBp = balance.Available,
                AccrueBp = movement.AccrueBp,
                CarryBp = movement.CarryBp,
                LapseBp = movement.LapseBp,
                EncashBp = movement.EncashBp,
                Basis = movement.Basis,
            });

            accrued += movement.AccrueBp;
            carried += movement.CarryBp;
            lapsed += movement.LapseBp;
            encashed += movement.EncashBp;
        }

        close.State = LeaveCloseState.Previewed;
        close.PreviewedAt = clock.GetUtcNow();
        close.EmployeeCount = employeeIds.Count;
        close.AccruedBp = accrued;
        close.CarriedBp = carried;
        close.LapsedBp = lapsed;
        close.EncashedBp = encashed;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.leave_close.preview",
            "hr.leave_year_close", closeId,
            after: new { close.EmployeeCount, accrued, carried, lapsed, encashed }, tier: 1);
    }

    /// <summary>
    /// Applies the whole preview: opens next year's balance for every line, and writes an encashment
    /// where the policy allows one. One transaction — the caller's — so it commits entirely or not
    /// at all (§7.6's "never partially applied").
    /// </summary>
    public async Task CommitAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long closeId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var close = await LoadAsync(hr, closeId, ct);
        Require(close, LeaveCloseState.Previewed);

        var lines = await hr.LeaveCloseLines.Where(l => l.CloseId == closeId).ToListAsync(ct);
        if (lines.Count == 0)
            throw new HrException("There is nothing to commit — preview the close first.");

        var openingYear = close.OpeningYear.Year;

        foreach (var line in lines)
        {
            var next = await hr.LeaveBalances
                .FirstOrDefaultAsync(b => b.EmployeeId == line.EmployeeId
                                          && b.LeaveTypeId == line.LeaveTypeId
                                          && b.LeaveYear == openingYear, ct);

            if (next is null)
            {
                next = new LeaveBalance
                {
                    BranchId = branchId,
                    EmployeeId = line.EmployeeId,
                    LeaveTypeId = line.LeaveTypeId,
                    LeaveYear = openingYear,
                };
                hr.LeaveBalances.Add(next);
            }

            next.OpeningBp = line.CarryBp;
            next.AccruedBp = line.AccrueBp;

            if (line.EncashBp > 0)
            {
                hr.LeaveEncashments.Add(new LeaveEncashment
                {
                    BranchId = branchId,
                    EmployeeId = line.EmployeeId,
                    LeaveTypeId = line.LeaveTypeId,
                    LeaveYear = close.ClosingYear.Year,
                    DaysBp = line.EncashBp,
                    // Priced when payroll pays it: a day's rate is a payroll question and the
                    // close has no business guessing at one.
                    AmountTaka = 0,
                    CreatedAt = clock.GetUtcNow(),
                    CreatedBy = actorId,
                });

                // The closing year's balance records that the days went, so its own arithmetic
                // still adds up after the close.
                var closing = await hr.LeaveBalances
                    .FirstOrDefaultAsync(b => b.EmployeeId == line.EmployeeId
                                              && b.LeaveTypeId == line.LeaveTypeId
                                              && b.LeaveYear == close.ClosingYear.Year, ct);
                if (closing is not null) closing.EncashedBp += line.EncashBp;
            }

            line.Applied = true;
        }

        close.State = LeaveCloseState.Committed;
        close.CommittedAt = clock.GetUtcNow();
        close.CommittedBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.leave_close.commit",
            "hr.leave_year_close", closeId,
            after: new { close.ClosingYear, close.OpeningYear, lines = lines.Count,
                close.CarriedBp, close.LapsedBp, close.EncashedBp }, tier: 1);
    }

    // ---- balance adjustment (G35) -------------------------------------------------------------

    /// <summary>
    /// Moves a balance by hand, with a mandatory reason (§7.6, G35). <c>AdjustmentBp</c> has existed
    /// since the module shipped and nothing has ever written it — so an HR officer who needed to
    /// correct a balance had no way to, and no record if they had.
    /// </summary>
    public async Task AdjustAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId, long leaveTypeId,
        int leaveYear, int deltaBp, string reason, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("An adjustment needs a reason — it is what makes it reviewable.");
        if (deltaBp == 0)
            throw new HrException("An adjustment of zero days changes nothing.");

        var balance = await hr.LeaveBalances
            .FirstOrDefaultAsync(b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId
                                      && b.LeaveYear == leaveYear, ct)
            ?? throw new HrException(
                "No balance exists for that employee, leave type and year — a leave policy has to "
                + "open one first.");

        var before = balance.AdjustmentBp;
        var available = balance.OpeningBp + balance.AccruedBp + balance.AdjustmentBp
                        - balance.AvailedBp - balance.EncashedBp;

        if (deltaBp < 0 && -deltaBp > available)
        {
            throw new HrException(
                $"Only {available / 10000m:0.##} day(s) are available — a balance cannot be adjusted "
                + "below zero. The database refuses it too.");
        }

        balance.AdjustmentBp += deltaBp;

        audit.Append(kernel, branchId, actorId, actorName, "hr.leave.adjust", "hr.leave_balance",
            balance.Id,
            before: new { AdjustmentBp = before },
            after: new { AdjustmentBp = balance.AdjustmentBp, deltaBp, reason }, tier: 1);
    }

    // ---- loading -------------------------------------------------------------------------------

    private static async Task<LeaveYearClose> LoadAsync(HrDbContext hr, long id, CancellationToken ct)
    {
        var locked = await hr.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM hr.leave_year_close WHERE id = {id} FOR UPDATE
            """).ToListAsync(ct);
        if (locked.Count == 0) throw new HrException("That close no longer exists.");

        return await hr.LeaveYearCloses.FirstOrDefaultAsync(c => c.Id == id, ct)
               ?? throw new HrException("That close no longer exists.");
    }

    private static void Require(LeaveYearClose close, params string[] allowed)
    {
        if (allowed.Contains(close.State)) return;
        throw new HrException(
            $"This close is {LeaveCloseState.Label(close.State).ToLowerInvariant()} — "
            + "that step does not apply.");
    }
}
