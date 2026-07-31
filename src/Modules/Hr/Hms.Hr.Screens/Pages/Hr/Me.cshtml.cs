using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Ui.Pages.Hr;

public sealed record MyLeaveRow(string No, string Type, DateOnly From, DateOnly To, int DaysBp, string State);
public sealed record MyBalanceRow(string Type, int AvailableBp);

/// <summary>
/// Self-service (§5 M16 [S], and the <c>U (own leave)</c> cells §12 gives Nurse, Technologist and
/// Pharmacist). This is the one HR screen an ordinary employee reaches, so it shows their own record
/// and nothing else.
/// <para>
/// The identity link is <c>AppUser.EmployeeRef</c> — the column reserved for exactly this since the
/// MVP and unwritten until now. Every query below is scoped by it: there is no employee id in the
/// URL, so there is nothing to tamper with.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.LeaveApply)]
public class MeModel(IHrTx tx, LeaveService leave) : HmsPageModel
{
    [BindProperty] public long LeaveTypeId { get; set; }
    [BindProperty] public string FromDate { get; set; } = "";
    [BindProperty] public string ToDate { get; set; } = "";
    [BindProperty] public bool HalfDay { get; set; }
    [BindProperty] public string Reason { get; set; } = "";

    public Employee? Me { get; private set; }
    public IReadOnlyList<MyLeaveRow> Applications { get; private set; } = [];
    public IReadOnlyList<MyBalanceRow> Balances { get; private set; } = [];
    public List<SelectListItem> LeaveTypes { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostApplyAsync()
    {
        await LoadAsync();

        if (Me is null)
        {
            Fail("Your login is not linked to an employee record yet — ask HR to link it.");
            return Page();
        }
        if (!FlexibleDate.TryParse(FromDate, out var from) || !FlexibleDate.TryParse(ToDate, out var to))
        {
            Fail("Enter both dates as dd/mm/yyyy.");
            return Page();
        }

        var days = to.DayNumber - from.DayNumber + 1;
        var daysBp = HalfDay && days == 1 ? 5000 : days * 10000;

        try
        {
            await tx.RunAsync(async s => await leave.ApplyAsync(
                s.Hr, s.Kernel, BranchId, Me.Id, LeaveTypeId, from, to, daysBp, Reason,
                ActorId, ActorName));
            Toast("Applied — your department head will see it", "event_available");
            return Redirect("/hr/me");
        }
        catch (HrException e)
        {
            Fail(e.Message);
            return Page();
        }
    }

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        var userRef = ActorId.ToString();
        Me = await s.Hr.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.BranchId == BranchId && e.UserRef == userRef);

        LeaveTypes = await s.Hr.LeaveTypes.AsNoTracking()
            .Where(t => t.BranchId == BranchId && t.Active).OrderBy(t => t.Name)
            .Select(t => new SelectListItem(t.Name, t.Id.ToString()))
            .ToListAsync();

        if (Me is null) return;

        var types = await s.Hr.LeaveTypes.AsNoTracking()
            .Where(t => t.BranchId == BranchId).ToDictionaryAsync(t => t.Id, t => t.Name);

        Applications = await s.Hr.LeaveApplications.AsNoTracking()
            .Where(l => l.EmployeeId == Me.Id)
            .OrderByDescending(l => l.AppliedAt).Take(50)
            .Select(l => new MyLeaveRow(
                l.ApplicationNo, types.GetValueOrDefault(l.LeaveTypeId, "—"),
                l.FromDate, l.ToDate, l.DaysBp, l.State))
            .ToListAsync();

        var year = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6)).Year;
        var balances = await s.Hr.LeaveBalances.AsNoTracking()
            .Where(b => b.EmployeeId == Me.Id && b.LeaveYear == year)
            .ToListAsync();

        Balances = balances
            .Select(b => new MyBalanceRow(types.GetValueOrDefault(b.LeaveTypeId, "—"), b.AvailableBp))
            .ToList();
    });
}
