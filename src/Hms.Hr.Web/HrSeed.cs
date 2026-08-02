using Hms.Hr.Screens;
using Hms.Shell;
using Hms.Kernel.Approvals;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Hr.Web;

/// <summary>
/// First-run setup for the HRM SKU: a branch, the §12 roles that touch HR, and the approval policy
/// rows the leave and payroll chains route through.
/// <para>
/// It deliberately seeds <b>no policy numbers</b> — no tax slabs, no PF rates, no leave entitlements.
/// Those are the employer's, and ADR-0027 forbids us inventing Bangladeshi ones. A fresh install
/// therefore refuses to run payroll until someone configures it, and says which policy is missing.
/// That is the honest behaviour; a system that silently computed zero tax would be worse than one
/// that stops.
/// </para>
/// </summary>
public static class HrSeed
{
    /// <summary>Roles from §12's matrix that have any HR column at all.</summary>
    private static readonly (string Role, string[] Perms)[] Roles =
    [
        ("HR Officer", [
            HrPerm.Claim.Read, HrPerm.Claim.SalaryRead, HrPerm.Claim.EmployeeManage,
            HrPerm.Claim.AttendanceReview, HrPerm.Claim.RosterManage,
            HrPerm.Claim.LeaveApply, HrPerm.Claim.LeaveApprove,
            HrPerm.Claim.PayrollRun, HrPerm.Claim.PolicyManage,
        ]),
        ("Department Head", [
            HrPerm.Claim.Read, HrPerm.Claim.AttendanceReview, HrPerm.Claim.RosterManage,
            HrPerm.Claim.LeaveApply, HrPerm.Claim.LeaveRecommend,
        ]),
        // §12 gives Accounts the payroll-post view and the lock approval — never employee management.
        ("Accounts Manager", [
            HrPerm.Claim.Read, HrPerm.Claim.SalaryRead, HrPerm.Claim.PayrollApprove,
        ]),
        ("Managing Director", [
            HrPerm.Claim.Read, HrPerm.Claim.SalaryRead, HrPerm.Claim.PayrollApprove,
        ]),
        // The self-service role every employee gets: apply for own leave, read own payslip. It
        // deliberately does NOT carry hr.read — a colleague's record is not theirs to browse.
        ("Employee", [HrPerm.Claim.LeaveApply]),
    ];

    /// <summary>
    /// The superuser. Its grants are not listed — they are <b>every permission this host ships</b>,
    /// read from the catalogue at startup.
    /// <para>
    /// It was previously listed, and held two of the eleven HR claims. The result was an
    /// administrator who saw four of eight sidebar entries and no "Add employee" button, on an
    /// install whose every automated check passed: the nav filter and the view guards were working
    /// exactly as designed, on a role that was seeded wrong. In a single-tenant product bought by one
    /// employer, the person who installs it is the HR head — a superuser who cannot open payroll is
    /// not a security posture, it is a broken install.
    /// </para>
    /// <para>
    /// Taking the list from the catalogue rather than restating it means a permission added tomorrow
    /// is granted without anyone remembering this file. §12's segregation of duties still holds for
    /// every other role, which is where it does real work.
    /// </para>
    /// </summary>
    public const string SuperuserRole = "System Admin";

    private static readonly (string User, string Display, string Role)[] Cast =
    [
        ("admin", "System Administrator", "System Admin"),
        ("farid", "Farid Ahmed", "HR Officer"),
        ("nasrin", "Nasrin Akter", "Department Head"),
        ("kamal", "Kamal Hossain", "Accounts Manager"),
    ];

    public static async Task RunAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Seed:DevUsers", false)) return;

        var password = config["Seed:Password"] ?? "Demo#1234";
        var kernel = sp.GetRequiredService<KernelDbContext>();

        if (!await kernel.Branches.AnyAsync())
        {
            kernel.Branches.Add(new Branch
            {
                Code = "MAIN",
                Name = config["Org:Name"] ?? "Your Organisation",
            });
            await kernel.SaveChangesAsync();
        }

        var roleMgr = sp.GetRequiredService<RoleManager<AppRole>>();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var auth = sp.GetRequiredService<AuthDbContext>();
        var catalog = sp.GetRequiredService<PermissionCatalog>();

        // The superuser's grants are reconciled on every start, not only on first run. A database
        // seeded before spec 0036 already has the role, so a first-run-only fix would repair nothing
        // on the one install that has the problem — and "run this SQL by hand" is not a fix a
        // customer can apply.
        var wanted = Roles.Append((SuperuserRole, catalog.Claims.ToArray()));

        foreach (var (roleName, perms) in wanted)
        {
            var role = await roleMgr.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new AppRole { Name = roleName, System = true };
                var created = await roleMgr.CreateAsync(role);
                if (!created.Succeeded)
                    throw new InvalidOperationException(
                        $"Could not create role {roleName}: "
                        + string.Join("; ", created.Errors.Select(e => e.Description)));
            }

            foreach (var p in perms)
            {
                var parts = p.Split('.', 2);   // "hr.salary.read" → module "hr", action "salary.read"
                if (!await auth.Permissions.AnyAsync(x =>
                        x.RoleId == role.Id && x.Module == parts[0] && x.Action == parts[1]))
                    auth.Permissions.Add(new Permission
                    {
                        RoleId = role.Id, Module = parts[0], Action = parts[1],
                    });
            }
        }
        await auth.SaveChangesAsync();

        foreach (var (userName, display, roleName) in Cast)
        {
            if (await userMgr.FindByNameAsync(userName) is not null) continue;
            var user = new AppUser { UserName = userName, DisplayName = display };
            var created = await userMgr.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException(
                    $"Could not create user {userName}: "
                    + string.Join("; ", created.Errors.Select(e => e.Description)));
            await userMgr.AddToRoleAsync(user, roleName);
        }

        await SeedApprovalPoliciesAsync(kernel);
    }

    /// <summary>
    /// The two chains §11 marks with ⚿. Both are pure data — the kernel engine needs no HR-specific
    /// code to route, escalate or delegate them.
    /// </summary>
    private static async Task SeedApprovalPoliciesAsync(KernelDbContext kernel)
    {
        var wanted = new[]
        {
            new ApprovalPolicy { Type = "leave-approval", Role = "HR Officer", Tier = 1 },
            new ApprovalPolicy { Type = "leave-approval", Role = "Department Head", Tier = 2 },
            // §12: "Payroll run lock — HR Officer → Accounts Manager / MD".
            new ApprovalPolicy { Type = "payroll-lock", Role = "Accounts Manager", Tier = 1 },
            new ApprovalPolicy { Type = "payroll-lock", Role = "Managing Director", Tier = 2 },
        };

        foreach (var policy in wanted)
        {
            if (await kernel.ApprovalPolicies.AnyAsync(
                    p => p.Type == policy.Type && p.Role == policy.Role))
                continue;
            kernel.ApprovalPolicies.Add(policy);
        }

        await kernel.SaveChangesAsync();
    }
}
