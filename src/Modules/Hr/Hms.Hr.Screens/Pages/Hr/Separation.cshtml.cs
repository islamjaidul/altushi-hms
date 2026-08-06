using Hms.Hr.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record ClearanceRow(
    long Id, string Department, string Status, string? Note, long DuesTaka,
    string? SignedOffByName, DateTimeOffset? SignedOffAt, long? OrgUnitId, string? OrgUnitName)
{
    public bool Outstanding => Status is ClearanceStatus.Pending or ClearanceStatus.Blocked;
}

public sealed record RunOption(long Id, string RunNo, DateOnly Period, string State);

/// <summary>
/// Separation, clearance and final settlement — module PRD §9's whole state machine on one page,
/// staged so an operator only ever sees the step they are on (§7 U1: one screen, one job).
/// <para>
/// Everything before this spec was a date and a status, written by a method with no caller. The
/// page's shape follows the states: serve notice → departments sign off → compute → approve →
/// pay through a supplementary run → issue the documents.
/// </para>
/// <para>
/// The statement is <b>salary-bearing</b>: it states what someone is owed. The whole computed
/// section requires <c>hr.salary.read</c> on top of <c>hr.settlement.manage</c> (D6), and the
/// figures are not loaded for a reader who lacks it rather than hidden in the view.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.SettlementManage)]
public class SeparationModel(IHrTx tx, SeparationService separations) : HmsPageModel
{
    public Employee? Person { get; private set; }
    public Separation? Record { get; private set; }
    public IReadOnlyList<ClearanceRow> Clearance { get; private set; } = [];
    public IReadOnlyList<SettlementLine> Lines { get; private set; } = [];
    public IReadOnlyList<RunOption> Runs { get; private set; } = [];
    public IReadOnlyList<IssuedLetter> Letters { get; private set; } = [];
    public ApprovalRequest? Approval { get; private set; }
    public DateOnly Today { get; private set; }

    /// <summary>The employee row of the person signing in, when they have one. Drives unit-head rights.</summary>
    public long? ActorEmployeeId { get; private set; }
    public IReadOnlyList<long> UnitsIHead { get; private set; } = [];

    public bool CanSeeMoney => Can("hr.salary.read");
    public bool CanIssueLetter => Can("hr.document.issue");

    public long EarningsTaka => Lines.Where(l => l.Kind == PayComponentKind.Earning).Sum(l => l.AmountTaka);
    public long DeductionsTaka => Lines.Where(l => l.Kind == PayComponentKind.Deduction).Sum(l => l.AmountTaka);

    /// <summary>
    /// §11 names no permission for a department's own sign-off. Rather than invent one, an item is
    /// signed off by a settlement manager, or by the head of the unit it is routed to — §11 scoping
    /// rule 1's existing shape. Whether each department should hold its own claim is a question for
    /// the PM.
    /// </summary>
    public bool CanSignOff(ClearanceRow item) =>
        Can("hr.settlement.manage")
        || (item.OrgUnitId is { } unit && UnitsIHead.Contains(unit));

    // ---- forms
    [BindProperty] public string? Kind { get; set; }
    [BindProperty] public string? NoticeOn { get; set; }
    [BindProperty] public string? IntendedLastDay { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public string? ClearanceStatusValue { get; set; }
    [BindProperty] public string? ClearanceNote { get; set; }
    [BindProperty] public long DuesTaka { get; set; }
    [BindProperty] public string? LastWorkingDay { get; set; }
    [BindProperty] public string? LineLabel { get; set; }
    [BindProperty] public string? LineKind { get; set; }
    [BindProperty] public long LineAmount { get; set; }
    [BindProperty] public string? LineBasis { get; set; }
    [BindProperty] public long PayRunId { get; set; }
    [BindProperty] public string? RelievingOn { get; set; }
    [BindProperty] public string? ExitNote { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        await LoadAsync(id);
        return Person is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostInitiateAsync(long id)
    {
        if (!FlexibleDate.TryParse(NoticeOn ?? "", out var notice) || notice == default)
            return await BackWith(id, "Enter the date notice was served, for example 01/09/2026.");
        if (!FlexibleDate.TryParse(IntendedLastDay ?? "", out var last) || last == default)
            return await BackWith(id, "Enter the intended last working day.");

        try
        {
            await tx.RunAsync(s => separations.InitiateAsync(
                s.Hr, s.Kernel, BranchId, id, Kind ?? "", notice, last, Reason,
                ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Notice recorded — the clearance checklist is open", "logout");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostSignOffAsync(long id, long itemId)
    {
        await LoadAsync(id);
        var item = Clearance.FirstOrDefault(c => c.Id == itemId);
        if (item is null) return NotFound();
        if (!CanSignOff(item)) return Forbid();

        try
        {
            await tx.RunAsync(s => separations.SignOffClearanceAsync(
                s.Hr, s.Kernel, BranchId, itemId, ClearanceStatusValue ?? "", ClearanceNote,
                DuesTaka, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast($"{ClearanceDepartment.Label(item.Department)} recorded", "task_alt");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostComputeAsync(long id, long separationId)
    {
        if (!CanSeeMoney) return Forbid();

        if (!FlexibleDate.TryParse(LastWorkingDay ?? "", out var last) || last == default)
            return await BackWith(id, "Enter the actual last working day — every figure follows from it.");

        try
        {
            await tx.RunAsync(s => separations.ComputeAsync(
                s.Hr, s.Kernel, BranchId, separationId, last, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Settlement computed — read the notes before sending it for approval", "calculate");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostAddLineAsync(long id, long separationId)
    {
        if (!CanSeeMoney) return Forbid();

        try
        {
            await tx.RunAsync(s => separations.AddManualLineAsync(
                s.Hr, s.Kernel, BranchId, separationId, LineLabel ?? "",
                LineKind ?? PayComponentKind.Earning, LineAmount, LineBasis ?? "",
                ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Line added to the statement", "add");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostSendForApprovalAsync(long id, long separationId)
    {
        if (!CanSeeMoney) return Forbid();

        RaiseResult? result = null;
        try
        {
            result = await tx.RunAsync(s => separations.RaiseApprovalAsync(
                s.Hr, s.Kernel, BranchId, separationId, ActorId, ActorRole));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast(result!.AutoApproved
            ? "Approved under your own threshold"
            : "Sent for approval — Accounts and the MD decide it in the approvals inbox", "how_to_reg");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostApplyApprovalAsync(long id, long separationId)
    {
        if (!CanSeeMoney) return Forbid();

        try
        {
            await tx.RunAsync(s => separations.ApplyApprovalAsync(
                s.Hr, s.Kernel, BranchId, separationId, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Approval applied — the settlement can now be paid", "verified");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostMarkPaidAsync(long id, long separationId)
    {
        if (!CanSeeMoney) return Forbid();

        try
        {
            await tx.RunAsync(s => separations.MarkPaidAsync(
                s.Hr, s.Kernel, BranchId, separationId, PayRunId, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Paid — the employment is now formally ended", "payments");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostIssueDocumentsAsync(long id, long separationId)
    {
        if (!FlexibleDate.TryParse(RelievingOn ?? "", out var relieving) || relieving == default)
            return await BackWith(id, "Enter the relieving date.");

        try
        {
            await tx.RunAsync(s => separations.MarkDocumentsIssuedAsync(
                s.Hr, s.Kernel, BranchId, separationId, relieving, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Separation closed — issue the relieving and experience letters from the file", "task_alt");
        return Redirect($"/hr/employees/{id}/separation");
    }

    public async Task<IActionResult> OnPostCancelAsync(long id, long separationId)
    {
        try
        {
            await tx.RunAsync(s => separations.CancelAsync(
                s.Hr, s.Kernel, BranchId, separationId, Reason ?? "", ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Separation withdrawn", "undo");
        return Redirect($"/hr/employees/{id}");
    }

    private async Task<IActionResult> BackWith(long id, string message)
    {
        await LoadAsync(id);
        Fail(message);
        return Page();
    }

    private async Task LoadAsync(long id)
    {
        Today = Dhaka.Today(TimeProvider.System);

        await tx.RunAsync(async s =>
        {
            Person = await s.Hr.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.BranchId == BranchId);
            if (Person is null) return;

            // Who am I, in employee terms? Only then can "head of that unit" mean anything.
            var userRef = ActorId.ToString();
            ActorEmployeeId = await s.Hr.Employees.AsNoTracking()
                .Where(e => e.BranchId == BranchId && e.UserRef == userRef)
                .Select(e => (long?)e.Id)
                .FirstOrDefaultAsync();

            if (ActorEmployeeId is { } me)
            {
                UnitsIHead = await s.Hr.OrgUnits.AsNoTracking()
                    .Where(u => u.HeadEmployeeId == me)
                    .Select(u => u.Id)
                    .ToListAsync();
            }

            Record = await s.Hr.Separations.AsNoTracking()
                .Where(x => x.EmployeeId == id && x.State != SeparationState.Cancelled)
                .FirstOrDefaultAsync();
            if (Record is null) return;

            var units = await s.Hr.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name);

            Clearance =
            [
                .. (await s.Hr.ClearanceItems.AsNoTracking()
                        .Where(c => c.SeparationId == Record.Id)
                        .ToListAsync())
                    .OrderBy(c => Array.IndexOf(ClearanceDepartment.All, c.Department))
                    .Select(c => new ClearanceRow(
                        c.Id, c.Department, c.Status, c.Note, c.DuesTaka,
                        c.SignedOffByName, c.SignedOffAt, c.OrgUnitId,
                        c.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null)),
            ];

            Letters = await s.Hr.IssuedLetters.AsNoTracking()
                .Where(l => l.EmployeeId == id)
                .OrderByDescending(l => l.IssuedOn).ThenByDescending(l => l.Id)
                .ToListAsync();

            // The statement itself is money. A reader without salary read gets the workflow and
            // the clearance, and no figures are fetched at all.
            if (!CanSeeMoney) return;

            Lines = await s.Hr.SettlementLines.AsNoTracking()
                .Where(l => l.SeparationId == Record.Id)
                .OrderBy(l => l.Ordinal)
                .ToListAsync();

            if (Record.ApprovalRequestId is { } requestId)
            {
                Approval = await s.Kernel.ApprovalRequests.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == requestId);
            }

            if (Record.State == SeparationState.Approved)
            {
                Runs = await s.Hr.PayrollRuns.AsNoTracking()
                    .Where(r => r.BranchId == BranchId && r.Kind == PayrollRunKind.Supplementary
                                && (r.State == PayrollRunState.Locked || r.State == PayrollRunState.Posted))
                    .OrderByDescending(r => r.Period)
                    .Take(24)
                    .Select(r => new RunOption(r.Id, r.RunNo, r.Period, r.State))
                    .ToListAsync();
            }
        });
    }

    /// <summary>The letters this stage of the workflow offers, in the order HR issues them.</summary>
    public static IReadOnlyList<string> ClosingLetters(string kind) => kind switch
    {
        SeparationKind.Termination => [LetterKind.Termination, LetterKind.Relieving, LetterKind.Experience],
        _ => [LetterKind.Relieving, LetterKind.Experience],
    };

    public static string LetterLabel(string kind) => LetterKind.Label(kind);

    /// <summary>Suggested wording, when the employer has not written their own yet.</summary>
    public static string Starter(string kind) => LetterService.Starter(kind);
}
