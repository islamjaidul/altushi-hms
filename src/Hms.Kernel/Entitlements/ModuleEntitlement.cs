using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Hms.Kernel.Entitlements;

/// <summary>
/// ADR-0016 choke point 2, finally built (ADR-0026). Marks an endpoint as belonging to a licensed
/// module: without the licence the endpoint refuses, rather than merely vanishing from the sidebar.
/// <para>
/// Applied by Razor Pages convention over a route prefix, never by hand per page — a check that must
/// be remembered on every new screen is a check that will eventually be forgotten.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireModuleAttribute(string module) : Attribute, IAuthorizeData
{
    public string Module { get; } = module;

    /// <summary>Policy name the provider parses back into a module requirement.</summary>
    public string? Policy
    {
        get => ModuleEntitlementPolicy.Name(Module);
        set => throw new NotSupportedException("The policy name is derived from the module.");
    }

    public string? Roles { get; set; }
    public string? AuthenticationSchemes { get; set; }
}

public static class ModuleEntitlementPolicy
{
    public const string Prefix = "module:";

    public static string Name(string module) => Prefix + module;

    public static bool TryParse(string policyName, out string module)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            module = policyName[Prefix.Length..];
            return module.Length > 0;
        }
        module = "";
        return false;
    }
}

public sealed class ModuleEntitlementRequirement(string module) : IAuthorizationRequirement
{
    public string Module { get; } = module;
}

/// <summary>
/// Succeeds only when the loaded entitlement names the module. Entitlement is checked <em>alongside</em>
/// the role policy, never instead of it: losing a licence must not grant anything, and holding one is
/// never a substitute for a permission (G10 stays the actual control).
/// </summary>
public sealed class ModuleEntitlementHandler(EntitlementProvider entitlements)
    : AuthorizationHandler<ModuleEntitlementRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ModuleEntitlementRequirement requirement)
    {
        if (entitlements.IsLoaded && entitlements.IsEnabled(requirement.Module))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Refuses mutating requests once the licence is past its grace window (P6). Reads always pass —
/// a hospital or an employer must never be locked out of its own records by a billing dispute.
/// <para>
/// The entitlement screen itself is exempt, or a customer who has paid could not apply the file that
/// fixes the problem.
/// </para>
/// </summary>
public sealed class ReadOnlyEntitlementMiddleware(RequestDelegate next, EntitlementProvider entitlements)
{
    /// <summary>Paths that stay writable in read-only mode, so recovery is always possible.</summary>
    public static readonly string[] AlwaysWritable = ["/admin/entitlement", "/login", "/logout"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsMutating(context.Request.Method)
            && entitlements.IsLoaded
            && !entitlements.WritesAllowed
            && !AlwaysWritable.Any(p =>
                context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            var expiry = entitlements.Current.ExpiresUtc;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(
                $"This licence expired on {expiry:dd MMM yyyy} and its grace period has ended, so "
                + "changes cannot be saved. Everything remains readable and printable. Ask the "
                + "administrator to upload a renewed licence file.");
            return;
        }

        await next(context);
    }

    private static bool IsMutating(string method)
        => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
}
