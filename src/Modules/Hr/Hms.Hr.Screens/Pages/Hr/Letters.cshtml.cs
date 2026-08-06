using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record IssuedRow(
    long Id, string LetterNo, string Kind, string Title, DateOnly IssuedOn,
    string EmployeeName, string EmployeeCode, string IssuedBy);

/// <summary>
/// Letter templates and the letters issued from them (module PRD §5A-17, G20). An employer that
/// cannot print an appointment letter or an experience certificate is not an HR system, and until
/// this screen the product could print neither.
/// <para>
/// The load-bearing distinction: a <b>template is editable</b> and an <b>issued letter is not</b>.
/// Issuing renders the body once and stores it. Editing the template next year does not rewrite the
/// certificate somebody is holding — the same reason a payslip snapshots its component names.
/// </para>
/// <para>
/// Templates are configuration (<c>hr.policy.manage</c>); issuing is an act on a person's record
/// (<c>hr.document.issue</c>), and a letter that states a salary needs <c>hr.salary.read</c> as well.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.DocumentIssue)]
public class LettersModel(IHrTx tx, LetterService letters, OrgIdentity org) : HmsPageModel
{
    public IReadOnlyDictionary<string, LetterTemplate> Templates { get; private set; } =
        new Dictionary<string, LetterTemplate>();
    public IReadOnlyList<IssuedRow> Issued { get; private set; } = [];

    /// <summary>Set when the screen was opened from an employee record, ready to issue.</summary>
    public Employee? Subject { get; private set; }
    public string? Preview { get; private set; }
    public IReadOnlyList<string> PreviewGaps { get; private set; } = [];
    public string SelectedKind { get; private set; } = LetterKind.Appointment;

    public bool CanEditTemplates => Can("hr.policy.manage");
    public bool CanSeeSalary => Can("hr.salary.read");

    public IReadOnlyList<(string Token, string Means)> Tokens => LetterService.Tokens;

    [BindProperty] public string? TemplateKind { get; set; }
    [BindProperty] public string? TemplateTitle { get; set; }
    [BindProperty] public string? TemplateBody { get; set; }
    [BindProperty] public string? IssueBody { get; set; }
    [BindProperty] public string? IssueTitle { get; set; }
    [BindProperty] public string? IssueOn { get; set; }

    public async Task<IActionResult> OnGetAsync(long? employee, string? kind)
    {
        SelectedKind = kind is not null && LetterKind.All.Contains(kind) ? kind : LetterKind.Appointment;
        await LoadAsync(employee);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveTemplateAsync(long? employee)
    {
        if (!CanEditTemplates) return Forbid();

        try
        {
            await tx.RunAsync(s => letters.SaveTemplateAsync(
                s.Hr, s.Kernel, BranchId, TemplateKind ?? "", TemplateTitle ?? "", TemplateBody ?? "",
                ActorId, ActorName));
        }
        catch (HrException e)
        {
            SelectedKind = TemplateKind ?? SelectedKind;
            await LoadAsync(employee);
            Fail(e.Message);
            return Page();
        }

        Toast($"{LetterKind.Label(TemplateKind!)} template saved", "edit_document");
        return Redirect($"/hr/letters?kind={TemplateKind}"
                        + (employee is { } e2 ? $"&employee={e2}" : ""));
    }

    public async Task<IActionResult> OnPostIssueAsync(long employee, string kind)
    {
        if (!LetterKind.All.Contains(kind)) return NotFound();

        // D6, enforced before anything is written: a salary certificate is a salary surface.
        if (LetterKind.SalaryBearing(kind) && !Can("hr.salary.read")) return Forbid();

        var on = FlexibleDate.TryParse(IssueOn ?? "", out var d) && d != default
            ? d : Dhaka.Today(TimeProvider.System);

        long letterId;
        try
        {
            letterId = await tx.RunAsync(async s => (await letters.IssueAsync(
                s.Hr, s.Kernel, BranchId, employee, kind, IssueTitle ?? LetterKind.Label(kind),
                IssueBody ?? "", on, null, ActorId, ActorName)).Id);
        }
        catch (HrException e)
        {
            SelectedKind = kind;
            await LoadAsync(employee);
            Fail(e.Message);
            return Page();
        }

        Toast("Letter issued — its wording is now frozen", "mail");
        return Redirect($"/hr/letters/{letterId}");
    }

    private async Task LoadAsync(long? employeeId)
    {
        await tx.RunAsync(async s =>
        {
            Templates = await s.Hr.LetterTemplates.AsNoTracking()
                .Where(t => t.BranchId == BranchId && t.Active)
                .ToDictionaryAsync(t => t.Kind);

            Issued =
            [
                .. await (
                    from l in s.Hr.IssuedLetters.AsNoTracking()
                    join e in s.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                    orderby l.IssuedOn descending, l.Id descending
                    select new IssuedRow(
                        l.Id, l.LetterNo, l.Kind, l.Title, l.IssuedOn,
                        e.FullName, e.EmployeeCode, l.CreatedByName))
                    .Take(50)
                    .ToListAsync(),
            ];

            if (employeeId is not { } id) return;

            Subject = await s.Hr.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.BranchId == BranchId);
            if (Subject is null) return;

            if (!Templates.TryGetValue(SelectedKind, out var template)) return;

            var facts = await FactsAsync(s, Subject, SelectedKind);
            Preview = LetterService.Render(template.Body, facts);
            PreviewGaps = LetterService.Unresolved(Preview);
        });
    }

    /// <summary>
    /// What the letter may say about this person. The salary facts are populated only for a reader
    /// who holds salary read — a token they may not fill stays visibly unresolved, so a letter that
    /// would have leaked a figure is obviously not ready to sign rather than quietly wrong.
    /// </summary>
    private async Task<LetterFacts> FactsAsync(HrScope s, Employee person, string kind)
    {
        var assignment = await s.Hr.Assignments.AsNoTracking()
            .Where(a => a.EmployeeId == person.Id && a.EffectiveTo == null)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync();

        string? designation = null, unit = null, grade = null;
        if (assignment is not null)
        {
            designation = await s.Hr.Designations.AsNoTracking()
                .Where(x => x.Id == assignment.DesignationId).Select(x => x.Name).FirstOrDefaultAsync();
            unit = await s.Hr.OrgUnits.AsNoTracking()
                .Where(x => x.Id == assignment.OrgUnitId).Select(x => x.Name).FirstOrDefaultAsync();
            grade = await s.Hr.Grades.AsNoTracking()
                .Where(x => x.Id == assignment.GradeId).Select(x => x.Name).FirstOrDefaultAsync();
        }

        var separation = await s.Hr.Separations.AsNoTracking()
            .Where(x => x.EmployeeId == person.Id && x.State != SeparationState.Cancelled)
            .FirstOrDefaultAsync();

        string? gross = null, basic = null;
        if (CanSeeSalary)
        {
            var structure = await s.Hr.PayStructures.AsNoTracking()
                .Where(x => x.EmployeeId == person.Id && x.EffectiveTo == null)
                .OrderByDescending(x => x.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (structure is not null)
            {
                var components = await s.Hr.PayComponents.AsNoTracking()
                    .Where(c => c.BranchId == BranchId && c.Kind == PayComponentKind.Earning)
                    .ToDictionaryAsync(c => c.Id, c => c.DisplayOrder);

                var configured = await s.Hr.PayStructureComponents.AsNoTracking()
                    .Where(l => l.PayStructureId == structure.Id)
                    .ToListAsync();

                var earnings = configured.Where(l => components.ContainsKey(l.ComponentId)).ToList();
                if (earnings.Count > 0)
                {
                    gross = earnings.Sum(l => l.AmountTaka).ToString("N0");
                    basic = earnings.OrderBy(l => components[l.ComponentId])
                        .First().AmountTaka.ToString("N0");
                }
            }
        }

        return new LetterFacts
        {
            EmployeeName = person.FullName,
            EmployeeCode = person.EmployeeCode,
            FatherName = person.FatherName,
            Designation = designation,
            Department = unit,
            Grade = grade,
            EmploymentType = EmploymentType.Label(person.EmploymentType),
            NationalId = person.NationalId,
            JoinedOn = person.JoinedOn.ToString("dd MMMM yyyy"),
            ConfirmedOn = person.ConfirmedOn?.ToString("dd MMMM yyyy"),
            ProbationDueOn = person.ProbationDueOn?.ToString("dd MMMM yyyy"),
            LastWorkingDay = (separation?.LastWorkingDay ?? person.SeparatedOn)?.ToString("dd MMMM yyyy"),
            GrossSalary = gross,
            BasicSalary = basic,
            OrganisationName = org.Name,
            Today = Dhaka.Today(TimeProvider.System).ToString("dd MMMM yyyy"),
            // The real number is issued inside the transaction; the preview says so rather than
            // burning a number on a page the operator may never submit.
            LetterNo = "(issued on save)",
        };
    }

    public static string Starter(string kind) => LetterService.Starter(kind);
}
