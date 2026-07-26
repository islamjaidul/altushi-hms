using Hms.Kernel.Auth;

namespace Hms.Kernel.Tests;

// ADR-0016/0019: the menu tree = f(permissions ∩ entitlements). UI hiding is never the
// control (G10) — but the nav must not show what the user cannot open (05 §2: Rina sees 4 items).
public class NavComposerTests
{
    private static readonly NavItem[] Registry =
    [
        new("Registration", "New Patient", "/registration/new", "registration.create"),
        new("Registration", "Patient Directory", "/registration", "registration.read"),
        new("Billing", "OPD Invoice", "/billing/opd", "billing.invoice.create"),
        new("Billing", "Day-Close", "/billing/day-close", "billing.session.close"),
        new("Lis", "Work Board", "/lis/board", "lis.worklist.read"),
    ];

    [Fact]
    public void Nav_shows_only_permitted_items()
    {
        var nav = NavComposer.Compose(Registry,
            permissions: ["registration.create", "registration.read"],
            enabledModules: ["Registration", "Billing", "Lis"]);

        Assert.Equal(["Registration"], nav.Select(g => g.Module).Distinct());
        Assert.Equal(2, nav.Sum(g => g.Items.Count));
    }

    [Fact]
    public void Nav_hides_unentitled_modules_even_when_permitted()
    {
        var nav = NavComposer.Compose(Registry,
            permissions: ["lis.worklist.read", "registration.read"],
            enabledModules: ["Registration"]);          // LIS not licensed (ADR-0016)

        Assert.DoesNotContain(nav, g => g.Module == "Lis");
        Assert.Contains(nav, g => g.Module == "Registration");
    }

    [Fact]
    public void Full_permissions_and_entitlements_show_everything_grouped()
    {
        var nav = NavComposer.Compose(Registry,
            permissions: Registry.Select(i => i.Permission).ToArray(),
            enabledModules: ["Registration", "Billing", "Lis"]);

        Assert.Equal(3, nav.Count);
        Assert.Equal(5, nav.Sum(g => g.Items.Count));
    }
}

public class PermissionPolicyTests
{
    [Theory]
    [InlineData("perm:billing.invoice.create", "billing.invoice.create", true)]
    [InlineData("perm:registration.read", "registration.read", true)]
    [InlineData("registration.read", null, false)]     // missing prefix → not ours
    [InlineData("perm:", null, false)]                 // empty permission
    public void Policy_name_parsing(string policyName, string? expected, bool ok)
    {
        var parsed = PermissionPolicy.TryParse(policyName, out var permission);
        Assert.Equal(ok, parsed);
        if (ok) Assert.Equal(expected, permission);
    }
}
