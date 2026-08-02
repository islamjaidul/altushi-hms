using Microsoft.EntityFrameworkCore;

namespace Hms.Architecture.Tests;

/// <summary>
/// Spec 0039 WP5 (AUD-ARCH-01), AC6: branch isolation is structural. Every entity that carries
/// a <c>BranchId</c> must have a global query filter, so a query cannot omit its branch
/// predicate — it can only opt out loudly with <c>IgnoreQueryFilters()</c>. This fails the
/// moment someone adds a branch-scoped entity to a context that forgot
/// <c>ApplyBranchIsolation</c>, which is exactly how the constant-branch defect was born.
/// </summary>
public class BranchIsolationTests
{
    public static readonly TheoryData<string> Contexts = new(
        "Hms.Appointments.Data.ApptDbContextFactory, Hms.Appointments",
        "Hms.Billing.Data.BillDbContextFactory, Hms.Billing",
        "Hms.Diagnostics.Data.DiagDbContextFactory, Hms.Diagnostics",
        "Hms.Registration.Data.RegDbContextFactory, Hms.Registration",
        "Hms.Emr.Data.EmrDbContextFactory, Hms.Emr",
        "Hms.Ipd.Data.IpdDbContextFactory, Hms.Ipd",
        "Hms.Pharmacy.Data.PharmDbContextFactory, Hms.Pharmacy",
        "Hms.Lis.Data.LisDbContextFactory, Hms.Lis",
        "Hms.Ot.Data.OtDbContextFactory, Hms.Ot",
        "Hms.Radiology.Data.RadiologyDbContextFactory, Hms.Radiology",
        "Hms.Notifications.Data.NotifDbContextFactory, Hms.Notifications",
        "Hms.Admin.Data.AdmDbContextFactory, Hms.Admin",
        "Hms.Kernel.Data.KernelDbContextFactory, Hms.Kernel",
        "Hms.Hr.Data.HrDbContextFactory, Hms.Hr");

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_branch_scoped_entity_is_query_filtered(string factoryType)
    {
        var type = Type.GetType(factoryType, throwOnError: true)!;
        var factory = Activator.CreateInstance(type)!;
        using var ctx = (DbContext)type.GetMethod("CreateDbContext")!
            .Invoke(factory, [Array.Empty<string>()])!;

        var unfiltered = ctx.Model.GetEntityTypes()
            .Where(t => t.FindProperty("BranchId") is { ClrType: var c } && c == typeof(long)
                        && t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.FullName)
            .OrderBy(n => n)
            .ToList();

        Assert.True(unfiltered.Count == 0,
            $"{ctx.GetType().Name}: these branch-scoped entities have no branch query filter "
            + "(ApplyBranchIsolation missing or bypassed):\n  " + string.Join("\n  ", unfiltered));
    }
}
