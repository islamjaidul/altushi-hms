using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record PayRunRow(long Id, string RunNo, DateOnly Period, string State);

/// <summary>
/// Bonus sheets and increment/promotion runs (module PRD §7.7 — G38, G39, G40).
/// <para>
/// One workspace rather than two screens: both are "change a lot of people's money at once", both
/// go through the same propose → review → approve → apply shape, and an HR manager doing a festival
/// bonus in the same week as an annual increment should not have to learn two of them.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.CompensationManage)]
public class CompensationModel(IHrTx tx, CompensationService compensation) : HmsPageModel
{
    public IReadOnlyList<BonusSheet> Sheets { get; private set; } = [];
    public IReadOnlyList<CompensationRun> Runs { get; private set; } = [];
    public IReadOnlyList<PayRunRow> PayRuns { get; private set; } = [];
    public List<SelectListItem> EarningComponents { get; private set; } = [];
    public List<SelectListItem> Units { get; private set; } = [];
    public List<SelectListItem> Grades { get; private set; } = [];
    public List<SelectListItem> Designations { get; private set; } = [];

    public bool CanSeeMoney => Can("hr.salary.read");

    // ---- bonus form
    [BindProperty] public string BonusKindValue { get; set; } = Data.BonusKind.Festival;
    [BindProperty] public string? BonusTitle { get; set; }
    [BindProperty] public string? BonusPeriod { get; set; }
    [BindProperty] public long? BasedOnComponentId { get; set; }
    [BindProperty] public long BonusPercent { get; set; } = 100;
    [BindProperty] public long FlatAmountTaka { get; set; }
    [BindProperty] public int BonusMinimumServiceMonths { get; set; }
    [BindProperty] public List<string> BonusEligibleTypes { get; set; } = [];

    // ---- compensation run form
    [BindProperty] public string RunKind { get; set; } = CompensationRunKind.Increment;
    [BindProperty] public string? RunTitle { get; set; }
    [BindProperty] public string? EffectiveFrom { get; set; }
    [BindProperty] public long? OrgUnitId { get; set; }
    [BindProperty] public long? GradeId { get; set; }
    [BindProperty] public long? DesignationId { get; set; }
    [BindProperty] public int RunMinimumServiceMonths { get; set; }
    [BindProperty] public long RunPercent { get; set; }
    [BindProperty] public long RunFlatTaka { get; set; }
    [BindProperty] public long? NewDesignationId { get; set; }
    [BindProperty] public long? NewGradeId { get; set; }
    [BindProperty] public long PayRunId { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    // ---- bonus -----------------------------------------------------------------------------------

    public Task<IActionResult> OnPostDraftBonusAsync() => ActAsync(async s =>
    {
        var period = FlexibleDate.TryParse(BonusPeriod ?? "", out var p) && p != default
            ? p : Dhaka.Today(TimeProvider.System);

        var sheet = await compensation.DraftBonusAsync(s.Hr, s.Kernel, BranchId, new BonusSheet
        {
            Kind = BonusKindValue,
            Title = BonusTitle ?? "",
            Period = period,
            BasedOnComponentId = BasedOnComponentId,
            // The form takes whole percent; the domain keeps basis points, so the conversion
            // happens once, here, rather than in an operator's head.
            PercentBp = BonusPercent * 100,
            FlatAmountTaka = FlatAmountTaka,
            MinimumServiceMonths = BonusMinimumServiceMonths,
            EligibleTypes = string.Join(',', BonusEligibleTypes),
            SheetNo = "",
            CreatedByName = ActorName,
        }, ActorId, ActorName);

        return $"{sheet.SheetNo} drafted — {sheet.EligibleCount} eligible, "
               + $"{sheet.ExcludedCount} excluded, {Ui.Money(sheet.TotalTaka)}";
    });

    public Task<IActionResult> OnPostReviewBonusAsync(long id) => ActAsync(async s =>
    {
        await compensation.ReviewBonusAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return "Eligibility reviewed";
    });

    public Task<IActionResult> OnPostSendBonusAsync(long id) => ActAsync(async s =>
    {
        var raise = await compensation.RaiseBonusApprovalAsync(
            s.Hr, s.Kernel, BranchId, id, ActorId, ActorRole);
        return raise.AutoApproved ? "Approved under the standing threshold" : "Sent for approval";
    });

    public Task<IActionResult> OnPostApplyBonusApprovalAsync(long id) => ActAsync(async s =>
    {
        await compensation.ApplyBonusApprovalAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return "Approval applied";
    });

    public Task<IActionResult> OnPostPayBonusAsync(long id) => ActAsync(async s =>
    {
        await compensation.MarkBonusPaidAsync(
            s.Hr, s.Kernel, BranchId, id, PayRunId, ActorId, ActorName);
        return "Recorded as paid through the run";
    });

    // ---- increment / promotion ---------------------------------------------------------------------

    public Task<IActionResult> OnPostProposeRunAsync() => ActAsync(async s =>
    {
        if (!FlexibleDate.TryParse(EffectiveFrom ?? "", out var from) || from == default)
            throw new HrException("Enter the date the change takes effect.");

        var run = await compensation.ProposeAsync(s.Hr, s.Kernel, BranchId, new CompensationRun
        {
            Kind = RunKind,
            Title = RunTitle ?? "",
            EffectiveFrom = from,
            OrgUnitId = OrgUnitId,
            GradeId = GradeId,
            DesignationId = DesignationId,
            MinimumServiceMonths = RunMinimumServiceMonths,
            PercentBp = RunPercent * 100,
            FlatAmountTaka = RunFlatTaka,
            NewDesignationId = RunKind == CompensationRunKind.Promotion ? NewDesignationId : null,
            NewGradeId = RunKind == CompensationRunKind.Promotion ? NewGradeId : null,
            RunNo = "",
            CreatedByName = ActorName,
        }, ActorId, ActorName);

        return $"{run.RunNo} proposed — preview it to see who it touches";
    });

    public Task<IActionResult> OnPostPreviewRunAsync(long id) => ActAsync(async s =>
    {
        await compensation.PreviewAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return "Previewed — every affected person now has an old and a new figure";
    });

    public Task<IActionResult> OnPostSendRunAsync(long id) => ActAsync(async s =>
    {
        var raise = await compensation.RaiseRunApprovalAsync(
            s.Hr, s.Kernel, BranchId, id, ActorId, ActorRole);
        return raise.AutoApproved ? "Approved under the standing threshold" : "Sent for approval";
    });

    public Task<IActionResult> OnPostApplyRunApprovalAsync(long id) => ActAsync(async s =>
    {
        await compensation.ApplyRunApprovalAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return "Approval applied — the run can be applied";
    });

    public Task<IActionResult> OnPostApplyRunAsync(long id) => ActAsync(async s =>
    {
        await compensation.ApplyAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return "Applied, effective-dated. Historical payslips keep their historical numbers.";
    });

    private async Task<IActionResult> ActAsync(Func<HrScope, Task<string>> body)
    {
        try
        {
            var message = await tx.RunAsync(body);
            Toast(message, "trending_up");
            return Redirect("/hr/compensation");
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            Sheets = await s.Hr.BonusSheets.AsNoTracking()
                .Where(x => x.BranchId == BranchId)
                .OrderByDescending(x => x.Period).ThenByDescending(x => x.Id)
                .Take(50)
                .ToListAsync();

            Runs = await s.Hr.CompensationRuns.AsNoTracking()
                .Where(x => x.BranchId == BranchId)
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
                .Take(50)
                .ToListAsync();

            PayRuns =
            [
                .. await s.Hr.PayrollRuns.AsNoTracking()
                    .Where(r => r.BranchId == BranchId
                                && (r.State == PayrollRunState.Locked
                                    || r.State == PayrollRunState.Posted
                                    || r.State == PayrollRunState.Disbursed))
                    .OrderByDescending(r => r.Period)
                    .Take(24)
                    .Select(r => new PayRunRow(r.Id, r.RunNo, r.Period, r.State))
                    .ToListAsync(),
            ];

            EarningComponents = await s.Hr.PayComponents.AsNoTracking()
                .Where(c => c.BranchId == BranchId && c.Active && c.Kind == PayComponentKind.Earning)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
                .ToListAsync();

            Units = await s.Hr.OrgUnits.AsNoTracking()
                .Where(u => u.BranchId == BranchId && u.Active).OrderBy(u => u.Name)
                .Select(u => new SelectListItem(u.Name, u.Id.ToString())).ToListAsync();
            Grades = await s.Hr.Grades.AsNoTracking()
                .Where(g => g.BranchId == BranchId && g.Active).OrderBy(g => g.Rank)
                .Select(g => new SelectListItem(g.Name, g.Id.ToString())).ToListAsync();
            Designations = await s.Hr.Designations.AsNoTracking()
                .Where(d => d.BranchId == BranchId && d.Active).OrderBy(d => d.Name)
                .Select(d => new SelectListItem(d.Name, d.Id.ToString())).ToListAsync();
        });
    }
}
