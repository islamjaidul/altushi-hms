using System.Reflection;
using Hms.Hr.Screens;

namespace Hms.Architecture.Tests;

/// <summary>
/// The HRM SKU exists because HR depends on nothing clinical (ADR-0025). That is a property of the
/// build, not a good intention, so it is asserted here: the day someone adds
/// <c>ProjectReference Hms.Billing</c> to HR to borrow one helper, the standalone product silently
/// stops being standalone. This test makes that a build failure instead of a discovery at packaging
/// time.
/// </summary>
public class HrIndependenceTests
{
    private static readonly string[] ForbiddenModules =
    [
        "Hms.Registration", "Hms.Appointments", "Hms.Billing", "Hms.Diagnostics", "Hms.Lis",
        "Hms.Dashboard", "Hms.Admin", "Hms.Notifications", "Hms.Pharmacy", "Hms.Ipd",
        "Hms.Emr", "Hms.Ot", "Hms.Radiology", "Hms.Web",
    ];

    [Theory]
    [InlineData("Hms.Hr")]
    [InlineData("Hms.Hr.Contracts")]
    [InlineData("Hms.Hr.Screens")]
    public void Hr_references_no_other_module_and_no_host(string assemblyName)
    {
        var referenced = Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        var violations = referenced
            .Where(r => ForbiddenModules.Any(f =>
                r.Equals(f, StringComparison.Ordinal)
                || r.StartsWith(f + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.True(violations.Count == 0,
            $"{assemblyName} references {string.Join(", ", violations)}. HR must depend on the "
            + "kernel and the shell only, or the standalone HRM SKU cannot be built (ADR-0025).");
    }

    /// <summary>
    /// Both forms of every HR permission are written out literally so
    /// <c>eng/check-lifecycle-traceability.sh</c> can parse them. Written twice means they can drift,
    /// so the relationship is asserted rather than trusted.
    /// </summary>
    [Fact]
    public void Every_prefixed_permission_matches_its_bare_claim()
    {
        var prefixed = typeof(HrPerm)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        var bare = typeof(HrPerm.Claim)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.NotEmpty(prefixed);
        Assert.Equal(bare.Count, prefixed.Count);

        foreach (var (name, policy) in prefixed)
        {
            Assert.True(bare.ContainsKey(name), $"HrPerm.{name} has no matching HrPerm.Claim.{name}.");
            Assert.Equal("perm:" + bare[name], policy);
        }
    }

    /// <summary>
    /// Every HR claim must be reachable through the module's own claim list, so a permission cannot
    /// be added to the policy constants and then forgotten everywhere a role is granted.
    /// </summary>
    [Fact]
    public void Claim_All_lists_every_declared_claim()
    {
        var declared = typeof(HrPerm.Claim)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(declared.OrderBy(x => x, StringComparer.Ordinal),
            HrPerm.Claim.All.OrderBy(x => x, StringComparer.Ordinal));
    }
}
