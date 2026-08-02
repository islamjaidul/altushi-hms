using System.Reflection;

namespace Hms.Shell;

/// <summary>
/// Policy names for the capabilities every host has, whatever it sells. Same shape as the ERP's
/// <c>Perm</c> and HR's <c>HrPerm</c>: prefixed constant for <c>[Authorize]</c>, bare claim for
/// <c>Can(...)</c>.
/// </summary>
public static class PlatformPerm
{
    private const string P = "perm:";

    public const string UsersManage = P + "admin.users.manage";
    public const string AuditRead = P + "admin.audit.read";

    public static class Claim
    {
        public const string UsersManage = "admin.users.manage";
        public const string AuditRead = "admin.audit.read";

        public static readonly string[] All = [UsersManage, AuditRead];
    }
}

/// <summary>One grantable permission, as the role matrix renders it.</summary>
/// <param name="Claim">The bare claim — what a <c>Permission</c> row stores and <c>Can</c> takes.</param>
/// <param name="Group">The module it belongs to, for grouping the matrix rows.</param>
/// <param name="Label">What it lets someone do, in a sentence an HR manager can act on.</param>
public sealed record PermissionDescriptor(string Claim, string Group, string Label);

/// <summary>
/// What permissions <b>this host</b> ships. The role matrix renders from it, so it can never offer a
/// permission no screen enforces, nor omit one that exists.
/// <para>
/// Built by reflecting over the policy-constant classes rather than hand-listing, because a
/// hand-list is exactly the thing that goes stale: the seeded <c>System Admin</c> role held two of
/// the eleven HR claims, and the resulting half-empty sidebar looked like a missing feature rather
/// than a wrong grant (spec 0036). Reflection means a permission added tomorrow is in the catalogue,
/// in the matrix, and in the superuser's grant, without anyone remembering three lists.
/// </para>
/// </summary>
public sealed class PermissionCatalog
{
    public IReadOnlyList<PermissionDescriptor> All { get; }

    public PermissionCatalog(IEnumerable<PermissionDescriptor> permissions) =>
        All = permissions
            .GroupBy(p => p.Claim)
            .Select(g => g.First())
            .OrderBy(p => p.Group, StringComparer.Ordinal)
            .ThenBy(p => p.Claim, StringComparer.Ordinal)
            .ToList();

    /// <summary>Every claim this host knows about — what a superuser role is granted.</summary>
    public IReadOnlyList<string> Claims => [.. All.Select(p => p.Claim)];

    public IEnumerable<IGrouping<string, PermissionDescriptor>> ByGroup =>
        All.GroupBy(p => p.Group);

    /// <summary>
    /// Reads the <c>public const string</c> fields of policy-constant classes — the same declarations
    /// <c>[Authorize(Policy = ...)]</c> uses — and strips the <c>perm:</c> prefix. Nested
    /// <c>Claim</c> classes are skipped: they restate the same permissions unprefixed, and counting
    /// them twice would put every permission in the matrix once as itself and once as a duplicate.
    /// </summary>
    public static PermissionCatalog FromPolicyConstants(params Type[] policyClasses)
    {
        var found = new List<PermissionDescriptor>();

        foreach (var type in policyClasses)
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
            if (field.GetRawConstantValue() is not string value) continue;
            if (!value.StartsWith("perm:", StringComparison.Ordinal)) continue;

            found.Add(Describe(value["perm:".Length..]));
        }

        return new PermissionCatalog(found);
    }

    /// <summary>
    /// Friendly names for the module segment. A claim whose module is not listed still renders —
    /// capitalised — rather than being dropped, because a permission missing from this screen is
    /// unfixable from inside the product.
    /// </summary>
    private static readonly Dictionary<string, string> GroupNames = new(StringComparer.Ordinal)
    {
        ["admin"] = "Administration",
        ["hr"] = "HR & Payroll",
        ["dashboard"] = "Dashboard",
        ["registration"] = "Registration",
        ["appointments"] = "Appointments",
        ["billing"] = "Billing",
        ["diagnostics"] = "Diagnostics",
        ["lis"] = "Laboratory",
        ["radiology"] = "Radiology",
        ["emr"] = "Clinical records",
        ["ipd"] = "Inpatient",
        ["ot"] = "Operation theatre",
        ["pharmacy"] = "Pharmacy",
        ["notifications"] = "Notifications",
    };

    /// <summary>
    /// Sentences for the claims whose meaning is not obvious from the string. The rest fall back to
    /// the action words, which read acceptably (<c>hr.roster.manage</c> → "Roster manage") and are at
    /// least never wrong — an invented description would be.
    /// </summary>
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["admin.users.manage"] = "Create logins, set passwords, and change what every role may do",
        ["admin.audit.read"] = "Read the audit trail",
        ["hr.read"] = "See the employee directory, roster and attendance — never pay",
        ["hr.salary.read"] = "See salary figures, pay structures and payslips",
        ["hr.employee.manage"] = "Add and edit employee records",
        ["hr.attendance.review"] = "Review attendance and correct a day, with a reason",
        ["hr.roster.manage"] = "Set shifts and rosters",
        ["hr.leave.apply"] = "Apply for own leave and read own payslip",
        ["hr.leave.recommend"] = "Recommend a team member's leave to HR",
        ["hr.leave.approve"] = "Approve or reject leave",
        ["hr.payroll.run"] = "Generate a payroll run and review its exceptions",
        ["hr.payroll.approve"] = "Approve and lock a payroll run",
        ["hr.policy.manage"] = "Configure org structure, pay components and payroll policy",
    };

    private static PermissionDescriptor Describe(string claim)
    {
        var module = claim.Split('.', 2)[0];
        var group = GroupNames.TryGetValue(module, out var friendly)
            ? friendly
            : char.ToUpperInvariant(module[0]) + module[1..];

        if (Labels.TryGetValue(claim, out var label))
            return new PermissionDescriptor(claim, group, label);

        // "billing.session.close" → "Session close". Plain, and derived from the claim rather than
        // guessed at, so it cannot describe something the permission does not do.
        var words = claim.Split('.').Skip(1).ToArray();
        var fallback = words.Length == 0
            ? claim
            : char.ToUpperInvariant(words[0][0]) + string.Join(' ', words)[1..];

        return new PermissionDescriptor(claim, group, fallback);
    }
}
