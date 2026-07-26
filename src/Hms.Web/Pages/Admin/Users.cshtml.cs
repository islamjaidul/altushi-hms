using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record UserRow(long Id, string Username, string DisplayName, string Roles, bool Active, bool LockedOut);
public sealed record RoleRow(string Name, int Users, IReadOnlyList<string> Permissions);

/// <summary>
/// §12 as data: roles carry `module.action` grants, and the same grants shape both the sidebar
/// and every endpoint policy. Deactivation rather than deletion keeps the audit trail whole (§8 N5).
/// </summary>
[Authorize(Policy = Perm.AdminUsersManage)]
public class UsersModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public IReadOnlyList<RoleRow> Roles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var now = clock.GetUtcNow();
        (Users, Roles) = await tx.RunAsync(async s =>
        {
            var users = await s.Auth.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
            var roles = await s.Auth.Roles.AsNoTracking().ToListAsync();
            var userRoles = await s.Auth.UserRoles.AsNoTracking().ToListAsync();
            var permissions = await s.Auth.Permissions.AsNoTracking().ToListAsync();

            var roleNameById = roles.ToDictionary(r => r.Id, r => r.Name ?? "—");

            var userRows = users.Select(u => new UserRow(
                u.Id, u.UserName ?? "—", u.DisplayName,
                string.Join(", ", userRoles.Where(ur => ur.UserId == u.Id)
                    .Select(ur => roleNameById.GetValueOrDefault(ur.RoleId, "—"))),
                u.Active,
                u.LockoutEnd is { } end && end > now)).ToList();

            var roleRows = roles.Select(r => new RoleRow(
                r.Name ?? "—",
                userRoles.Count(ur => ur.RoleId == r.Id),
                permissions.Where(p => p.RoleId == r.Id)
                    .Select(p => p.Value).OrderBy(v => v).ToList())).ToList();

            return ((IReadOnlyList<UserRow>)userRows, (IReadOnlyList<RoleRow>)roleRows);
        });
    }
}
