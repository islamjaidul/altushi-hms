using Hms.Hr.Data;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;

namespace Hms.Hr;

/// <summary>
/// The three contexts an HR business action ever touches. Deliberately narrow: HR pages live in a
/// razor class library that ships in two hosts (ADR-0025), so they cannot inject the ERP host's
/// <c>HmsTx</c> — that type exposes fourteen module contexts and cannot compile without all
/// fourteen assemblies.
/// </summary>
public sealed class HrScope(KernelDbContext kernel, AuthDbContext auth, HrDbContext hr)
{
    /// <summary>Audit, numbering, approvals, jobs.</summary>
    public KernelDbContext Kernel { get; } = kernel;

    /// <summary>Identity — self-service provisioning writes <c>AppUser.EmployeeRef</c>.</summary>
    public AuthDbContext Auth { get; } = auth;

    public HrDbContext Hr { get; } = hr;
}

/// <summary>
/// G19 for HR: one business action, one transaction. The ERP host implements this by projecting its
/// existing <c>HmsTx</c> scope, so an HR write commits together with the kernel audit row exactly as
/// every other module does; the HRM host implements it over its own three contexts. Neither the HR
/// services nor the HR pages can tell which host they are running in.
/// </summary>
public interface IHrTx
{
    Task<T> RunAsync<T>(Func<HrScope, Task<T>> body, CancellationToken ct = default);

    Task RunAsync(Func<HrScope, Task> body, CancellationToken ct = default);
}

/// <summary>The operator-facing failure path for this module: a plain sentence, never a stack trace.</summary>
public sealed class HrException(string message) : Exception(message);
