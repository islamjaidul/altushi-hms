using Hms.Kernel.Time;
using Hms.Shell.Reporting;
using Microsoft.Extensions.Caching.Memory;

namespace Hms.Hr.Screens;

/// <summary>
/// Supplies the employer's leave year and financial year to the period control (module PRD §12.3:
/// both are "taken from the employer's configured start month, never assumed").
/// <para>
/// It lives in the screens library rather than in <c>Hms.Hr</c> because it implements a
/// <c>Hms.Shell</c> interface and <c>Hms.Hr.csproj</c> deliberately does not reference the Shell —
/// the domain assembly knows nothing about presentation. This is the same direction as
/// <c>HrTxAdapter</c>: the module implements the seam, the host composes it.
/// </para>
/// <para>
/// <b>Call it before opening a transaction, never inside one.</b> It opens its own
/// <see cref="IHrTx"/> scope, so invoking it from within an enclosing <c>RunAsync</c> would take a
/// second connection for a read the caller could have made itself.
/// </para>
/// </summary>
public sealed class HrPeriodCalendarSource(
    IHrTx tx,
    PolicyResolver policies,
    FiscalCalendar fiscal,
    IMemoryCache cache) : IPeriodCalendarSource
{
    /// <summary>
    /// Short: a policy change must show up in the control without a restart, and the read is cheap.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async ValueTask<PeriodCalendar> ForAsync(long branchId, DateOnly on,
        CancellationToken ct = default)
    {
        var key = $"hr.period-calendar:{branchId}:{on:yyyy-MM}";
        if (cache.TryGetValue<PeriodCalendar>(key, out var cached) && cached is not null) return cached;

        var policy = await tx.RunAsync(s => policies.PayrollAsync(s.Hr, branchId, on, ct), ct);

        // No policy configured is not an error and not a guess: the module states the default it is
        // using and the control labels the preset "(default)", so an operator can see the difference
        // between their policy and ours. Inventing a leave year would be the same class of mistake
        // as shipping a tax slab (D2).
        var configured = policy is not null && policy.LeaveYearStartMonth is >= 1 and <= 12;

        var calendar = new PeriodCalendar(
            LeaveYearStartMonth: configured ? policy!.LeaveYearStartMonth : PeriodCalendar.Default.LeaveYearStartMonth,
            FinancialYearStartMonth: fiscal.StartMonth,
            WeekStartsOn: PeriodCalendar.Default.WeekStartsOn,
            MaxRangeDays: PeriodCalendar.Default.MaxRangeDays)
        {
            IsDefault = !configured,
        };

        cache.Set(key, calendar, Ttl);
        return calendar;
    }
}
