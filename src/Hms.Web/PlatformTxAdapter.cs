using Hms.Shell;

namespace Hms.Web;

/// <summary>
/// Lets the shared identity-administration screens — which live in a razor class library shipped by
/// both hosts (ADR-0025) — run inside this host's existing <see cref="HmsTx"/>. One connection, one
/// transaction: a permission grant and its audit row commit together exactly as every other write
/// does (G19 unchanged).
/// </summary>
public sealed class PlatformTxAdapter(HmsTx tx) : IPlatformTx
{
    public Task<T> RunAsync<T>(Func<PlatformScope, Task<T>> body, CancellationToken ct = default)
        => tx.RunAsync(s => body(new PlatformScope(s.Kernel, s.Auth)), ct);

    public Task RunAsync(Func<PlatformScope, Task> body, CancellationToken ct = default)
        => tx.RunAsync(s => body(new PlatformScope(s.Kernel, s.Auth)), ct);
}
