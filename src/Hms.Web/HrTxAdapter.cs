using Hms.Hr;
using Hms.Hr.Contracts;

namespace Hms.Web;

/// <summary>
/// Lets HR pages — which live in a razor class library shared by two hosts (ADR-0025) — run inside
/// this host's existing <see cref="HmsTx"/>. One connection, one transaction: an HR write and its
/// kernel audit row commit together exactly as every other module's do (G19 unchanged).
/// </summary>
public sealed class HrTxAdapter(HmsTx tx) : IHrTx
{
    public Task<T> RunAsync<T>(Func<HrScope, Task<T>> body, CancellationToken ct = default)
        => tx.RunAsync(s => body(new HrScope(s.Kernel, s.Auth, s.Hr)), ct);

    public Task RunAsync(Func<HrScope, Task> body, CancellationToken ct = default)
        => tx.RunAsync(s => body(new HrScope(s.Kernel, s.Auth, s.Hr)), ct);
}

/// <summary>
/// What happens to a payroll journal until M15 Accounts exists (Wave 4 of the phase-2 plan, unbuilt)
/// and in the HRM SKU, where it never will.
/// <para>
/// §6.6 is explicit that no operational module writes ledger entries directly, so HR does not invent
/// a ledger to write into. <c>PayrollService.LockAsync</c> has already persisted the balanced journal
/// on the run; this implementation exists so that "post" is a real, auditable step rather than a
/// missing one — the same "post into a holding structure the real ledger consumes later" reasoning
/// §9A.2 applied to day-close. When M15 lands it replaces this registration and reads the stored
/// journals.
/// </para>
/// </summary>
public sealed class JournalOnlyPosting : IPayrollPosting
{
    public Task<long> PostAsync(PayrollJournal journal, long actorId, CancellationToken ct = default)
        => Task.FromResult(journal.RunId);
}
