using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record CloseLineRow(
    string Code, string Name, string LeaveType,
    int ClosingBp, int AccrueBp, int CarryBp, int LapseBp, int EncashBp, string? Basis);

/// <summary>
/// The leave year, closed and reopened (module PRD §7.6 — G33, G34, G35).
/// <para>
/// <c>LeaveBalance</c> has carried opening, accrued, adjustment, availed and encashed columns since
/// the module shipped and nothing has ever rolled them. This is the screen that does — previewable,
/// and committed as one act because §7.6 says "never partially applied".
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.LeaveApprove)]
public class LeaveYearModel(IHrTx tx, LeaveYearService years, IPeriodCalendarSource calendars)
    : HmsPageModel
{
    public IReadOnlyList<LeaveYearClose> Closes { get; private set; } = [];
    public LeaveYearClose? Selected { get; private set; }
    public IReadOnlyList<CloseLineRow> Lines { get; private set; } = [];
    public List<SelectListItem> People { get; private set; } = [];
    public List<SelectListItem> Types { get; private set; } = [];
    public DateOnly SuggestedYear { get; private set; }

    /// <summary>Adjusting a balance by hand is an HR-manager act, not an approver's.</summary>
    public bool CanAdjust => Can("hr.policy.manage");

    [BindProperty] public string? ClosingYear { get; set; }
    [BindProperty] public long AdjustEmployeeId { get; set; }
    [BindProperty] public long AdjustLeaveTypeId { get; set; }
    [BindProperty] public int AdjustYear { get; set; }
    [BindProperty] public decimal AdjustDays { get; set; }
    [BindProperty] public string? AdjustReason { get; set; }

    public async Task OnGetAsync(long? id) => await LoadAsync(id);

    public async Task<IActionResult> OnPostProposeAsync()
    {
        if (!FlexibleDate.TryParse(ClosingYear ?? "", out var year) || year == default)
            return await BackWith(null, "Enter the first day of the leave year being closed.");

        try
        {
            await tx.RunAsync(s => years.ProposeAsync(
                s.Hr, s.Kernel, BranchId, year, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(null, e.Message);
        }

        Toast($"Close proposed for the year starting {year:dd MMM yyyy} — preview it next", "event_repeat");
        return Redirect("/hr/leave-year");
    }

    public async Task<IActionResult> OnPostPreviewAsync(long id)
    {
        try
        {
            await tx.RunAsync(s => years.PreviewAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Previewed — read it before committing; the commit is all or nothing", "preview");
        return Redirect($"/hr/leave-year?id={id}");
    }

    public async Task<IActionResult> OnPostCommitAsync(long id)
    {
        try
        {
            await tx.RunAsync(s => years.CommitAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Committed — the new year is open for everybody on the preview", "task_alt");
        return Redirect($"/hr/leave-year?id={id}");
    }

    public async Task<IActionResult> OnPostAdjustAsync()
    {
        if (!CanAdjust) return Forbid();

        // The form takes days because that is what an HR officer says; the domain keeps basis
        // points, so the conversion happens once, here.
        var deltaBp = (int)Math.Round(AdjustDays * 10_000m);

        try
        {
            await tx.RunAsync(s => years.AdjustAsync(
                s.Hr, s.Kernel, BranchId, AdjustEmployeeId, AdjustLeaveTypeId, AdjustYear,
                deltaBp, AdjustReason ?? "", ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(null, e.Message);
        }

        Toast($"Balance adjusted by {AdjustDays:0.##} day(s)", "edit_note");
        return Redirect("/hr/leave-year");
    }

    private async Task<IActionResult> BackWith(long? id, string message)
    {
        await LoadAsync(id);
        Fail(message);
        return Page();
    }

    private async Task LoadAsync(long? id)
    {
        var today = Dhaka.Today(TimeProvider.System);
        var calendar = await calendars.ForAsync(BranchId, today);

        // The leave year that is closing is the one that started before today's — read from the
        // employer's own configured start month, never assumed (§12.3, D2).
        var thisYearStart = new DateOnly(today.Year, calendar.LeaveYearStartMonth, 1);
        if (thisYearStart > today) thisYearStart = thisYearStart.AddYears(-1);
        SuggestedYear = thisYearStart.AddYears(-1);

        await tx.RunAsync(async s =>
        {
            Closes = await s.Hr.LeaveYearCloses.AsNoTracking()
                .Where(c => c.BranchId == BranchId)
                .OrderByDescending(c => c.ClosingYear)
                .Take(20)
                .ToListAsync();

            Selected = id is { } chosen
                ? Closes.FirstOrDefault(c => c.Id == chosen)
                : Closes.FirstOrDefault();

            if (Selected is not null)
            {
                var types = await s.Hr.LeaveTypes.AsNoTracking()
                    .ToDictionaryAsync(t => t.Id, t => t.Name);

                // The type id travels with its own row and the name is resolved from the loaded
                // dictionary. Matching two differently-ordered queries by index would have been a
                // silent mislabelling the moment either ordering changed.
                var raw = await (
                    from l in s.Hr.LeaveCloseLines.AsNoTracking()
                    join e in s.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                    where l.CloseId == Selected.Id
                    orderby e.EmployeeCode, l.LeaveTypeId
                    select new
                    {
                        e.EmployeeCode, e.FullName, l.LeaveTypeId, l.ClosingBalanceBp,
                        l.AccrueBp, l.CarryBp, l.LapseBp, l.EncashBp, l.Basis,
                    })
                    .Take(2000)
                    .ToListAsync();

                Lines =
                [
                    .. raw.Select(l => new CloseLineRow(
                        l.EmployeeCode, l.FullName, types.GetValueOrDefault(l.LeaveTypeId, "—"),
                        l.ClosingBalanceBp, l.AccrueBp, l.CarryBp, l.LapseBp, l.EncashBp, l.Basis)),
                ];
            }

            People = await s.Hr.Employees.AsNoTracking()
                .Where(e => e.BranchId == BranchId && e.SeparatedOn == null)
                .OrderBy(e => e.EmployeeCode)
                .Select(e => new SelectListItem(e.EmployeeCode + " — " + e.FullName, e.Id.ToString()))
                .ToListAsync();

            Types = await s.Hr.LeaveTypes.AsNoTracking()
                .Where(t => t.BranchId == BranchId && t.Active)
                .OrderBy(t => t.Name)
                .Select(t => new SelectListItem(t.Name, t.Id.ToString()))
                .ToListAsync();
        });

        if (AdjustYear == 0) AdjustYear = today.Year;
    }
}
