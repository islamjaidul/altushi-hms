using System.Reflection;

namespace Hms.Architecture.Tests;

/// <summary>
/// G16 / ADR-0003: a module implementation assembly may reference only Contracts assemblies,
/// the kernel, and the BCL — never another module's implementation. Enforced here at the
/// compiled-metadata level: a violation requires an actual code dependency, which lands in
/// the assembly's referenced-assemblies table.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly string[] Modules =
        ["Registration", "Appointments", "Billing", "Diagnostics", "Lis", "Dashboard", "Admin", "Notifications"];

    private static Assembly Load(string name) => Assembly.Load(name);

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var m in Modules) data.Add(m);
        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_implementation_references_no_other_module_implementation(string module)
    {
        var assembly = Load($"Hms.{module}");
        var forbidden = Modules
            .Where(m => m != module)
            .Select(m => $"Hms.{m}")
            .ToHashSet();

        var offenders = assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null && forbidden.Contains(r.Name))
            .Select(r => r.Name!)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Hms.{module} references module implementation(s): {string.Join(", ", offenders)} — " +
            "modules may only reference Contracts + kernel (ADR-0003).");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_assemblies_reference_no_implementations(string module)
    {
        var assembly = Load($"Hms.{module}.Contracts");
        var offenders = assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null &&
                        r.Name.StartsWith("Hms.", StringComparison.Ordinal) &&
                        !r.Name.EndsWith(".Contracts", StringComparison.Ordinal))
            .Select(r => r.Name!)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Hms.{module}.Contracts references non-contract assembly(ies): {string.Join(", ", offenders)} — " +
            "contracts must stay dependency-free (04 §1).");
    }

    [Fact]
    public void Only_sanctioned_cross_module_contract_references_exist()
    {
        // 04 §1: Appointments→Billing.Contracts, Diagnostics→Billing.Contracts+Lis.Contracts,
        // Lis→Diagnostics.Contracts, Billing→Registration.Contracts (IPatientLookup).
        var sanctioned = new Dictionary<string, string[]>
        {
            ["Appointments"] = ["Hms.Billing.Contracts"],
            ["Diagnostics"] = ["Hms.Billing.Contracts", "Hms.Lis.Contracts"],
            ["Lis"] = ["Hms.Diagnostics.Contracts"],
            ["Billing"] = ["Hms.Registration.Contracts"],
        };

        foreach (var module in Modules)
        {
            var allowed = new HashSet<string>(sanctioned.GetValueOrDefault(module, []))
            {
                $"Hms.{module}.Contracts",
                "Hms.Kernel",
            };

            var offenders = Load($"Hms.{module}").GetReferencedAssemblies()
                .Where(r => r.Name is not null &&
                            r.Name.StartsWith("Hms.", StringComparison.Ordinal) &&
                            !allowed.Contains(r.Name))
                .Select(r => r.Name!)
                .ToList();

            Assert.True(offenders.Count == 0,
                $"Hms.{module} has unsanctioned Hms.* reference(s): {string.Join(", ", offenders)} (04 §1).");
        }
    }
}
