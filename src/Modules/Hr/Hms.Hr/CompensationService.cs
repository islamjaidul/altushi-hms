using Hms.Hr.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Money;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// Bonus sheets, increment runs and promotions (module PRD §7.7 — G38, G39, G40).
/// <para>
/// Compensation could only ever be changed one person at a time, by hand, on the employee record.
/// An employer with three hundred staff giving everybody a festival bonus had three hundred edits
/// to make and no record that they were one decision.
/// </para>
/// <para>
/// ADR-0027 holds: the bonus basis, the eligibility rule and the increment percentage are all the
/// employer's, entered per sheet or per run. Nothing here knows what a Bangladeshi festival bonus
/// customarily is, and nothing is defaulted.
/// </para>
/// </summary>
public sealed class CompensationService(
    EmployeeService employees, ApprovalEngine approvals, AuditWriter audit,
    NumberSeriesService numbers, FiscalCalendar fiscal, TimeProvider clock)
{
    // ---- bonus (G38) ---------------------------------------------------------------------------

    /// <summary>
    /// Drafts a sheet and computes every line — including the people who do <b>not</b> qualify, each
    /// with a stated reason. An excluded row is the difference between "she was not eligible" and
    /// "she was forgotten", and only one of those is defensible when she asks.
    /// </summary>
    public async Task<BonusSheet> DraftBonusAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, BonusSheet draft,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!BonusKind.All.Contains(draft.Kind))
            throw new HrException("Choose what kind of bonus this is.");
        if (string.IsNullOrWhiteSpace(draft.Title))
            throw new HrException("Give the sheet a name — it is what the register lists.");
        if (draft.BasedOnComponentId is null && draft.FlatAmountTaka <= 0)
            throw new HrException(
                "A bonus is either a percentage of a component or a flat amount. Set one.");
        if (draft.BasedOnComponentId is not null && draft.PercentBp <= 0)
            throw new HrException("Set the percentage of the base component, in basis points.");

        var (_, sheetNo) = await numbers.IssueAsync(
            kernel, branchId, "hr_bonus", fiscal.FiscalYearOf(draft.Period), "BN-{fy}-{n:D4}", ct);

        draft.BranchId = branchId;
        draft.SheetNo = sheetNo;
        draft.State = BonusState.Drafted;
        draft.Period = new DateOnly(draft.Period.Year, draft.Period.Month, 1);
        draft.CreatedAt = clock.GetUtcNow();
        draft.CreatedBy = actorId;
        draft.CreatedByName = actorName;
        hr.BonusSheets.Add(draft);
        await hr.SaveChangesAsync(ct);

        await RecomputeBonusAsync(hr, branchId, draft, ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.bonus.draft", "hr.bonus_sheet",
            draft.Id, after: new { draft.SheetNo, draft.Kind, draft.Period, draft.TotalTaka,
                draft.EligibleCount, draft.ExcludedCount }, tier: 1);

        return draft;
    }

    /// <summary>Rebuilds every line from the sheet's own rules. Idempotent, so it can be re-run.</summary>
    public async Task RecomputeBonusAsync(
        HrDbContext hr, long branchId, BonusSheet sheet, CancellationToken ct = default)
    {
        var existing = await hr.BonusLines.Where(l => l.SheetId == sheet.Id).ToListAsync(ct);
        hr.BonusLines.RemoveRange(existing);

        var asOf = sheet.Period;
        var eligibleTypes = sheet.EligibleTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var people = await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.JoinedOn <= asOf
                        && (e.SeparatedOn == null || e.SeparatedOn >= asOf))
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(ct);

        // One query for the base amounts rather than one per person.
        var baseByEmployee = new Dictionary<long, long>();
        if (sheet.BasedOnComponentId is { } componentId)
        {
            var structures = await hr.PayStructures.AsNoTracking()
                .Where(p => p.BranchId == branchId && p.EffectiveFrom <= asOf
                            && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
                .Select(p => new { p.Id, p.EmployeeId })
                .ToListAsync(ct);

            var ids = structures.Select(x => x.Id).ToList();
            var amounts = await hr.PayStructureComponents.AsNoTracking()
                .Where(c => ids.Contains(c.PayStructureId) && c.ComponentId == componentId)
                .Select(c => new { c.PayStructureId, c.AmountTaka })
                .ToListAsync(ct);

            var byStructure = amounts.ToDictionary(a => a.PayStructureId, a => a.AmountTaka);
            foreach (var s in structures)
                if (byStructure.TryGetValue(s.Id, out var amount))
                    baseByEmployee[s.EmployeeId] = amount;
        }

        long total = 0;
        int eligible = 0, excluded = 0;

        foreach (var person in people)
        {
            var months = SettlementCalculator.CompletedMonths(person.JoinedOn, asOf);
            string? exclusion = null;

            if (months < sheet.MinimumServiceMonths)
            {
                exclusion = $"{months} month{(months == 1 ? "" : "s")} of service; "
                            + $"{sheet.MinimumServiceMonths} required";
            }
            else if (eligibleTypes.Count > 0 && !eligibleTypes.Contains(person.EmploymentType))
            {
                exclusion = $"{EmploymentType.Label(person.EmploymentType)} is not an eligible "
                            + "kind of employment for this bonus";
            }

            long amount = 0;
            string? basis = null;

            if (exclusion is null)
            {
                if (sheet.BasedOnComponentId is not null)
                {
                    var baseAmount = baseByEmployee.GetValueOrDefault(person.Id);
                    if (baseAmount <= 0)
                    {
                        exclusion = "No pay structure in force for this period, so there is no base "
                                    + "to compute the bonus from";
                    }
                    else
                    {
                        amount = Taka.ApplyBp(baseAmount, sheet.PercentBp);
                        basis = $"{sheet.PercentBp / 100m:0.##}% of {baseAmount:N0}";
                    }
                }
                else
                {
                    amount = sheet.FlatAmountTaka;
                    basis = "flat amount";
                }
            }

            hr.BonusLines.Add(new BonusLine
            {
                BranchId = branchId,
                SheetId = sheet.Id,
                EmployeeId = person.Id,
                Eligible = exclusion is null,
                AmountTaka = amount,
                Basis = basis,
                ExclusionReason = exclusion,
            });

            if (exclusion is null) { eligible++; total += amount; } else excluded++;
        }

        sheet.TotalTaka = total;
        sheet.EligibleCount = eligible;
        sheet.ExcludedCount = excluded;
        await hr.SaveChangesAsync(ct);
    }

    public async Task ReviewBonusAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long sheetId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var sheet = await LoadSheetAsync(hr, sheetId, ct);
        RequireBonus(sheet, BonusState.Drafted);

        sheet.State = BonusState.Reviewed;
        audit.Append(kernel, branchId, actorId, actorName, "hr.bonus.review", "hr.bonus_sheet",
            sheetId, after: new { sheet.EligibleCount, sheet.ExcludedCount, sheet.TotalTaka }, tier: 1);
    }

    public async Task<RaiseResult> RaiseBonusApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long sheetId,
        long actorId, string actorRole, CancellationToken ct = default)
    {
        var sheet = await LoadSheetAsync(hr, sheetId, ct);
        RequireBonus(sheet, BonusState.Reviewed);
        if (sheet.ApprovalRequestId is not null)
            throw new HrException("This sheet has already been sent for approval.");

        var result = await approvals.RaiseAsync(
            kernel, branchId, "hr.bonus", "hr.bonus_sheet", sheetId, actorId, actorRole,
            $"{BonusKind.Label(sheet.Kind)} — {sheet.Title}", sheet.TotalTaka, ct);

        sheet.ApprovalRequestId = result.RequestId ?? result.ApprovalId;
        if (result.AutoApproved)
        {
            sheet.State = BonusState.Approved;
            sheet.ApprovedAt = clock.GetUtcNow();
            sheet.ApprovedBy = actorId;
        }
        return result;
    }

    public async Task ApplyBonusApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long sheetId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var sheet = await LoadSheetAsync(hr, sheetId, ct);
        RequireBonus(sheet, BonusState.Reviewed);

        if (sheet.ApprovalRequestId is not { } requestId)
            throw new HrException("This sheet has not been sent for approval yet.");

        var request = await kernel.ApprovalRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new HrException("The approval request could not be found.");
        if (request.State != ApprovalState.Approved)
            throw new HrException($"The approval is {request.State} — nothing is paid until it is approved.");

        sheet.State = BonusState.Approved;
        sheet.ApprovedAt = request.DecidedAt ?? clock.GetUtcNow();
        sheet.ApprovedBy = request.DecidedBy ?? actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.bonus.approve", "hr.bonus_sheet",
            sheetId, after: new { sheet.TotalTaka, sheet.EligibleCount }, tier: 1);
    }

    /// <summary>Records that a run paid it. A bonus is never paid outside payroll.</summary>
    public async Task MarkBonusPaidAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long sheetId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var sheet = await LoadSheetAsync(hr, sheetId, ct);
        RequireBonus(sheet, BonusState.Approved);

        var run = await hr.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new HrException("That payroll run no longer exists.");
        if (run.State is not (PayrollRunState.Locked or PayrollRunState.Posted
            or PayrollRunState.Disbursed))
            throw new HrException($"Run {run.RunNo} is {run.State} — it has not paid anything yet.");

        sheet.State = BonusState.Paid;
        sheet.PaidRunId = runId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.bonus.pay", "hr.bonus_sheet",
            sheetId, after: new { runId, run.RunNo, sheet.TotalTaka }, tier: 1);
    }

    // ---- increment and promotion (G39, G40) --------------------------------------------------------

    /// <summary>
    /// Proposes a run over a cohort, then previews every affected person with old and new. The
    /// preview is <b>stored</b>, so the approver sees exactly what will apply rather than a figure
    /// recomputed from data that may have moved since.
    /// </summary>
    public async Task<CompensationRun> ProposeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, CompensationRun draft,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!CompensationRunKind.All.Contains(draft.Kind))
            throw new HrException("A run is either an increment or a promotion.");
        if (string.IsNullOrWhiteSpace(draft.Title))
            throw new HrException("Give the run a name.");
        if (draft.PercentBp <= 0 && draft.FlatAmountTaka <= 0)
            throw new HrException("Set either a percentage or a flat amount to add.");
        if (draft.Kind == CompensationRunKind.Promotion
            && draft.NewDesignationId is null && draft.NewGradeId is null)
            throw new HrException("A promotion has to move the designation or the grade, or both.");

        var (_, runNo) = await numbers.IssueAsync(
            kernel, branchId, "hr_compensation", fiscal.FiscalYearOf(draft.EffectiveFrom),
            "CR-{fy}-{n:D4}", ct);

        draft.BranchId = branchId;
        draft.RunNo = runNo;
        draft.State = CompensationRunState.Proposed;
        draft.CreatedAt = clock.GetUtcNow();
        draft.CreatedBy = actorId;
        draft.CreatedByName = actorName;
        hr.CompensationRuns.Add(draft);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.compensation.propose",
            "hr.compensation_run", draft.Id,
            after: new { draft.RunNo, draft.Kind, draft.EffectiveFrom, draft.PercentBp,
                draft.FlatAmountTaka }, tier: 1);

        return draft;
    }

    public async Task PreviewAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(hr, runId, ct);
        RequireRun(run, CompensationRunState.Proposed, CompensationRunState.Previewed);

        var existing = await hr.CompensationLines.Where(l => l.RunId == runId).ToListAsync(ct);
        hr.CompensationLines.RemoveRange(existing);

        var asOf = run.EffectiveFrom;

        var assignments = await hr.Assignments.AsNoTracking()
            .Where(a => a.BranchId == branchId && a.EffectiveFrom <= asOf
                        && (a.EffectiveTo == null || a.EffectiveTo >= asOf))
            .Select(a => new { a.EmployeeId, a.OrgUnitId, a.DesignationId, a.GradeId })
            .ToListAsync(ct);
        var byEmployee = assignments.GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var people = await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.SeparatedOn == null && e.JoinedOn <= asOf)
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(ct);

        var earningIds = await hr.PayComponents.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.Kind == PayComponentKind.Earning)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var structures = await hr.PayStructures.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= asOf
                        && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
            .Select(p => new { p.Id, p.EmployeeId })
            .ToListAsync(ct);
        var structureIds = structures.Select(x => x.Id).ToList();

        var componentAmounts = await hr.PayStructureComponents.AsNoTracking()
            .Where(c => structureIds.Contains(c.PayStructureId) && earningIds.Contains(c.ComponentId))
            .Select(c => new { c.PayStructureId, c.AmountTaka })
            .ToListAsync(ct);

        var grossByStructure = componentAmounts.GroupBy(c => c.PayStructureId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.AmountTaka));
        var grossByEmployee = structures
            .Where(s => grossByStructure.ContainsKey(s.Id))
            .ToDictionary(s => s.EmployeeId, s => grossByStructure[s.Id]);

        var affected = 0;
        long increase = 0;

        foreach (var person in people)
        {
            if (!byEmployee.TryGetValue(person.Id, out var a)) continue;
            if (run.OrgUnitId is { } unit && a.OrgUnitId != unit) continue;
            if (run.GradeId is { } grade && a.GradeId != grade) continue;
            if (run.DesignationId is { } designation && a.DesignationId != designation) continue;

            var months = SettlementCalculator.CompletedMonths(person.JoinedOn, asOf);
            var old = grossByEmployee.GetValueOrDefault(person.Id);

            string? skipped = null;
            if (months < run.MinimumServiceMonths)
                skipped = $"{months} months of service; {run.MinimumServiceMonths} required";
            else if (old <= 0)
                skipped = "no pay structure in force on the effective date";

            var rise = skipped is not null ? 0
                : run.PercentBp > 0 ? Taka.ApplyBp(old, run.PercentBp) : run.FlatAmountTaka;

            hr.CompensationLines.Add(new CompensationLine
            {
                BranchId = branchId,
                RunId = runId,
                EmployeeId = person.Id,
                OldGrossTaka = old,
                // The check constraint refuses a zero new pay, so a skipped line keeps the old one.
                NewGrossTaka = skipped is not null ? Math.Max(old, 1) : old + rise,
                OldDesignationId = a.DesignationId,
                NewDesignationId = run.NewDesignationId ?? a.DesignationId,
                OldGradeId = a.GradeId,
                NewGradeId = run.NewGradeId ?? a.GradeId,
                Basis = skipped is not null ? null
                    : run.PercentBp > 0
                        ? $"{run.PercentBp / 100m:0.##}% of {old:N0}"
                        : $"flat {run.FlatAmountTaka:N0}",
                Skipped = skipped,
            });

            if (skipped is null) { affected++; increase += rise; }
        }

        run.AffectedCount = affected;
        run.TotalIncreaseTaka = increase;
        run.State = CompensationRunState.Previewed;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.compensation.preview",
            "hr.compensation_run", runId, after: new { affected, increase }, tier: 1);
    }

    public async Task<RaiseResult> RaiseRunApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorRole, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(hr, runId, ct);
        RequireRun(run, CompensationRunState.Previewed);
        if (run.ApprovalRequestId is not null)
            throw new HrException("This run has already been sent for approval.");
        if (run.AffectedCount == 0)
            throw new HrException("Nobody in the cohort qualifies — there is nothing to approve.");

        var result = await approvals.RaiseAsync(
            kernel, branchId, "hr.compensation", "hr.compensation_run", runId, actorId, actorRole,
            $"{CompensationRunKind.Label(run.Kind)} — {run.Title}", run.TotalIncreaseTaka, ct);

        run.ApprovalRequestId = result.RequestId ?? result.ApprovalId;
        if (result.AutoApproved)
        {
            run.State = CompensationRunState.Approved;
            run.ApprovedAt = clock.GetUtcNow();
            run.ApprovedBy = actorId;
        }
        return result;
    }

    public async Task ApplyRunApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(hr, runId, ct);
        RequireRun(run, CompensationRunState.Previewed);

        if (run.ApprovalRequestId is not { } requestId)
            throw new HrException("This run has not been sent for approval yet.");

        var request = await kernel.ApprovalRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new HrException("The approval request could not be found.");
        if (request.State != ApprovalState.Approved)
            throw new HrException($"The approval is {request.State} — nothing applies until it is approved.");

        run.State = CompensationRunState.Approved;
        run.ApprovedAt = request.DecidedAt ?? clock.GetUtcNow();
        run.ApprovedBy = request.DecidedBy ?? actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.compensation.approve",
            "hr.compensation_run", runId, after: new { run.AffectedCount, run.TotalIncreaseTaka },
            tier: 1);
    }

    /// <summary>
    /// Writes the new pay for everybody on the sheet, effective-dated. A promotion also writes the
    /// assignment, so designation, grade and pay move together on one date — which is what makes it
    /// one action rather than three the operator has to remember to do (G40).
    /// <para>
    /// The raise is applied to the <b>first earning component</b> of each structure rather than
    /// spread across all of them: spreading would silently change every allowance's proportion, and
    /// an employer who prices a percent-of allowance off basic would find it had moved twice.
    /// </para>
    /// </summary>
    public async Task ApplyAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(hr, runId, ct);
        RequireRun(run, CompensationRunState.Approved);

        var lines = await hr.CompensationLines
            .Where(l => l.RunId == runId && l.Skipped == null && !l.Applied)
            .ToListAsync(ct);

        var components = await hr.PayComponents.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.Kind == PayComponentKind.Earning
                        && c.CalcMethod == PayComponentCalc.Fixed)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (var line in lines)
        {
            var structure = await hr.PayStructures.AsNoTracking()
                .Where(p => p.EmployeeId == line.EmployeeId && p.EffectiveFrom <= run.EffectiveFrom
                            && (p.EffectiveTo == null || p.EffectiveTo >= run.EffectiveFrom))
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync(ct);
            if (structure is null) { line.Skipped = "pay structure disappeared before apply"; continue; }

            var current = await hr.PayStructureComponents.AsNoTracking()
                .Where(c => c.PayStructureId == structure.Id)
                .ToListAsync(ct);

            var target = components.FirstOrDefault(id => current.Any(c => c.ComponentId == id));
            if (target == 0) { line.Skipped = "no fixed earning component to raise"; continue; }

            var next = current.Select(c => new EmployeePayComponent
            {
                ComponentId = c.ComponentId,
                AmountTaka = c.ComponentId == target
                    ? c.AmountTaka + (line.NewGrossTaka - line.OldGrossTaka)
                    : c.AmountTaka,
                PercentBpOverride = c.PercentBpOverride,
            }).ToList();

            await employees.SetPayAsync(
                hr, kernel, branchId, line.EmployeeId,
                new EmployeePayStructure
                {
                    EffectiveFrom = run.EffectiveFrom,
                    Reason = run.Kind == CompensationRunKind.Promotion ? "promotion" : "increment",
                },
                next, actorId, actorName, ct);

            if (run.Kind == CompensationRunKind.Promotion)
            {
                var open = await hr.Assignments.AsNoTracking()
                    .Where(a => a.EmployeeId == line.EmployeeId && a.EffectiveTo == null)
                    .OrderByDescending(a => a.EffectiveFrom)
                    .FirstOrDefaultAsync(ct);

                if (open is not null)
                {
                    await employees.AssignAsync(hr, kernel, branchId, line.EmployeeId,
                        new EmployeeAssignment
                        {
                            OrgUnitId = open.OrgUnitId,
                            DesignationId = line.NewDesignationId ?? open.DesignationId,
                            GradeId = line.NewGradeId ?? open.GradeId,
                            WorkLocationId = open.WorkLocationId,
                            ReportsToEmployeeId = open.ReportsToEmployeeId,
                            WeeklyOffPatternId = open.WeeklyOffPatternId,
                            HolidayCalendarId = open.HolidayCalendarId,
                            EffectiveFrom = run.EffectiveFrom,
                            Reason = run.Title,
                        },
                        EmploymentEventKind.Promoted, actorId, actorName, ct);
                }
            }
            else
            {
                hr.EmploymentEvents.Add(new EmploymentEvent
                {
                    BranchId = branchId,
                    EmployeeId = line.EmployeeId,
                    Kind = EmploymentEventKind.Increment,
                    OnDate = run.EffectiveFrom,
                    Note = run.Title,
                    RecordedAt = clock.GetUtcNow(),
                    RecordedBy = actorId,
                    RecordedByName = actorName,
                });
            }

            line.Applied = true;
        }

        run.State = CompensationRunState.Applied;
        run.AppliedAt = clock.GetUtcNow();
        run.AppliedBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.compensation.apply",
            "hr.compensation_run", runId,
            after: new { run.RunNo, applied = lines.Count(l => l.Applied) }, tier: 1);
    }

    // ---- loading -----------------------------------------------------------------------------------

    private static async Task<BonusSheet> LoadSheetAsync(HrDbContext hr, long id, CancellationToken ct)
    {
        var locked = await hr.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM hr.bonus_sheet WHERE id = {id} FOR UPDATE
            """).ToListAsync(ct);
        if (locked.Count == 0) throw new HrException("That bonus sheet no longer exists.");
        return await hr.BonusSheets.FirstOrDefaultAsync(s => s.Id == id, ct)
               ?? throw new HrException("That bonus sheet no longer exists.");
    }

    private static async Task<CompensationRun> LoadRunAsync(HrDbContext hr, long id, CancellationToken ct)
    {
        var locked = await hr.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM hr.compensation_run WHERE id = {id} FOR UPDATE
            """).ToListAsync(ct);
        if (locked.Count == 0) throw new HrException("That run no longer exists.");
        return await hr.CompensationRuns.FirstOrDefaultAsync(r => r.Id == id, ct)
               ?? throw new HrException("That run no longer exists.");
    }

    private static void RequireBonus(BonusSheet sheet, params string[] allowed)
    {
        if (allowed.Contains(sheet.State)) return;
        throw new HrException(
            $"{sheet.SheetNo} is {BonusState.Label(sheet.State).ToLowerInvariant()} — "
            + "that step does not apply.");
    }

    private static void RequireRun(CompensationRun run, params string[] allowed)
    {
        if (allowed.Contains(run.State)) return;
        throw new HrException(
            $"{run.RunNo} is {CompensationRunState.Label(run.State).ToLowerInvariant()} — "
            + "that step does not apply.");
    }
}
