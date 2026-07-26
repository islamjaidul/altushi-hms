using Microsoft.AspNetCore.Authorization;

namespace Hms.Kernel.Auth;

/// <summary>
/// ADR-0019 / G10: every endpoint carries a `module.action` policy, evaluated server-side.
/// Policy names are "perm:module.action"; a user satisfies one via a matching "perm" claim
/// stamped at sign-in from role permissions (§12 matrix as data).
/// </summary>
public static class PermissionPolicy
{
    public const string Prefix = "perm:";
    public const string ClaimType = "perm";

    public static string Name(string permission) => Prefix + permission;

    public static bool TryParse(string policyName, out string permission)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal) && policyName.Length > Prefix.Length)
        {
            permission = policyName[Prefix.Length..];
            return true;
        }
        permission = string.Empty;
        return false;
    }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(PermissionPolicy.ClaimType, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Builds "perm:*" policies on demand; unknown names fall through to the default provider.</summary>
public sealed class PermissionPolicyProvider(
    Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (PermissionPolicy.TryParse(policyName, out var permission))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }
}
