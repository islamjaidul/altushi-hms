namespace Hms.Hr.Contracts;

/// <summary>
/// The whole public surface of M16 (ADR-0003: cross-module reference goes through contracts only).
/// M15 Accounts consumes <see cref="IPayrollPosting"/>; M17 Consultant Payments consumes
/// <see cref="PayeeRecord"/> for its payee master (11-build-plan-phase2 wave 5). Neither module
/// exists yet — these types exist so that when they do, HR is already a supplier and not a rewrite.
/// </summary>

/// <summary>An employee as a payable party: what a ledger or a bank file needs, and nothing more.</summary>
public sealed record PayeeRecord(
    long EmployeeId,
    string EmployeeCode,
    string FullName,
    string? BankName,
    string? BankAccountNo,
    string? BankRoutingNo,
    string? Tin,
    bool Active);

/// <summary>One line of an approved payroll run, as an accounting posting would see it.</summary>
public sealed record PayrollJournalLine(
    string Account,
    string Narration,
    long DebitTaka,
    long CreditTaka,
    long? EmployeeId);

/// <summary>
/// A locked payroll run offered to the ledger. §6.6 is explicit that no operational module writes
/// ledger entries directly — HR hands over a balanced journal and M15 decides what to do with it.
/// </summary>
public sealed record PayrollJournal(
    long RunId,
    long BranchId,
    DateOnly Period,
    string RunNo,
    IReadOnlyList<PayrollJournalLine> Lines)
{
    public long TotalDebit => Lines.Sum(l => l.DebitTaka);
    public long TotalCredit => Lines.Sum(l => l.CreditTaka);
    public bool IsBalanced => TotalDebit == TotalCredit;
}

/// <summary>
/// Implemented by M15 when it lands. Until then the host registers a no-op recorder so a locked run
/// still produces and stores its journal — the data the ledger will need is captured from day one
/// (the same "holding structure" reasoning §9A.2 applied to day-close).
/// </summary>
public interface IPayrollPosting
{
    Task<long> PostAsync(PayrollJournal journal, long actorId, CancellationToken ct = default);
}
