using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>One row of whichever master is on screen, flattened so the view has a single shape.</summary>
public sealed record MasterRow(long Id, string Code, string Name, string Detail, bool Active);

public sealed record MasterTab(string Key, string Title, string Icon, string Blurb);

/// <summary>
/// Create, rename and retire the org structure a payroll run depends on.
/// <para>
/// This screen is the answer to a complaint that read as five missing features. <c>/hr/policies</c>
/// listed Units, Designations, Grades, Shifts, Leave types and Pay components, each showing
/// <c>none</c> and four of them linking nowhere. It was a checklist of things the operator was told
/// to configure, with no screen on which to configure them — so payroll could not run, and the
/// product correctly reported a state it gave no way to leave.
/// </para>
/// <para>
/// There is no delete. An <c>employee_assignment</c> written in March references the unit it was
/// written against forever; removing the row would make a historical salary sheet unreadable, which
/// is hard rule 4's reasoning applied to masters rather than to money. Retiring keeps the name and
/// takes it out of the pickers.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.PolicyManage)]
public class MastersModel(IHrTx tx) : HmsPageModel
{
    public static readonly IReadOnlyList<MasterTab> Tabs =
    [
        new("units", "Units", "factory", "Departments or sections. Every employee is placed in one."),
        new("designations", "Designations", "badge", "Job titles. Two designations can share a grade."),
        new("grades", "Grades", "bar_chart", "Pay bands. Pay scales hang off these, effective-dated."),
        new("shifts", "Shifts", "schedule", "Working windows. A night shift pairs punches across midnight."),
        new("leave-types", "Leave types", "event_busy", "Whatever this employer offers — we ship none."),
        new("components", "Pay components", "payments", "The lines on a payslip: basic, allowances, deductions."),
    ];

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "units";

    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string? Name { get; set; }

    // Shift
    [BindProperty] public string? StartsAt { get; set; }
    [BindProperty] public string? EndsAt { get; set; }
    [BindProperty] public int BreakMinutes { get; set; }

    // Grade
    [BindProperty] public int Rank { get; set; }

    // Leave type
    [BindProperty] public string Accrual { get; set; } = LeaveAccrual.AnnualGrant;
    [BindProperty] public bool Paid { get; set; } = true;

    // Pay component
    [BindProperty] public string Kind { get; set; } = PayComponentKind.Earning;
    [BindProperty] public string CalcMethod { get; set; } = PayComponentCalc.Fixed;
    [BindProperty] public long PercentBp { get; set; }
    [BindProperty] public long? BasedOnComponentId { get; set; }
    [BindProperty] public bool Taxable { get; set; } = true;

    public IReadOnlyList<MasterRow> Rows { get; private set; } = [];
    public IReadOnlyList<(long Id, string Name)> EarningComponents { get; private set; } = [];
    public MasterTab Current => Tabs.FirstOrDefault(t => t.Key == Tab) ?? Tabs[0];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        { await LoadAsync(); Fail("Give it a name."); return Page(); }

        var name = Name.Trim();
        // A code the operator did not type is better than a required field they must invent: these
        // are internal keys, and §7 is explicit about typing cost.
        var code = string.IsNullOrWhiteSpace(Code) ? Slug(name) : Code.Trim().ToUpperInvariant();

        try
        {
            await tx.RunAsync(async s =>
            {
                switch (Current.Key)
                {
                    case "units":
                        RejectDuplicate(await s.Hr.OrgUnits.AnyAsync(x => x.BranchId == BranchId && x.Code == code), code);
                        s.Hr.OrgUnits.Add(new OrgUnit { BranchId = BranchId, Code = code, Name = name });
                        break;

                    case "designations":
                        RejectDuplicate(await s.Hr.Designations.AnyAsync(x => x.BranchId == BranchId && x.Code == code), code);
                        s.Hr.Designations.Add(new Designation { BranchId = BranchId, Code = code, Name = name });
                        break;

                    case "grades":
                        RejectDuplicate(await s.Hr.Grades.AnyAsync(x => x.BranchId == BranchId && x.Code == code), code);
                        s.Hr.Grades.Add(new Grade { BranchId = BranchId, Code = code, Name = name, Rank = Rank });
                        break;

                    case "shifts":
                    {
                        if (!TimeOnly.TryParse(StartsAt, out var from) || !TimeOnly.TryParse(EndsAt, out var to))
                            throw new HrException("Enter both a start and an end time, as HH:MM.");

                        RejectDuplicate(await s.Hr.Shifts.AnyAsync(x => x.BranchId == BranchId && x.Code == code), code);

                        // An end at or before the start is how a night shift is written — 20:00 to
                        // 04:00 — not an error. The flag is what makes punch pairing cross midnight.
                        var overnight = to <= from;
                        var span = overnight
                            ? (TimeOnly.MaxValue - from) + to.ToTimeSpan() + TimeSpan.FromTicks(1)
                            : to - from;

                        s.Hr.Shifts.Add(new Shift
                        {
                            BranchId = BranchId, Code = code, Name = name,
                            StartsAt = from, EndsAt = to, EndsNextDay = overnight,
                            BreakMinutes = BreakMinutes,
                            StandardMinutes = (int)span.TotalMinutes - BreakMinutes,
                        });
                        break;
                    }

                    case "leave-types":
                        RejectDuplicate(await s.Hr.LeaveTypes.AnyAsync(x => x.BranchId == BranchId && x.Code == code), code);
                        s.Hr.LeaveTypes.Add(new LeaveType
                        {
                            BranchId = BranchId, Code = code, Name = name,
                            Accrual = Accrual, Paid = Paid,
                        });
                        break;

                    case "components":
                    {
                        RejectDuplicate(await s.Hr.PayComponents.AnyAsync(x => x.BranchId == BranchId && x.Code == code), code);

                        if (CalcMethod == PayComponentCalc.PercentOf)
                        {
                            if (BasedOnComponentId is null)
                                throw new HrException("A percentage component needs the component it is a percentage of.");
                            if (PercentBp is <= 0 or > 100_00)
                                throw new HrException("The percentage must be between 0 and 100.");
                        }

                        var order = await s.Hr.PayComponents
                            .Where(x => x.BranchId == BranchId)
                            .Select(x => (int?)x.DisplayOrder).MaxAsync() ?? 0;

                        s.Hr.PayComponents.Add(new PayComponent
                        {
                            BranchId = BranchId, Code = code, Name = name,
                            Kind = Kind, CalcMethod = CalcMethod,
                            BasedOnComponentId = CalcMethod == PayComponentCalc.PercentOf ? BasedOnComponentId : null,
                            PercentBp = CalcMethod == PayComponentCalc.PercentOf ? PercentBp : 0,
                            Taxable = Taxable,
                            DisplayOrder = order + 10,
                        });
                        break;
                    }

                    default:
                        throw new HrException("Unknown master.");
                }

                await s.Hr.SaveChangesAsync();
            });
        }
        catch (HrException ex)
        {
            await LoadAsync();
            Fail(ex.Message);
            return Page();
        }

        Toast($"{name} added", "add");
        return Redirect($"/hr/masters?tab={Current.Key}");
    }

    public async Task<IActionResult> OnPostRenameAsync(long id, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        { await LoadAsync(); Fail("A name cannot be blank."); return Page(); }

        var name = newName.Trim();
        var found = await tx.RunAsync(async s =>
        {
            var row = await FindAsync(s, id);
            if (row is null) return false;
            row.Name = name;
            await s.Hr.SaveChangesAsync();
            return true;
        });

        if (!found)
        { await LoadAsync(); Fail(Gone); return Page(); }

        Toast("Renamed", "edit_note");
        return Redirect($"/hr/masters?tab={Current.Key}");
    }

    /// <summary>
    /// Retire or bring back. Never a delete: an assignment or a payslip line written against this row
    /// has to stay readable, and a retired master is exactly how you stop new records using it
    /// without rewriting the old ones.
    /// </summary>
    public async Task<IActionResult> OnPostToggleAsync(long id)
    {
        var found = await tx.RunAsync(async s =>
        {
            var row = await FindAsync(s, id);
            if (row is null) return false;
            row.Active = !row.Active;
            await s.Hr.SaveChangesAsync();
            return true;
        });

        if (!found)
        { await LoadAsync(); Fail(Gone); return Page(); }

        Toast("Updated", "task_alt");
        return Redirect($"/hr/masters?tab={Current.Key}");
    }

    private const string Gone =
        "That row is not on this tab any more — the screen was reloaded, so try again.";

    /// <summary>
    /// The row this tab's id refers to, or null.
    /// <para>
    /// Was <c>SingleAsync(x =&gt; x.Id == id)</c> per tab, which threw — an unhandled 500 — whenever
    /// the id was not on the current tab. Two browser windows on different tabs is enough to
    /// produce it, and the operator sees a stack trace (spec 0037). Scoped by branch as well: an id
    /// is not an authorisation.
    /// </para>
    /// </summary>
    private async Task<IMasterRow?> FindAsync(HrScope s, long id) => Current.Key switch
    {
        "units" => await s.Hr.OrgUnits.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == BranchId),
        "designations" => await s.Hr.Designations.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == BranchId),
        "grades" => await s.Hr.Grades.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == BranchId),
        "shifts" => await s.Hr.Shifts.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == BranchId),
        "leave-types" => await s.Hr.LeaveTypes.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == BranchId),
        "components" => await s.Hr.PayComponents.FirstOrDefaultAsync(x => x.Id == id && x.BranchId == BranchId),
        _ => null,
    };

    private static void RejectDuplicate(bool clash, string code)
    {
        if (clash) throw new HrException($"Something here already uses the code {code}.");
    }

    private static string Slug(string name)
    {
        var letters = new string([.. name.ToUpperInvariant().Where(char.IsLetterOrDigit)]);
        return letters.Length == 0 ? "X" : letters[..Math.Min(12, letters.Length)];
    }

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        EarningComponents = await s.Hr.PayComponents
            .Where(x => x.BranchId == BranchId && x.Active && x.Kind == PayComponentKind.Earning)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new ValueTuple<long, string>(x.Id, x.Name))
            .ToListAsync();

        Rows = Current.Key switch
        {
            "units" => await s.Hr.OrgUnits.AsNoTracking()
                .Where(x => x.BranchId == BranchId).OrderBy(x => x.Name)
                .Select(x => new MasterRow(x.Id, x.Code, x.Name, "", x.Active)).ToListAsync(),

            "designations" => await s.Hr.Designations.AsNoTracking()
                .Where(x => x.BranchId == BranchId).OrderBy(x => x.Name)
                .Select(x => new MasterRow(x.Id, x.Code, x.Name, "", x.Active)).ToListAsync(),

            "grades" => await s.Hr.Grades.AsNoTracking()
                .Where(x => x.BranchId == BranchId).OrderBy(x => x.Rank).ThenBy(x => x.Name)
                .Select(x => new MasterRow(x.Id, x.Code, x.Name, "Rank " + x.Rank, x.Active)).ToListAsync(),

            "shifts" => await s.Hr.Shifts.AsNoTracking()
                .Where(x => x.BranchId == BranchId).OrderBy(x => x.StartsAt)
                .Select(x => new MasterRow(x.Id, x.Code, x.Name,
                    x.StartsAt.ToString("HH\\:mm") + " – " + x.EndsAt.ToString("HH\\:mm")
                    + (x.EndsNextDay ? " (next day)" : "")
                    + " · " + x.StandardMinutes / 60 + "h" + (x.BreakMinutes > 0 ? " · " + x.BreakMinutes + "m break" : ""),
                    x.Active)).ToListAsync(),

            "leave-types" => await s.Hr.LeaveTypes.AsNoTracking()
                .Where(x => x.BranchId == BranchId).OrderBy(x => x.Name)
                .Select(x => new MasterRow(x.Id, x.Code, x.Name,
                    (x.Paid ? "Paid" : "Unpaid") + " · " + x.Accrual.Replace("_", " "), x.Active)).ToListAsync(),

            "components" => await s.Hr.PayComponents.AsNoTracking()
                .Where(x => x.BranchId == BranchId).OrderBy(x => x.DisplayOrder)
                .Select(x => new MasterRow(x.Id, x.Code, x.Name,
                    x.Kind.Replace("_", " ") + " · "
                    + (x.CalcMethod == PayComponentCalc.PercentOf
                        ? x.PercentBp / 100 + "% of another component"
                        : x.CalcMethod.Replace("_", " "))
                    + (x.Taxable ? " · taxable" : ""), x.Active)).ToListAsync(),

            _ => [],
        };
    });
}
