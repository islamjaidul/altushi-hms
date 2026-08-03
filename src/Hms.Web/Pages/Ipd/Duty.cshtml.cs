using System.ComponentModel.DataAnnotations;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record DutyCell(string Shift, IReadOnlyList<DutyAssignment> Staff);
public sealed record DutyWard(long Id, string Name, IReadOnlyList<DutyCell> Shifts);

/// <summary>
/// R5 (spec 0041) — M6's `[S]` "nurse / ward-boy / aya duty assignment", which the 2026-08 audit
/// found existed nowhere.
///
/// It lives in IPD rather than HR on purpose. HR owns shifts and employees, but a ward is
/// clinical vocabulary and P27 keeps it out of the <c>hr</c> schema — and the HRM SKU is sold to
/// businesses that have no wards at all. So the employee picker is a convenience over whatever
/// HR rows exist, and a free-typed name is always allowed: an aya is often nobody's HR record,
/// and a roster that only works once HR is populated is a roster the ward cannot use tonight.
/// </summary>
[Authorize(Policy = Perm.IpdDutyManage)]
public class DutyModel(HmsTx tx, IpdService ipd, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true), StringLength(Bounds.Code)] public string? On { get; set; }

    [BindProperty] public long WardId { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? ShiftLabel { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? StaffRole { get; set; }
    [BindProperty] public long? EmployeeId { get; set; }
    [BindProperty, StringLength(Bounds.Name)] public string? StaffName { get; set; }

    [BindProperty] public long AssignmentId { get; set; }
    [BindProperty, StringLength(Bounds.Note)] public string? Reason { get; set; }

    public DateOnly OnDate { get; private set; }
    public IReadOnlyList<DutyWard> Wards { get; private set; } = [];
    public IReadOnlyList<(long Id, string Name)> Staff { get; private set; } = [];
    public IReadOnlyDictionary<long, string> Actors { get; private set; } =
        new Dictionary<long, string>();

    public static readonly string[] Shifts = [DutyShift.Morning, DutyShift.Evening, DutyShift.Night];
    public static readonly string[] Roles = [DutyRole.Nurse, DutyRole.WardBoy, DutyRole.Aya];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        OnDate = !string.IsNullOrWhiteSpace(On) && FlexibleDate.TryParse(On, out var picked)
            ? picked : today;

        var wards = await s.Ipd.Wards.AsNoTracking().Where(w => w.Active)
            .OrderBy(w => w.Id).ToListAsync();
        var duty = await s.Ipd.DutyAssignments.AsNoTracking()
            .Where(d => d.OnDate == OnDate && d.Active)
            .OrderBy(d => d.StaffName).ToListAsync();

        Wards = wards.Select(w => new DutyWard(w.Id, w.Name,
            Shifts.Select(sh => new DutyCell(sh,
                duty.Where(d => d.WardId == w.Id && d.ShiftLabel == sh).ToList())).ToList())).ToList();

        // The HR list is a convenience, not a requirement — the ERP host may carry no employees.
        Staff = (await s.Hr.Employees.AsNoTracking()
                .OrderBy(e => e.FullName).Take(200).ToListAsync())
            .Select(e => (e.Id, e.FullName)).ToList();
        return 0;
    });

    public async Task<IActionResult> OnPostAssignAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                // Never trust the posted name when an employee was picked: resolve it from the
                // row, so the roster cannot record a name that is nobody's (verify-don't-trust).
                var name = StaffName;
                if (EmployeeId is { } id)
                {
                    var employee = await s.Hr.Employees.AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == id)
                        ?? throw new IpdException("That staff member is not on file.");
                    name = employee.FullName;
                }

                return await ipd.AssignDutyAsync(s.Ipd, s.Kernel, BranchId, WardId, ParseOn(),
                    ShiftLabel ?? "", StaffRole ?? "", EmployeeId, name, ActorId, ActorName);
            });
            Toast("Duty assigned", "group");
            return Redirect($"/ipd/duty?on={ParseOn():yyyy-MM-dd}");
        }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }

    public async Task<IActionResult> OnPostEndAsync()
    {
        try
        {
            await tx.RunAsync(async s => await ipd.EndDutyAsync(
                s.Ipd, s.Kernel, BranchId, AssignmentId, Reason, ActorId, ActorName));
            Toast("Duty ended", "task_alt");
            return Redirect($"/ipd/duty?on={ParseOn():yyyy-MM-dd}");
        }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }

    private DateOnly ParseOn()
        => !string.IsNullOrWhiteSpace(On) && FlexibleDate.TryParse(On, out var picked)
            ? picked
            : DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
}
