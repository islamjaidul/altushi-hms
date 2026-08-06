using Hms.Kernel.Time;

namespace Hms.Shell.Reporting;

/// <summary>
/// Where a <see cref="PeriodCalendar"/> comes from. Declared here because the Shell is the only
/// assembly both hosts and every module's screen library reference; implemented by the module that
/// owns the configuration.
/// <para>
/// The direction of the dependency is the point: HR implements a Shell interface, exactly as
/// <c>HrTxAdapter</c> implements <c>IHrTx</c>. The Shell must never learn what a payroll policy is,
/// and an architecture test asserts it does not.
/// </para>
/// </summary>
public interface IPeriodCalendarSource
{
    ValueTask<PeriodCalendar> ForAsync(long branchId, DateOnly on, CancellationToken ct = default);
}

/// <summary>
/// What a host uses when no module claims the calendar — the standalone platform screens, and any
/// host that ships without HR. Returns the stated defaults, flagged as defaults so that a screen can
/// say "(default)" beside "This leave year" rather than implying the employer chose July.
/// </summary>
public sealed class DefaultPeriodCalendarSource : IPeriodCalendarSource
{
    private static readonly PeriodCalendar Value = PeriodCalendar.Default with { IsDefault = true };

    public ValueTask<PeriodCalendar> ForAsync(long branchId, DateOnly on, CancellationToken ct = default)
        => ValueTask.FromResult(Value);
}
