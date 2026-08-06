using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// A run's money, split by how it is actually handed over (module PRD §7.7, G45).
/// <para>
/// <c>PostAsync</c> handed a journal to Accounts and the money then vanished as far as the product
/// was concerned — no bank file, no cash sheet, no cheque list, no per-employee paid status. §9's
/// terminal state is Disbursed and the module had no such state.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.PayrollRun)]
public class DisbursementModel(IHrTx tx, DisbursementService money) : HmsPageModel
{
    public PayrollRun? Run { get; private set; }
    public Data.Disbursement? Batch { get; private set; }
    public IReadOnlyList<DisbursementLine> Lines { get; private set; } = [];

    public bool CanSeeMoney => Can("hr.salary.read");

    public IReadOnlyList<DisbursementLine> By(string method) =>
        [.. Lines.Where(l => l.Method == method)];

    public int UnpaidCount => Lines.Count(l => l.PaidAt is null);

    [BindProperty] public string? ValueDate { get; set; }
    [BindProperty] public string? Reference { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        await LoadAsync(id);
        if (Run is null) return NotFound();
        return CanSeeMoney ? Page() : Forbid();
    }

    public async Task<IActionResult> OnPostPrepareAsync(long id)
    {
        if (!CanSeeMoney) return Forbid();

        var value = FlexibleDate.TryParse(ValueDate ?? "", out var d) && d != default
            ? d : Dhaka.Today(TimeProvider.System);

        try
        {
            await tx.RunAsync(s => money.PrepareAsync(
                s.Hr, s.Kernel, BranchId, id, value, ActorId, ActorName));
        }
        catch (HrException e)
        {
            await LoadAsync(id);
            Fail(e.Message);
            return Page();
        }

        Toast("Batch prepared — download the bank file and the cash sheet", "account_balance");
        return Redirect($"/hr/payroll/{id}/disbursement");
    }

    public async Task<IActionResult> OnPostPayLineAsync(long id, long lineId)
    {
        if (!CanSeeMoney) return Forbid();

        try
        {
            await tx.RunAsync(s => money.MarkPaidAsync(
                s.Hr, s.Kernel, BranchId, lineId, Reference, ActorId, ActorName));
        }
        catch (HrException e)
        {
            await LoadAsync(id);
            Fail(e.Message);
            return Page();
        }

        Toast("Marked paid", "task_alt");
        return Redirect($"/hr/payroll/{id}/disbursement");
    }

    public async Task<IActionResult> OnPostPayMethodAsync(long id, string method)
    {
        if (!CanSeeMoney) return Forbid();

        try
        {
            await tx.RunAsync(s => money.MarkMethodPaidAsync(
                s.Hr, s.Kernel, BranchId, Batch?.Id ?? 0, method, Reference, ActorId, ActorName));
        }
        catch (HrException e)
        {
            await LoadAsync(id);
            Fail(e.Message);
            return Page();
        }

        Toast($"{PaymentMethod.Label(method)} marked paid", "task_alt");
        return Redirect($"/hr/payroll/{id}/disbursement");
    }

    /// <summary>The bank's upload file. Salary-bearing, so gated exactly as the screen is.</summary>
    public async Task<IActionResult> OnGetBankFileAsync(long id)
    {
        if (!CanSeeMoney) return Forbid();

        await LoadAsync(id);
        if (Batch is null) return NotFound();

        var bytes = DisbursementService.BankFile(Batch, Lines);
        // Download.Name is period-shaped for reports; a bank file is dated by its value date, so
        // the filename is composed here rather than forced through a shape that does not fit.
        var name = $"bank-transfer_{Batch.BatchNo}_{Batch.ValueDate:yyyy-MM-dd}.csv"
            .Replace('/', '-');
        return File(bytes, "text/csv", name);
    }

    private async Task LoadAsync(long id)
    {
        await tx.RunAsync(async s =>
        {
            Run = await s.Hr.PayrollRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.BranchId == BranchId);
            if (Run is null || !CanSeeMoney) return;

            Batch = await s.Hr.Disbursements.AsNoTracking()
                .FirstOrDefaultAsync(d => d.RunId == id);
            if (Batch is null) return;

            Lines = await s.Hr.DisbursementLines.AsNoTracking()
                .Where(l => l.DisbursementId == Batch.Id)
                .OrderBy(l => l.EmployeeCode)
                .ToListAsync();
        });
    }
}
