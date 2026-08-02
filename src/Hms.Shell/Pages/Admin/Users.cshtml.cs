using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Shell.Pages.Admin;

public sealed record UserRow(long Id, string Username, string DisplayName, string Roles, bool Active, bool LockedOut);
public sealed record RoleRow(long Id, string Name, bool System, int Users, IReadOnlyList<string> Permissions);

/// <summary>
/// §5 M21 [M]: user management and role-based access, both editable. §12's matrix is data —
/// granting a permission here changes the sidebar and the endpoint policy together, because
/// both read the same claim. Nobody is deleted: deactivation keeps the audit trail whole (§8 N5).
/// <para>
/// Moved out of <c>src/Hms.Web</c> in spec 0036. It had been the ERP host's alone, which meant the
/// standalone HRM SKU shipped with no way to create a second login — the one screen that could have
/// repaired a wrongly-seeded role was the screen that SKU did not have.
/// </para>
/// </summary>
[Authorize(Policy = PlatformPerm.UsersManage)]
public class UsersModel(
    IPlatformTx tx,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    PermissionCatalog catalog,
    TimeProvider clock) : HmsPageModel
{
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? DisplayName { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public string? RoleName { get; set; }
    [BindProperty] public string? NewRoleName { get; set; }
    [BindProperty] public string? CopyFromRole { get; set; }

    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public IReadOnlyList<RoleRow> Roles { get; private set; } = [];
    public PermissionCatalog Catalog => catalog;

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

            Roles = roles.Select(r => new RoleRow(r.Id, r.Name ?? "—", r.System,
                userRoles.Count(ur => ur.RoleId == r.Id),
                permissions.Where(p => p.RoleId == r.Id).Select(p => p.Value).ToList()))
                .ToList();
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

    /// <summary>
    /// A new role, optionally starting from an existing one's grants. Copying matters more than it
    /// looks: the alternative is an empty role, and an empty role hands its holder a product with an
    /// empty sidebar — which reads as a broken install rather than as a permission problem.
    /// </summary>
    public async Task<IActionResult> OnPostCreateRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRoleName))
        { await LoadAsync(); Fail("Give the role a name."); return Page(); }

        var name = NewRoleName.Trim();
        if (await roleManager.RoleExistsAsync(name))
        { await LoadAsync(); Fail($"A role called {name} already exists."); return Page(); }

        var role = new AppRole { Name = name, System = false };
        var created = await roleManager.CreateAsync(role);
        if (!created.Succeeded)
        {
            await LoadAsync();
            Fail(string.Join(" ", created.Errors.Select(e => e.Description)));
            return Page();
        }

        var copied = 0;
        if (!string.IsNullOrWhiteSpace(CopyFromRole))
            copied = await tx.RunAsync(async s =>
            {
                var source = await s.Auth.Roles.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Name == CopyFromRole);
                if (source is null) return 0;

                var grants = await s.Auth.Permissions.AsNoTracking()
                    .Where(p => p.RoleId == source.Id).ToListAsync();
                foreach (var g in grants)
                    s.Auth.Permissions.Add(new Permission
                    {
                        RoleId = role.Id, Module = g.Module, Action = g.Action,
                    });

                await s.Auth.SaveChangesAsync();
                await AuditAsync(s, "role.create", "adm.role", role.Id, new { name, copiedFrom = CopyFromRole });
                return grants.Count;
            });
        else
            await tx.RunAsync(s => AuditAsync(s, "role.create", "adm.role", role.Id, new { name }));

        Toast(copied > 0
            ? $"Role {name} created with {copied} permission(s) copied from {CopyFromRole}"
            : $"Role {name} created — grant it permissions below", "admin_panel_settings");
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
        });
        // Deactivation must also kill the live session, not only the next sign-in (ADR-0019).
        if (await userManager.FindByIdAsync(id.ToString()) is { } affected)
            await userManager.UpdateSecurityStampAsync(affected);
        Toast("Account updated", "manage_accounts");
        return Redirect("/admin/users");
    }

    /// <summary>
    /// Moves an account to a different role. Without this the only way to correct a mis-assigned
    /// login was to deactivate it and make another, which leaves two names on the audit trail for
    /// one person.
    /// </summary>
    public async Task<IActionResult> OnPostAssignRoleAsync(long id, string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        { await LoadAsync(); Fail("Pick a role."); return Page(); }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) { await LoadAsync(); Fail("No such account."); return Page(); }

        var existing = await userManager.GetRolesAsync(user);
        if (existing.Count > 0) await userManager.RemoveFromRolesAsync(user, existing);
        await userManager.AddToRoleAsync(user, roleName);
        await userManager.UpdateSecurityStampAsync(user);

        await tx.RunAsync(s => AuditAsync(s, "user.role.assign", "adm.user", id,
            new { user.UserName, from = string.Join(",", existing), to = roleName }));

        Toast($"{user.DisplayName} is now {roleName}", "manage_accounts");
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

        await tx.RunAsync(s => AuditAsync(s, "user.password.reset", "adm.user", id, new { user.UserName }));

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

        // A permission this host does not ship would sit in the database enforcing nothing, and
        // would then show up in the matrix as a row nobody can explain.
        if (!catalog.Claims.Contains(permission))
        { await LoadAsync(); Fail($"This installation has no permission called {permission}."); return Page(); }

        var roleName = await tx.RunAsync(async s =>
        {
            var existing = await s.Auth.Permissions.FirstOrDefaultAsync(
                p => p.RoleId == roleId && p.Module == parts[0] && p.Action == parts[1]);

            if (grant && existing is null)
                s.Auth.Permissions.Add(new Permission { RoleId = roleId, Module = parts[0], Action = parts[1] });
            else if (!grant && existing is not null)
                s.Auth.Permissions.Remove(existing);

            await s.Auth.SaveChangesAsync();
            await AuditAsync(s, grant ? "role.grant" : "role.revoke", "adm.permission", roleId,
                new { roleId, permission });

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

    private async Task AuditAsync(PlatformScope s, string action, string entity, long entityId, object after)
    {
        s.Kernel.AuditEvents.Add(new AuditEvent
        {
            BranchId = BranchId, At = clock.GetUtcNow(), ActorId = ActorId,
            ActorNameSnapshot = ActorName, Action = action,
            Entity = entity, EntityId = entityId,
            After = System.Text.Json.JsonSerializer.Serialize(after),
            CorrelationId = Guid.NewGuid(), Tier = 2,
        });
        await s.Kernel.SaveChangesAsync();
    }
}
