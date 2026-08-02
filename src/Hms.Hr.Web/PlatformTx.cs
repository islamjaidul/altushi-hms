using Hms.Hr;
using Hms.Shell;

namespace Hms.Hr.Web;

/// <summary>
/// G19 for identity administration on the HRM SKU. It projects the host's existing <see cref="HrTx"/>
/// rather than opening a second connection of its own: one connection, one transaction, and no
/// second place where the schema-to-context mapping could drift.
/// </summary>
public sealed class PlatformTxOverHr(IHrTx tx) : IPlatformTx
{
    public Task<T> RunAsync<T>(Func<PlatformScope, Task<T>> body, CancellationToken ct = default)
        => tx.RunAsync(s => body(new PlatformScope(s.Kernel, s.Auth)), ct);

    public Task RunAsync(Func<PlatformScope, Task> body, CancellationToken ct = default)
        => tx.RunAsync(s => body(new PlatformScope(s.Kernel, s.Auth)), ct);
}
