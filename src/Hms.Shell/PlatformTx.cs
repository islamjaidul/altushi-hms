using Hms.Kernel.Auth;
using Hms.Kernel.Data;

namespace Hms.Shell;

/// <summary>
/// The two contexts a platform-administration action ever touches. Deliberately narrow, for the same
/// reason <c>HrScope</c> is: these pages live in a razor class library shipped by both hosts
/// (ADR-0025), so they cannot inject the ERP's <c>HmsTx</c> — that type exposes fourteen module
/// contexts and will not compile without all fourteen assemblies.
/// </summary>
public sealed class PlatformScope(KernelDbContext kernel, AuthDbContext auth)
{
    /// <summary>Audit. Every grant, revoke and password reset lands here (§8 N5).</summary>
    public KernelDbContext Kernel { get; } = kernel;

    /// <summary>Identity: users, roles, and the permission rows §12's matrix is made of.</summary>
    public AuthDbContext Auth { get; } = auth;
}

/// <summary>
/// G19 for identity administration: one action, one transaction. A permission grant and its audit
/// row commit together or not at all — an ungrantable claim that nonetheless logged as granted would
/// be worse than a failure, because the log is what anyone would trust afterwards.
/// <para>
/// ADR-0025 anticipated this seam for HR and for the UI, and missed it here. §5 M21 — user
/// management and role-based access — is not a hospital feature; it is what makes any install
/// administrable. Shipping it in the ERP host only left the HRM SKU with no way to create a second
/// user (spec 0036).
/// </para>
/// </summary>
public interface IPlatformTx
{
    Task<T> RunAsync<T>(Func<PlatformScope, Task<T>> body, CancellationToken ct = default);

    Task RunAsync(Func<PlatformScope, Task> body, CancellationToken ct = default);
}
