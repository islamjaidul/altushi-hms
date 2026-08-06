using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record RequestRow(
    long Id, string Kind, long EmployeeId, string Code, string Name,
    DateOnly OnDate, string What, string? Reason, string State, string RaisedByName);

/// <summary>
/// The decision desk for everything an employee asks about their own time (module PRD §7.4, §7.5 —
/// G23, G24, G25, G28, G29).
/// <para>
/// One screen rather than five: a manager clearing yesterday's exceptions has one queue to work
/// through, and the four request kinds share a state machine precisely so they can share a surface.
/// </para>
/// <para>
/// §11 names <c>hr.request.decide</c> for exactly this — regularization, OT, comp-off, swap and
/// profile-change decisions.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.RequestDecide)]
public class TimeDeskModel(IHrTx tx, TimeRequestService requests) : HmsPageModel
{
    public IReadOnlyList<RequestRow> Pending { get; private set; } = [];
    public IReadOnlyList<RequestRow> Decided { get; private set; } = [];
    public List<SelectListItem> People { get; private set; } = [];
    public int BankedMinutes { get; private set; }
    public DateOnly Today { get; private set; }

    /// <summary>Raising on somebody's behalf is an HR act; deciding is a manager's.</summary>
    public bool CanRaise => Can("hr.attendance.review");

    [BindProperty] public long EmployeeId { get; set; }
    [BindProperty] public string? OnDate { get; set; }
    [BindProperty] public string? RequestedStatus { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public int Minutes { get; set; }
    [BindProperty] public string OvertimeKind { get; set; } = OvertimeApprovalKind.Post;
    [BindProperty] public string ShortLeaveKindValue { get; set; } = ShortLeaveKind.ShortLeave;
    [BindProperty] public string? FromTime { get; set; }
    [BindProperty] public string? ToTime { get; set; }
    [BindProperty] public string? Note { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    // ---- raising -----------------------------------------------------------------------------

    public Task<IActionResult> OnPostRaiseRegularizationAsync() => ActAsync(async s =>
    {
        var on = ParseDate(OnDate);
        await requests.RaiseRegularizationAsync(
            s.Hr, s.Kernel, BranchId, EmployeeId, on, RequestedStatus ?? AttendanceStatus.Present,
            Reason ?? "", ActorId, ActorName);
        return $"Correction requested for {on:dd MMM yyyy}";
    });

    public Task<IActionResult> OnPostRaiseOvertimeAsync() => ActAsync(async s =>
    {
        var on = ParseDate(OnDate);
        await requests.RaiseOvertimeAsync(
            s.Hr, s.Kernel, BranchId, EmployeeId, on, Minutes, OvertimeKind, Reason,
            ActorId, ActorName);
        return $"{Minutes} minute(s) of overtime raised for {on:dd MMM yyyy}";
    });

    public Task<IActionResult> OnPostRaiseCompOffAsync() => ActAsync(async s =>
    {
        var on = ParseDate(OnDate);
        await requests.RaiseCompOffAsync(
            s.Hr, s.Kernel, BranchId, EmployeeId, on, Minutes, Reason, ActorId, ActorName);
        return $"Comp-off requested for {on:dd MMM yyyy}";
    });

    public Task<IActionResult> OnPostRaiseShortLeaveAsync() => ActAsync(async s =>
    {
        var on = ParseDate(OnDate);
        if (!TimeOnly.TryParse(FromTime ?? "", out var from) || !TimeOnly.TryParse(ToTime ?? "", out var to))
            throw new HrException("Enter the times as HH:mm, for example 14:00 and 16:30.");

        await requests.RaiseShortLeaveAsync(
            s.Hr, s.Kernel, BranchId, EmployeeId, on, ShortLeaveKindValue, from, to,
            Reason ?? "", ActorId, ActorName);
        return $"{ShortLeaveKind.Label(ShortLeaveKindValue)} requested for {on:dd MMM yyyy}";
    });

    // ---- deciding ------------------------------------------------------------------------------

    public Task<IActionResult> OnPostDecideAsync(string kind, long id, bool approve) => ActAsync(async s =>
    {
        switch (kind)
        {
            case "regularization":
                await requests.DecideRegularizationAsync(
                    s.Hr, s.Kernel, BranchId, id, approve, Note, ActorId, ActorName);
                break;
            case "overtime":
                await requests.DecideOvertimeAsync(
                    s.Hr, s.Kernel, BranchId, id, approve, Note, ActorId, ActorName);
                break;
            case "compoff":
                await requests.DecideCompOffAsync(
                    s.Hr, s.Kernel, BranchId, id, approve, Note, ActorId, ActorName);
                break;
            case "shortleave":
                await requests.DecideShortLeaveAsync(
                    s.Hr, s.Kernel, BranchId, id, approve, Note, ActorId, ActorName);
                break;
            case "swap":
                await requests.DecideSwapAsync(
                    s.Hr, s.Kernel, BranchId, id, approve, Note, ActorId, ActorName);
                break;
            default:
                throw new HrException("That is not a request this desk decides.");
        }

        return approve ? "Approved" : "Rejected";
    });

    private static DateOnly ParseDate(string? raw)
        => FlexibleDate.TryParse(raw ?? "", out var d) && d != default
            ? d
            : throw new HrException("Enter the date, for example 01/09/2026.");

    private async Task<IActionResult> ActAsync(Func<HrScope, Task<string>> body)
    {
        try
        {
            var message = await tx.RunAsync(body);
            Toast(message, "schedule");
            return Redirect("/hr/time-desk");
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
        Today = Dhaka.Today(TimeProvider.System);

        await tx.RunAsync(async s =>
        {
            var rows = new List<RequestRow>();

            rows.AddRange(await (
                from r in s.Hr.RegularizationRequests.AsNoTracking()
                join e in s.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
                orderby r.RaisedAt descending
                select new RequestRow(
                    r.Id, "regularization", e.Id, e.EmployeeCode, e.FullName, r.OnDate,
                    "Mark as " + r.RequestedStatus, r.Reason, r.State, r.RaisedByName))
                .Take(200).ToListAsync());

            rows.AddRange(await (
                from r in s.Hr.OvertimeRequests.AsNoTracking()
                join e in s.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
                orderby r.RaisedAt descending
                select new RequestRow(
                    r.Id, "overtime", e.Id, e.EmployeeCode, e.FullName, r.OnDate,
                    r.Minutes + " minutes", r.Reason, r.State, r.RaisedByName))
                .Take(200).ToListAsync());

            rows.AddRange(await (
                from r in s.Hr.CompOffRequests.AsNoTracking()
                join e in s.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
                orderby r.RaisedAt descending
                select new RequestRow(
                    r.Id, "compoff", e.Id, e.EmployeeCode, e.FullName, r.OnDate,
                    r.Minutes + " minutes off", r.Reason, r.State, r.RaisedByName))
                .Take(200).ToListAsync());

            rows.AddRange(await (
                from r in s.Hr.ShortLeaveRequests.AsNoTracking()
                join e in s.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
                orderby r.RaisedAt descending
                select new RequestRow(
                    r.Id, "shortleave", e.Id, e.EmployeeCode, e.FullName, r.OnDate,
                    r.Kind + " " + r.FromTime + "–" + r.ToTime, r.Reason, r.State, r.RaisedByName))
                .Take(200).ToListAsync());

            rows.AddRange(await (
                from r in s.Hr.ShiftSwapRequests.AsNoTracking()
                join e in s.Hr.Employees.AsNoTracking() on r.RequesterEmployeeId equals e.Id
                orderby r.RaisedAt descending
                select new RequestRow(
                    r.Id, "swap", e.Id, e.EmployeeCode, e.FullName, r.RequesterDate,
                    "swap with #" + r.CounterpartEmployeeId, r.Reason, r.State, r.RaisedByName))
                .Take(200).ToListAsync());

            Pending = [.. rows
                .Where(r => r.State is RequestState.Raised or RequestState.Recommended)
                .OrderBy(r => r.OnDate)];
            Decided = [.. rows
                .Where(r => r.State is not (RequestState.Raised or RequestState.Recommended))
                .OrderByDescending(r => r.OnDate)
                .Take(50)];

            People = await s.Hr.Employees.AsNoTracking()
                .Where(e => e.BranchId == BranchId && e.SeparatedOn == null)
                .OrderBy(e => e.EmployeeCode)
                .Select(e => new SelectListItem(e.EmployeeCode + " — " + e.FullName, e.Id.ToString()))
                .ToListAsync();

            if (EmployeeId > 0)
                BankedMinutes = await TimeRequestService.BankBalanceAsync(s.Hr, EmployeeId);
        });
    }
}
