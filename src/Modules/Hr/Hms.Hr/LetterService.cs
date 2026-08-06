using System.Text.RegularExpressions;
using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// What a letter can say about a person. Every value is a plain string the template can name; the
/// salary ones are only ever populated for a reader who holds <c>hr.salary.read</c> (D6), so a body
/// that mentions salary in a context that must not reveal it renders the token unresolved rather
/// than the figure.
/// </summary>
public sealed record LetterFacts
{
    public required string EmployeeName { get; init; }
    public required string EmployeeCode { get; init; }
    public string? FatherName { get; init; }
    public string? Designation { get; init; }
    public string? Department { get; init; }
    public string? Grade { get; init; }
    public required string JoinedOn { get; init; }
    public string? ConfirmedOn { get; init; }
    public string? ProbationDueOn { get; init; }
    public string? LastWorkingDay { get; init; }
    public string? EmploymentType { get; init; }
    public string? NationalId { get; init; }

    /// <summary>Null unless the issuer may see pay. A null renders as an unresolved token, on purpose.</summary>
    public string? GrossSalary { get; init; }
    public string? BasicSalary { get; init; }

    public required string OrganisationName { get; init; }
    public required string Today { get; init; }
    public required string LetterNo { get; init; }
}

/// <summary>
/// Letter templates and issued letters (module PRD §7.1, §5A-17, G20).
/// <para>
/// The load-bearing rule is that <b>a template is editable and an issued letter is not</b>. An
/// <see cref="IssuedLetter"/> stores the rendered body, not a template id — the same discipline a
/// payslip uses when it snapshots component names. Editing the appointment-letter wording next year
/// must not rewrite the appointment letter somebody is holding.
/// </para>
/// </summary>
public sealed partial class LetterService(
    NumberSeriesService numbers, AuditWriter audit, FiscalCalendar fiscal, TimeProvider clock)
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z_.]+)\s*\}\}")]
    private static partial Regex TokenPattern();

    /// <summary>Every token a template may use, with the sentence the editor shows beside it.</summary>
    public static readonly IReadOnlyList<(string Token, string Means)> Tokens =
    [
        ("employee.name", "Full name"),
        ("employee.code", "Employee code"),
        ("employee.father", "Father's name"),
        ("employee.designation", "Current designation"),
        ("employee.department", "Current department or unit"),
        ("employee.grade", "Current grade"),
        ("employee.type", "Kind of employment"),
        ("employee.nid", "National ID number"),
        ("employee.joined_on", "Joining date"),
        ("employee.confirmed_on", "Confirmation date"),
        ("employee.probation_due_on", "Date probation falls due"),
        ("employee.last_working_day", "Last working day"),
        ("employee.gross", "Gross monthly salary — needs salary read"),
        ("employee.basic", "Basic salary — needs salary read"),
        ("org.name", "The organisation's name"),
        ("letter.no", "This letter's reference number"),
        ("today", "Today's date"),
    ];

    /// <summary>
    /// Substitutes <c>{{tokens}}</c>. An unknown or unavailable token is left <b>visible and
    /// unresolved</b> rather than blanked: a confirmation letter that silently drops the joining
    /// date reads as finished and is wrong, while one that still shows <c>{{employee.joined_on}}</c>
    /// is obviously not ready to sign.
    /// </summary>
    public static string Render(string body, LetterFacts facts)
    {
        var values = Values(facts);
        return TokenPattern().Replace(body, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) && v is not null ? v : m.Value);
    }

    /// <summary>The tokens a rendered body still carries — what the issue screen warns about.</summary>
    public static IReadOnlyList<string> Unresolved(string rendered) =>
        [.. TokenPattern().Matches(rendered).Select(m => m.Groups[1].Value).Distinct()];

    private static Dictionary<string, string?> Values(LetterFacts f) => new()
    {
        ["employee.name"] = f.EmployeeName,
        ["employee.code"] = f.EmployeeCode,
        ["employee.father"] = f.FatherName,
        ["employee.designation"] = f.Designation,
        ["employee.department"] = f.Department,
        ["employee.grade"] = f.Grade,
        ["employee.type"] = f.EmploymentType,
        ["employee.nid"] = f.NationalId,
        ["employee.joined_on"] = f.JoinedOn,
        ["employee.confirmed_on"] = f.ConfirmedOn,
        ["employee.probation_due_on"] = f.ProbationDueOn,
        ["employee.last_working_day"] = f.LastWorkingDay,
        ["employee.gross"] = f.GrossSalary,
        ["employee.basic"] = f.BasicSalary,
        ["org.name"] = f.OrganisationName,
        ["letter.no"] = f.LetterNo,
        ["today"] = f.Today,
    };

    // ---- templates -------------------------------------------------------------------------------

    /// <summary>
    /// Suggested wording an employer can start from and then edit. Deliberately <b>not</b> seeded
    /// into the database: a production install starts with no templates, and this is offered on the
    /// editor as a starting point so nothing is issued in words the employer never chose.
    /// </summary>
    public static string Starter(string kind) => kind switch
    {
        LetterKind.Appointment =>
            "Dear {{employee.name}},\n\n"
            + "We are pleased to appoint you as {{employee.designation}} in the {{employee.department}} "
            + "department of {{org.name}}, with effect from {{employee.joined_on}}.\n\n"
            + "Your gross monthly salary is BDT {{employee.gross}}. The terms of your employment are "
            + "as set out in the employee handbook.\n\n"
            + "Please sign and return a copy of this letter in acceptance.\n\n"
            + "For {{org.name}}",

        LetterKind.Confirmation =>
            "Dear {{employee.name}},\n\n"
            + "Following the satisfactory completion of your probation, we are pleased to confirm your "
            + "appointment as {{employee.designation}} with effect from {{employee.confirmed_on}}.\n\n"
            + "All other terms of your employment remain unchanged.\n\n"
            + "For {{org.name}}",

        LetterKind.ProbationExtension =>
            "Dear {{employee.name}},\n\n"
            + "Your probation as {{employee.designation}} has been extended to "
            + "{{employee.probation_due_on}}. Your performance will be reviewed again on that date.\n\n"
            + "For {{org.name}}",

        LetterKind.Experience =>
            "TO WHOM IT MAY CONCERN\n\n"
            + "This is to certify that {{employee.name}} ({{employee.code}}), son/daughter of "
            + "{{employee.father}}, was employed at {{org.name}} as {{employee.designation}} in the "
            + "{{employee.department}} department from {{employee.joined_on}} to "
            + "{{employee.last_working_day}}.\n\n"
            + "We found their conduct and performance satisfactory throughout, and we wish them well "
            + "in their future endeavours.\n\n"
            + "For {{org.name}}",

        LetterKind.Relieving =>
            "Dear {{employee.name}},\n\n"
            + "This is to confirm that you have been relieved of your duties as "
            + "{{employee.designation}} at {{org.name}} at the close of business on "
            + "{{employee.last_working_day}}, and that all dues between us have been settled.\n\n"
            + "For {{org.name}}",

        LetterKind.SalaryCertificate =>
            "TO WHOM IT MAY CONCERN\n\n"
            + "This is to certify that {{employee.name}} ({{employee.code}}) has been employed at "
            + "{{org.name}} as {{employee.designation}} since {{employee.joined_on}}.\n\n"
            + "Their present gross monthly salary is BDT {{employee.gross}}, of which BDT "
            + "{{employee.basic}} is basic.\n\n"
            + "This certificate is issued on request and carries no liability on the part of "
            + "{{org.name}}.\n\n"
            + "For {{org.name}}",

        LetterKind.Termination =>
            "Dear {{employee.name}},\n\n"
            + "This is to inform you that your employment as {{employee.designation}} at {{org.name}} "
            + "stands terminated with effect from {{employee.last_working_day}}.\n\n"
            + "Your final settlement will be paid in accordance with your terms of employment.\n\n"
            + "For {{org.name}}",

        LetterKind.Noc =>
            "TO WHOM IT MAY CONCERN\n\n"
            + "{{org.name}} has no objection to {{employee.name}} ({{employee.code}}), presently "
            + "employed as {{employee.designation}}, proceeding with the matter for which this "
            + "certificate has been sought.\n\n"
            + "For {{org.name}}",

        LetterKind.Increment or LetterKind.Promotion =>
            "Dear {{employee.name}},\n\n"
            + "We are pleased to inform you that your remuneration as {{employee.designation}} has "
            + "been revised with effect from the date stated below.\n\n"
            + "All other terms of your employment remain unchanged.\n\n"
            + "For {{org.name}}",

        _ => "Dear {{employee.name}},\n\n\n\nFor {{org.name}}",
    };

    /// <summary>
    /// Saves the employer's wording. One active template per kind is a database constraint, not a
    /// convention — "which template did this letter use" must never be a question.
    /// </summary>
    public async Task<LetterTemplate> SaveTemplateAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, string kind, string title, string body,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!LetterKind.All.Contains(kind))
            throw new HrException("That is not a letter this product issues.");
        if (string.IsNullOrWhiteSpace(title))
            throw new HrException("The letter needs a heading.");
        if (string.IsNullOrWhiteSpace(body))
            throw new HrException("The letter needs a body.");

        var known = Tokens.Select(t => t.Token).ToHashSet(StringComparer.Ordinal);
        var unknown = TokenPattern().Matches(body)
            .Select(m => m.Groups[1].Value)
            .Where(t => !known.Contains(t))
            .Distinct()
            .ToList();
        if (unknown.Count > 0)
            throw new HrException(
                "This template uses words the product cannot fill in: "
                + string.Join(", ", unknown.Select(u => "{{" + u + "}}"))
                + ". They would print unresolved on the letter.");

        var existing = await hr.LetterTemplates
            .FirstOrDefaultAsync(t => t.BranchId == branchId && t.Kind == kind && t.Active, ct);

        if (existing is null)
        {
            existing = new LetterTemplate
            {
                BranchId = branchId,
                Kind = kind,
                Title = title,
                Body = body,
                Active = true,
                CreatedAt = clock.GetUtcNow(),
                CreatedBy = actorId,
            };
            hr.LetterTemplates.Add(existing);
            await hr.SaveChangesAsync(ct);

            audit.Append(kernel, branchId, actorId, actorName, "hr.letter_template.create",
                "hr.letter_template", existing.Id, after: new { kind, title });
        }
        else
        {
            var before = new { existing.Title, existing.Body };
            existing.Title = title;
            existing.Body = body;
            existing.UpdatedAt = clock.GetUtcNow();
            existing.UpdatedBy = actorId;

            audit.Append(kernel, branchId, actorId, actorName, "hr.letter_template.update",
                "hr.letter_template", existing.Id, before: before, after: new { title, body });
        }

        return existing;
    }

    // ---- issuing ---------------------------------------------------------------------------------

    /// <summary>
    /// Issues a letter: number from the series, body frozen, row append-only. The rendered body is
    /// passed in rather than resolved here because the caller is the only thing that knows whether
    /// the reader may see a salary figure — the permission decision stays on the screen, where every
    /// other one in this module lives.
    /// </summary>
    public async Task<IssuedLetter> IssueAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        string kind, string title, string renderedBody, DateOnly issuedOn, long? separationId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!LetterKind.All.Contains(kind))
            throw new HrException("That is not a letter this product issues.");
        if (string.IsNullOrWhiteSpace(renderedBody))
            throw new HrException("There is nothing to issue — the letter body is empty.");

        var (_, letterNo) = await numbers.IssueAsync(
            kernel, branchId, "hr_letter", fiscal.FiscalYearOf(issuedOn), "LTR-{fy}-{n:D4}", ct);

        var letter = new IssuedLetter
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Kind = kind,
            LetterNo = letterNo,
            Title = title,
            Body = renderedBody,
            IssuedOn = issuedOn,
            SeparationId = separationId,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
            CreatedByName = actorName,
        };
        hr.IssuedLetters.Add(letter);
        await hr.SaveChangesAsync(ct);

        // N-HR8's shape: issuing a document that states someone's pay is itself worth a record.
        audit.Append(kernel, branchId, actorId, actorName, "hr.letter.issue", "hr.issued_letter",
            letter.Id, after: new { employeeId, kind, letterNo, issuedOn },
            tier: LetterKind.SalaryBearing(kind) ? (short)1 : (short)2);

        return letter;
    }
}
