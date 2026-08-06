using System.Globalization;
using System.Text;
using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// Salary hold, variance review and disbursement — the three things that stood between a computed
/// payslip and money actually reaching somebody (module PRD §7.7 — G45, G46, G47).
/// <para>
/// Before this, <c>PostAsync</c> handed a journal to Accounts and, as far as the product was
/// concerned, the money then vanished: no bank file, no cash sheet, no cheque list, and no
/// per-employee paid status. §9's terminal state is <b>Disbursed</b> and the module had no such
/// state.
/// </para>
/// </summary>
public sealed class DisbursementService(
    NumberSeriesService numbers, AuditWriter audit, FiscalCalendar fiscal, TimeProvider clock)
{
    // ---- salary hold (G46) -----------------------------------------------------------------------

    /// <summary>
    /// Withholds one person's salary. The run still computes their line — it is marked held with a
    /// zero net, never dropped, because a sheet that silently omits somebody makes its own headcount
    /// a lie and hides the reason nobody was paid.
    /// </summary>
    public async Task<SalaryHold> HoldAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("Say why the salary is being held — the employee is entitled to know.");

        var employee = await hr.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new HrException("That employee no longer exists.");

        var live = await hr.SalaryHolds
            .FirstOrDefaultAsync(h => h.EmployeeId == employeeId && h.ReleasedAt == null, ct);
        if (live is not null)
            throw new HrException($"{employee.FullName}'s salary is already held.");

        var hold = new SalaryHold
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Reason = reason,
            HeldAt = clock.GetUtcNow(),
            HeldBy = actorId,
            HeldByName = actorName,
        };
        hr.SalaryHolds.Add(hold);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.salary.hold", "hr.salary_hold",
            hold.Id, after: new { employeeId, employee.EmployeeCode, reason }, tier: 1);

        return hold;
    }

    public async Task ReleaseAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long holdId, string? reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var hold = await hr.SalaryHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct)
                   ?? throw new HrException("That hold no longer exists.");
        if (hold.ReleasedAt is not null) return;

        hold.ReleasedAt = clock.GetUtcNow();
        hold.ReleasedBy = actorId;
        hold.ReleaseReason = reason;

        audit.Append(kernel, branchId, actorId, actorName, "hr.salary.release", "hr.salary_hold",
            holdId, before: new { hold.Reason }, after: new { reason }, tier: 1);
    }

    public static Task<List<SalaryHold>> LiveHoldsAsync(
        HrDbContext hr, long branchId, CancellationToken ct = default)
        => hr.SalaryHolds.AsNoTracking()
            .Where(h => h.BranchId == branchId && h.ReleasedAt == null)
            .ToListAsync(ct);

    // ---- variance review (G47) ---------------------------------------------------------------------

    /// <summary>
    /// Builds the comparison: this run's net against the same employee's previous run. Anything
    /// outside the employer's tolerance has to carry a reason before the run can be approved.
    /// </summary>
    public static async Task BuildVarianceAsync(
        HrDbContext hr, long branchId, long runId, CancellationToken ct = default)
    {
        var run = await hr.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new HrException("That payroll run no longer exists.");

        var existing = await hr.VarianceNotes.Where(v => v.RunId == runId).ToListAsync(ct);
        hr.VarianceNotes.RemoveRange(existing);

        var lines = await hr.PayrollLines.AsNoTracking()
            .Where(l => l.RunId == runId)
            .Select(l => new { l.EmployeeId, l.NetPayTaka })
            .ToListAsync(ct);
        if (lines.Count == 0) return;

        // The previous run for this branch that actually paid: a cancelled one is not a comparison.
        var previousRun = await hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.Id != runId && r.Period < run.Period
                        && r.Kind == PayrollRunKind.Regular
                        && r.State != PayrollRunState.Cancelled)
            .OrderByDescending(r => r.Period)
            .FirstOrDefaultAsync(ct);

        var previous = previousRun is null
            ? []
            : await hr.PayrollLines.AsNoTracking()
                .Where(l => l.RunId == previousRun.Id)
                .ToDictionaryAsync(l => l.EmployeeId, l => l.NetPayTaka, ct);

        foreach (var line in lines)
        {
            var before = previous.GetValueOrDefault(line.EmployeeId);
            hr.VarianceNotes.Add(new VarianceNote
            {
                BranchId = branchId,
                RunId = runId,
                EmployeeId = line.EmployeeId,
                PreviousNetTaka = before,
                CurrentNetTaka = line.NetPayTaka,
                DeltaBp = DeltaBp(before, line.NetPayTaka),
            });
        }

        await hr.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The movement, in basis points of the previous net. A first appearance — a new joiner, or
    /// anyone the previous run did not pay — is <b>not</b> an infinite rise: it is reported as new,
    /// because "up 100%" would drown the register in noise every month a hire lands.
    /// </summary>
    public static int DeltaBp(long previous, long current)
    {
        if (previous == 0) return current == 0 ? 0 : int.MaxValue;
        var delta = (decimal)(current - previous) * 10_000m / previous;
        return (int)Math.Round(Math.Clamp(delta, int.MinValue / 2, int.MaxValue / 2));
    }

    /// <summary>Whether a movement needs explaining, given the employer's tolerance.</summary>
    public static bool NeedsReason(int deltaBp, int toleranceBp) =>
        deltaBp == int.MaxValue || Math.Abs(deltaBp) > Math.Max(0, toleranceBp);

    public async Task ExplainAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long noteId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("A change outside tolerance needs a reason before the run is approved.");

        var note = await hr.VarianceNotes.FirstOrDefaultAsync(v => v.Id == noteId, ct)
                   ?? throw new HrException("That variance line no longer exists.");

        note.Reason = reason;
        note.ReviewedAt = clock.GetUtcNow();
        note.ReviewedBy = actorId;
        note.ReviewedByName = actorName;

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.variance_explain",
            "hr.variance_note", noteId,
            after: new { note.EmployeeId, note.PreviousNetTaka, note.CurrentNetTaka, reason }, tier: 2);
    }

    /// <summary>
    /// The lines still needing an explanation. §9 makes this the gate: approval is not offered until
    /// this list is empty.
    /// </summary>
    public static async Task<List<VarianceNote>> OutstandingAsync(
        HrDbContext hr, long runId, int toleranceBp, CancellationToken ct = default)
    {
        var notes = await hr.VarianceNotes.AsNoTracking()
            .Where(v => v.RunId == runId && v.Reason == null)
            .ToListAsync(ct);

        return [.. notes.Where(n => NeedsReason(n.DeltaBp, toleranceBp))];
    }

    // ---- disbursement (G45) ------------------------------------------------------------------------

    /// <summary>
    /// Prepares the batch for a locked run: one line per person who is actually owed money, split by
    /// how they are paid. Bank details are snapshotted, so a file reproduced next year shows the
    /// account it paid rather than the one the employee has since changed to.
    /// </summary>
    public async Task<Disbursement> PrepareAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId, DateOnly valueDate,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await hr.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new HrException("That payroll run no longer exists.");
        if (run.State is not (PayrollRunState.Locked or PayrollRunState.Posted))
            throw new HrException(
                $"{run.RunNo} is {run.State.Replace('_', ' ')} — only a locked run is disbursed.");

        var already = await hr.Disbursements.AsNoTracking().FirstOrDefaultAsync(d => d.RunId == runId, ct);
        if (already is not null)
            throw new HrException($"{run.RunNo} already has batch {already.BatchNo}.");

        var lines = await (
            from l in hr.PayrollLines.AsNoTracking()
            join e in hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
            where l.RunId == runId && l.NetPayTaka > 0 && !l.Held
            orderby e.EmployeeCode
            select new
            {
                PayrollLineId = l.Id, l.EmployeeId, l.NetPayTaka,
                e.EmployeeCode, e.FullName, e.PaymentMethod,
                e.BankName, e.BankAccountNo, e.BankRoutingNo,
            })
            .ToListAsync(ct);

        if (lines.Count == 0)
            throw new HrException(
                $"{run.RunNo} has nobody to pay — every line is zero, or held.");

        var (_, batchNo) = await numbers.IssueAsync(
            kernel, branchId, "hr_disbursement", fiscal.FiscalYearOf(valueDate), "DB-{fy}-{n:D4}", ct);

        var batch = new Disbursement
        {
            BranchId = branchId,
            RunId = runId,
            BatchNo = batchNo,
            State = DisbursementState.Prepared,
            ValueDate = valueDate,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
            CreatedByName = actorName,
        };
        hr.Disbursements.Add(batch);
        await hr.SaveChangesAsync(ct);

        foreach (var line in lines)
        {
            // Somebody marked "bank" with no account is paid in cash, not paid into nothing. The
            // alternative is a bank file with a blank account number, which the bank rejects for
            // the whole batch rather than the one row.
            var method = line.PaymentMethod == PaymentMethod.Bank
                         && string.IsNullOrWhiteSpace(line.BankAccountNo)
                ? PaymentMethod.Cash
                : line.PaymentMethod;

            hr.DisbursementLines.Add(new DisbursementLine
            {
                BranchId = branchId,
                DisbursementId = batch.Id,
                PayrollLineId = line.PayrollLineId,
                EmployeeId = line.EmployeeId,
                EmployeeCode = line.EmployeeCode,
                EmployeeName = line.FullName,
                Method = method,
                AmountTaka = line.NetPayTaka,
                BankName = line.BankName,
                BankAccountNo = line.BankAccountNo,
                BankRoutingNo = line.BankRoutingNo,
            });

            batch.TotalTaka += line.NetPayTaka;
            switch (method)
            {
                case PaymentMethod.Bank: batch.BankTaka += line.NetPayTaka; break;
                case PaymentMethod.Cheque: batch.ChequeTaka += line.NetPayTaka; break;
                default: batch.CashTaka += line.NetPayTaka; break;
            }
        }
        batch.LineCount = lines.Count;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.disbursement.prepare",
            "hr.disbursement", batch.Id,
            after: new { batch.BatchNo, run.RunNo, batch.TotalTaka, batch.LineCount,
                batch.BankTaka, batch.CashTaka, batch.ChequeTaka }, tier: 1);

        return batch;
    }

    /// <summary>
    /// Records that one person was paid. One-way: hard rule 4 means an unpaid marking is a
    /// correction on the record, not an erasure — so there is no un-pay.
    /// </summary>
    public async Task MarkPaidAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long lineId, string? reference,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var line = await hr.DisbursementLines.FirstOrDefaultAsync(l => l.Id == lineId, ct)
                   ?? throw new HrException("That payment line no longer exists.");
        if (line.PaidAt is not null)
            throw new HrException($"{line.EmployeeName} was already marked paid.");

        line.PaidAt = clock.GetUtcNow();
        line.PaidBy = actorId;
        line.PaidByName = actorName;
        line.Reference = reference;

        var batch = await hr.Disbursements.FirstOrDefaultAsync(d => d.Id == line.DisbursementId, ct)!;
        var outstanding = await hr.DisbursementLines
            .CountAsync(l => l.DisbursementId == line.DisbursementId && l.PaidAt == null
                             && l.Id != lineId, ct);

        batch!.State = outstanding == 0 ? DisbursementState.Paid : DisbursementState.PartlyPaid;

        if (outstanding == 0)
        {
            var run = await hr.PayrollRuns.FirstOrDefaultAsync(r => r.Id == batch.RunId, ct);
            if (run is not null)
            {
                run.DisbursedAt = clock.GetUtcNow();
                // §9's terminal state, reached only when the last person actually has their money.
                if (run.State == PayrollRunState.Posted) run.State = PayrollRunState.Disbursed;
            }
        }

        audit.Append(kernel, branchId, actorId, actorName, "hr.disbursement.pay",
            "hr.disbursement_line", lineId,
            after: new { line.EmployeeCode, line.AmountTaka, line.Method, reference }, tier: 1);
    }

    /// <summary>Marks every unpaid line of one method at once — what a bank confirmation actually is.</summary>
    public async Task MarkMethodPaidAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long disbursementId, string method,
        string? reference, long actorId, string actorName, CancellationToken ct = default)
    {
        var ids = await hr.DisbursementLines
            .Where(l => l.DisbursementId == disbursementId && l.Method == method && l.PaidAt == null)
            .Select(l => l.Id)
            .ToListAsync(ct);

        if (ids.Count == 0)
            throw new HrException($"Nothing is outstanding by {PaymentMethod.Label(method).ToLowerInvariant()}.");

        foreach (var id in ids)
            await MarkPaidAsync(hr, kernel, branchId, id, reference, actorId, actorName, ct);
    }

    // ---- the files ---------------------------------------------------------------------------------

    /// <summary>
    /// The bank transfer file. A documented, plain CSV rather than a named bank's proprietary
    /// layout: §3.4's BEFTN format is not something this project can cite, and hard rule 3 forbids
    /// asserting a format we have not verified. Every Bangladeshi bank's corporate portal accepts a
    /// column-mapped CSV upload, so this is honest and usable, and a specific bank's layout becomes
    /// a mapping the customer configures once we have a real specification from them.
    /// </summary>
    public static byte[] BankFile(Disbursement batch, IReadOnlyList<DisbursementLine> lines)
    {
        var sb = new StringBuilder();
        sb.Append("account_no,account_name,routing_no,bank,amount_taka,reference,value_date\r\n");

        foreach (var l in lines.Where(x => x.Method == PaymentMethod.Bank).OrderBy(x => x.EmployeeCode))
        {
            sb.Append(Csv(l.BankAccountNo)).Append(',')
              .Append(Csv(l.EmployeeName)).Append(',')
              .Append(Csv(l.BankRoutingNo)).Append(',')
              .Append(Csv(l.BankName)).Append(',')
              .Append(l.AmountTaka.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv($"{batch.BatchNo}/{l.EmployeeCode}")).Append(',')
              .Append(batch.ValueDate.ToString("yyyy-MM-dd")).Append("\r\n");
        }

        // The BOM, for the same reason the report exporter carries one: without it Excel on a
        // Bangladeshi desktop mojibakes every Bangla name in the account-name column.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Csv(string? value)
    {
        var v = value ?? "";
        // A leading =, +, - or @ is a formula to Excel. Prefixing with an apostrophe defuses it
        // without changing what a human reads — the same treatment ReportCsv gives every cell.
        if (v.Length > 0 && (v[0] is '=' or '+' or '-' or '@')) v = "'" + v;
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }
}
