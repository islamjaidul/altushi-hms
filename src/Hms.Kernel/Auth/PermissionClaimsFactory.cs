using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hms.Kernel.Auth;

/// <summary>
/// Stamps the user's effective permission set ("perm" claims) into the cookie principal at
/// sign-in — the single source both endpoint policies (G10) and nav composition (05 §2) read.
/// Permission edits take effect at next sign-in; revocation is the session table (ADR-0019).
/// </summary>
public sealed class PermissionClaimsFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options,
    AuthDbContext db)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var roleIds = await db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var perms = await db.Permissions
            .Where(p => roleIds.Contains(p.RoleId))
            .Select(p => new { p.Module, p.Action })
            .Distinct()
            .ToListAsync();

        foreach (var p in perms)
            identity.AddClaim(new Claim(PermissionPolicy.ClaimType, $"{p.Module}.{p.Action}"));

        identity.AddClaim(new Claim("display_name", user.DisplayName));
        return identity;
    }
}
