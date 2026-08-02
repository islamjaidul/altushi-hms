using System.Reflection;
using System.Text.RegularExpressions;
using Hms.Hr.Screens;
using Hms.Shell;
using Hms.Web;

namespace Hms.Architecture.Tests;

/// <summary>
/// The role matrix renders from the permission catalogue, and the superuser role is granted from it.
/// Both properties depend on the catalogue actually containing what the product enforces.
/// <para>
/// The defect these exist for: <c>System Admin</c> was seeded with two of the eleven HR permissions,
/// so the administrator saw four of eight sidebar entries and no create buttons. Nothing failed. The
/// nav filter and the view guards were correct; the grant was not, and no check compared the two.
/// </para>
/// </summary>
public class PermissionCatalogTests
{
    private static PermissionCatalog ErpCatalog() =>
        PermissionCatalog.FromPolicyConstants(typeof(Perm), typeof(PlatformPerm), typeof(HrPerm));

    private static PermissionCatalog HrmCatalog() =>
        PermissionCatalog.FromPolicyConstants(typeof(HrPerm), typeof(PlatformPerm));

    [Fact]
    public void Every_authorize_policy_in_the_source_is_in_the_erp_catalogue()
    {
        var known = ErpCatalog().Claims.ToHashSet();
        var constants = PolicyConstants();
        var missing = new List<string>();

        foreach (var file in RazorPageModels())
        {
            foreach (Match m in Regex.Matches(
                         File.ReadAllText(file), @"\[Authorize\(Policy = \w*Perm\.(\w+)\)\]"))
            {
                var name = m.Groups[1].Value;
                if (!constants.TryGetValue(name, out var claim)) continue;
                if (!known.Contains(claim))
                    missing.Add($"{Path.GetFileName(file)} enforces {claim}, which no host declares");
            }
        }

        Assert.True(missing.Count == 0,
            "A page is protected by a permission the role matrix cannot show or grant, so nobody "
            + "can be given access to it from inside the product:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_hrm_catalogue_ships_no_clinical_permission()
    {
        // The commercial boundary and the security boundary are the same list (ADR-0026). If a
        // clinical claim were grantable here, an HRM customer could hand someone a permission for a
        // module their licence does not include.
        var clinical = HrmCatalog().Claims
            .Where(c => !c.StartsWith("hr.") && !c.StartsWith("admin."))
            .ToList();

        Assert.True(clinical.Count == 0,
            "The HRM SKU's catalogue contains permissions outside HR and platform administration: "
            + string.Join(", ", clinical));
    }

    [Fact]
    public void Every_hr_permission_is_grantable_on_the_hrm_host()
    {
        var catalogue = HrmCatalog().Claims.ToHashSet();
        var missing = HrPerm.Claim.All.Where(c => !catalogue.Contains(c)).ToList();

        Assert.True(missing.Count == 0,
            "These HR permissions exist and cannot be granted from any screen — which is how an "
            + "administrator ends up unable to repair their own access:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_superuser_grant_is_exactly_the_hrm_catalogue()
    {
        // HrSeed grants the System Admin role `catalog.Claims`. That is only a full grant if the
        // catalogue is itself complete, so the two directions are asserted separately: this one
        // proves no prefixed constant is missing from Claim.All, the test above proves the reverse.
        var catalogue = HrmCatalog().Claims.ToHashSet();
        var declared = HrPerm.Claim.All.Concat(PlatformPerm.Claim.All).ToHashSet();

        var undeclared = catalogue.Except(declared).ToList();
        Assert.True(undeclared.Count == 0,
            "These permissions exist as [Authorize] policies but are absent from the Claim.All "
            + "lists, so nav entries and Can(...) calls cannot reference them:\n  "
            + string.Join("\n  ", undeclared));
    }

    [Fact]
    public void Every_catalogue_entry_has_a_group_and_a_label()
    {
        var bad = ErpCatalog().All
            .Where(p => string.IsNullOrWhiteSpace(p.Group) || string.IsNullOrWhiteSpace(p.Label))
            .Select(p => p.Claim)
            .ToList();

        Assert.True(bad.Count == 0, "Unlabelled permissions: " + string.Join(", ", bad));
    }

    [Fact]
    public void The_nav_registries_only_reference_permissions_the_catalogue_knows()
    {
        // Nav takes the bare claim. A nav entry naming a claim nothing grants is invisible forever,
        // which reads as a missing feature rather than as a typo.
        var known = ErpCatalog().Claims.ToHashSet();
        var orphans = ModuleNav.Composed
            .Where(n => !known.Contains(n.Permission))
            .Select(n => $"{n.Title} ({n.Url}) needs {n.Permission}")
            .ToList();

        Assert.True(orphans.Count == 0,
            "Sidebar entries gated on a permission no role can be granted:\n  "
            + string.Join("\n  ", orphans));
    }

    private static Dictionary<string, string> PolicyConstants()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in new[] { typeof(Perm), typeof(PlatformPerm), typeof(HrPerm) })
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!f.IsLiteral || f.GetRawConstantValue() is not string v) continue;
            if (v.StartsWith("perm:", StringComparison.Ordinal))
                map[f.Name] = v["perm:".Length..];
        }
        return map;
    }

    private static IEnumerable<string> RazorPageModels()
    {
        var root = FindRepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cshtml.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
