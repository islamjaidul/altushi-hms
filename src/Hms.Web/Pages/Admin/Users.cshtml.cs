using Hms.Kernel.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record UserRow(long Id, string Username, string DisplayName, string Roles, bool Active, bool LockedOut);
public sealed record RoleRow(long Id, string Name, int Users, IReadOnlyList<string> Permissions);

/// <summary>
/// §5 M21 [M]: user management and role-based access, both editable. §12's matrix is data —
/// granting a permission here changes the sidebar and the endpoint policy together, because
/// both read the same claim. Nobody is deleted: deactivation keeps the audit trail whole (§8 N5).
/// </summary>
[Authorize(Policy = Perm.AdminUsersManage)]
public class UsersModel(
    HmsTx tx, UserManager<AppUser> userManager, TimeProvider clock) : HmsPageModel
{
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? DisplayName { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public string? RoleName { get; set; }

    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public IReadOnlyList<RoleRow> Roles { get; private set; } = [];
    /// <summary>Every permission any role holds, so the matrix has columns to toggle.</summary>
    public IReadOnlyList<string> AllPermissions { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var now = clock.GetUtcNow();
        await tx.RunAsync(async s =>
        {
            var users = await s.Auth.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
            var roles = await s.Auth.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
            var userRoles = await s.Auth.UserRoles.AsNoTracking().ToListAsync();
            var permissions = await s.Auth.Permissions.AsNoTracking().ToListAsync();
            var roleNameById = roles.ToDictionary(r => r.Id, r => r.Name ?? "—");

            Users = users.Select(u => new UserRow(
                u.Id, u.UserName ?? "—", u.DisplayName,
                string.Join(", ", userRoles.Where(ur => ur.UserId == u.Id)
                    .Select(ur => roleNameById.GetValueOrDefault(ur.RoleId, "—"))),
                u.Active, u.LockoutEnd is { } end && end > now)).ToList();

            Roles = roles.Select(r => new RoleRow(r.Id, r.Name ?? "—",
                userRoles.Count(ur => ur.RoleId == r.Id),
                permissions.Where(p => p.RoleId == r.Id).Select(p => p.Value).OrderBy(v => v).ToList()))
                .ToList();

            // The registry is the authority on what permissions exist — a role cannot be granted
            // something no screen is protected by.
            AllPermissions = ModuleNav.Registry.Select(i => i.Permission)
                .Concat(permissions.Select(p => p.Value))
                .Distinct().OrderBy(v => v).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(DisplayName))
        { await LoadAsync(); Fail("Username and full name are both required."); return Page(); }
        if (string.IsNullOrWhiteSpace(Password) || Password.Length < 8)
        { await LoadAsync(); Fail("Set a password of at least 8 characters."); return Page(); }
        if (string.IsNullOrWhiteSpace(RoleName))
        { await LoadAsync(); Fail("Every account needs a role — that is what decides its screens."); return Page(); }

        var user = new AppUser { UserName = Username.Trim(), DisplayName = DisplayName.Trim() };
        var created = await userManager.CreateAsync(user, Password);
        if (!created.Succeeded)
        {
            await LoadAsync();
            Fail(string.Join(" ", created.Errors.Select(e => e.Description)));
            return Page();
        }
        await userManager.AddToRoleAsync(user, RoleName);

        Toast($"{DisplayName} can sign in as {RoleName}", "person_add");
        return Redirect("/admin/users");
    }

    /// <summary>Deactivation, never deletion — receipts carry this name forever (§8 N5).</summary>
    public async Task<IActionResult> OnPostToggleAsync(long id)
    {
        if (id == ActorId)
        {
            await LoadAsync();
            Fail("You cannot deactivate the account you are signed in with.");
            return Page();
        }
        await tx.RunAsync(async s =>
        {
            var u = await s.Auth.Users.SingleAsync(x => x.Id == id);
            u.Active = !u.Active;
            u.LockoutEnd = u.Active ? null : DateTimeOffset.MaxValue;   // deactivated = cannot sign in
            await s.Auth.SaveChangesAsync();
            return 0;
        });
        // Deactivation must also kill the live session, not only the next sign-in (ADR-0019).
        if (await userManager.FindByIdAsync(id.ToString()) is { } affected)
            await userManager.UpdateSecurityStampAsync(affected);
        Toast("Account updated", "manage_accounts");
        return Redirect("/admin/users");
    }

    /// <summary>
    /// RUNBOOK §9 step 2 needs this: at go-live every credential that stays is rotated and every
    /// demo account that does not is deactivated. Rehearsing the runbook (spec 0023) found the
    /// procedure asked for a rotation the product could not perform.
    ///
    /// Identity's reset bumps the security stamp, so the holder's live session dies within the
    /// revalidation window (ADR-0019 amendment) rather than at their next voluntary sign-in.
    /// The new password is never audited — only the fact of the reset.
    /// </summary>
    public async Task<IActionResult> OnPostResetPasswordAsync(long id, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        { await LoadAsync(); Fail("Set a password of at least 8 characters."); return Page(); }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) { await LoadAsync(); Fail("No such account."); return Page(); }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!reset.Succeeded)
        {
            await LoadAsync();
            Fail(string.Join(" ", reset.Errors.Select(e => e.Description)));
            return Page();
        }

        await tx.RunAsync(async s =>
        {
            s.Kernel.AuditEvents.Add(new Hms.Kernel.Data.AuditEvent
            {
                BranchId = BranchId, At = clock.GetUtcNow(), ActorId = ActorId,
                ActorNameSnapshot = ActorName, Action = "user.password.reset",
                Entity = "adm.user", EntityId = id,
                After = System.Text.Json.JsonSerializer.Serialize(new { user.UserName }),
                CorrelationId = Guid.NewGuid(), Tier = 2,
            });
            await s.Kernel.SaveChangesAsync();
            return 0;
        });

        Toast($"{user.DisplayName}'s password is changed — they are signed out now", "key");
        return Redirect("/admin/users");
    }

    /// <summary>
    /// §12 as data. The change lands on the role, so it applies to everyone holding it. It
    /// takes effect within the security-stamp revalidation window (ADR-0019 amendment): the
    /// stamp bump below invalidates every holder's cookie, and the next check re-runs
    /// PermissionClaimsFactory against the updated matrix.
    /// </summary>
    public async Task<IActionResult> OnPostPermissionAsync(long roleId, string permission, bool grant)
    {
        var parts = permission.Split('.', 2);
        if (parts.Length != 2) { await LoadAsync(); Fail("Malformed permission."); return Page(); }

        var roleName = await tx.RunAsync(async s =>
        {
            var existing = await s.Auth.Permissions.FirstOrDefaultAsync(
                p => p.RoleId == roleId && p.Module == parts[0] && p.Action == parts[1]);

            if (grant && existing is null)
                s.Auth.Permissions.Add(new Permission { RoleId = roleId, Module = parts[0], Action = parts[1] });
            else if (!grant && existing is not null)
                s.Auth.Permissions.Remove(existing);

            await s.Auth.SaveChangesAsync();

            s.Kernel.AuditEvents.Add(new Hms.Kernel.Data.AuditEvent
            {
                BranchId = BranchId, At = clock.GetUtcNow(), ActorId = ActorId,
                ActorNameSnapshot = ActorName, Action = grant ? "role.grant" : "role.revoke",
                Entity = "adm.permission", EntityId = roleId,
                After = System.Text.Json.JsonSerializer.Serialize(new { roleId, permission }),
                CorrelationId = Guid.NewGuid(), Tier = 2,
            });
            await s.Kernel.SaveChangesAsync();
            return (await s.Auth.Roles.AsNoTracking()
                .Where(r => r.Id == roleId).Select(r => r.Name).FirstOrDefaultAsync()) ?? "";
        });

        // Bump every holder's security stamp so the change is enforced mid-session.
        if (roleName.Length > 0)
            foreach (var holder in await userManager.GetUsersInRoleAsync(roleName))
                await userManager.UpdateSecurityStampAsync(holder);

        Toast($"{(grant ? "Granted" : "Revoked")} {permission} — enforced within minutes", "admin_panel_settings");
        return Redirect("/admin/users");
    }
}
