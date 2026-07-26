using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// Development/demo seed: branch, §12 role templates with module.action permissions, and the
/// demo cast (07 §1). Idempotent; runs only when Seed:DevUsers=true (never in production).
/// The full 90-day history generator is the S6 deliverable — this is just enough for login+nav.
/// </summary>
public static class DevSeed
{
    private static readonly Dictionary<string, string[]> Roles = new()
    {
        ["Receptionist"] =
            ["registration.create", "registration.read", "appointments.read", "appointments.create"],
        ["Billing Operator"] =
            ["registration.read", "billing.invoice.create", "billing.receipt.create",
             "billing.session.open", "billing.session.close", "diagnostics.order.create"],
        ["Lab Technologist"] =
            ["lis.worklist.read", "lis.sample.collect", "lis.result.enter"],
        ["Pathologist"] =
            ["lis.worklist.read", "lis.result.verify"],
        ["Billing Supervisor"] =
            ["registration.read", "billing.invoice.create", "billing.receipt.create",
             "billing.session.close", "admin.approvals.decide"],
        ["Admin"] =
            ["admin.users.manage", "admin.audit.read", "admin.approvals.decide",
             "admin.masters.manage", "notifications.read"],
        ["MD"] =
            ["dashboard.read", "admin.approvals.decide", "admin.audit.read"],
    };

    private static readonly (string User, string Display, string Role)[] Cast =
    [
        ("jashim", "Jashim Uddin", "Receptionist"),
        ("rasel", "Rasel Ahmed", "Billing Operator"),
        ("ripon", "Ripon Das", "Lab Technologist"),
        ("farhana", "Dr. Farhana Rahman", "Pathologist"),
        ("shahid", "Shahid Alam", "Billing Supervisor"),
        ("admin", "System Admin", "Admin"),
        ("md", "Dr. Chairman", "MD"),
    ];

    public const string DevPassword = "Demo#1234";   // on the demo card (07 §1)

    public static async Task RunAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Seed:DevUsers", false)) return;

        var kdb = sp.GetRequiredService<KernelDbContext>();
        if (!await kdb.Branches.AnyAsync())
        {
            kdb.Branches.Add(new Branch { Code = "MAIN", Name = "Altushi General Hospital" });
            await kdb.SaveChangesAsync();
        }

        var roleMgr = sp.GetRequiredService<RoleManager<AppRole>>();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var adb = sp.GetRequiredService<AuthDbContext>();

        foreach (var (roleName, perms) in Roles)
        {
            var role = await roleMgr.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new AppRole { Name = roleName, System = true };
                (await roleMgr.CreateAsync(role)).ThrowIfFailed();
            }
            foreach (var p in perms)
            {
                var parts = p.Split('.', 2);   // "billing.invoice.create" → module "billing", action "invoice.create"
                if (!await adb.Permissions.AnyAsync(x =>
                        x.RoleId == role.Id && x.Module == parts[0] && x.Action == parts[1]))
                    adb.Permissions.Add(new Permission { RoleId = role.Id, Module = parts[0], Action = parts[1] });
            }
        }
        await adb.SaveChangesAsync();

        foreach (var (userName, display, roleName) in Cast)
        {
            if (await userMgr.FindByNameAsync(userName) is not null) continue;
            var user = new AppUser { UserName = userName, DisplayName = display };
            (await userMgr.CreateAsync(user, DevPassword)).ThrowIfFailed();
            (await userMgr.AddToRoleAsync(user, roleName)).ThrowIfFailed();
        }
    }

    private static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
