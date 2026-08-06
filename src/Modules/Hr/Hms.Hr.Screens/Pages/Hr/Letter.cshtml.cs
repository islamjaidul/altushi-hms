using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// One issued letter, on letterhead, ready to print (module PRD §5A-17, G20).
/// <para>
/// Renders the <b>frozen</b> body stored at issue, never a fresh render of today's template. That is
/// the whole point of the design: the document reproduces itself years later, exactly as it was
/// signed, in the same way a payslip reproduces last year's component names.
/// </para>
/// <para>
/// A letter that states a salary is behind salary read as well (D6). Refusing here rather than
/// blanking the figure keeps the rule in one place — the reader either gets the document or is told
/// they may not have it.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.DocumentIssue)]
public class LetterModel(IHrTx tx) : HmsPageModel
{
    public IssuedLetter? Letter { get; private set; }
    public Employee? Person { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        await tx.RunAsync(async s =>
        {
            Letter = await s.Hr.IssuedLetters.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id && l.BranchId == BranchId);
            if (Letter is null) return;

            Person = await s.Hr.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == Letter.EmployeeId);
        });

        if (Letter is null) return NotFound();
        if (LetterKind.SalaryBearing(Letter.Kind) && !Can("hr.salary.read")) return Forbid();

        return Page();
    }
}
