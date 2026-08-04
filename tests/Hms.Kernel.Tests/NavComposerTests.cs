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

    // ---- spec 0043: grouping is by the label the operator reads, not by module ----

    /// <summary>
    /// The shape that produced the defect: one module leads with an item labelled for a
    /// *different* group. Grouping by module and titling from Items[0] gave that module's
    /// remaining labels away.
    /// </summary>
    private static readonly NavItem[] Mixed =
    [
        new("Registration", "New Patient", "/registration/new", "p", "i", "Front Desk"),
        new("Ipd", "Help Desk", "/frontdesk", "p", "i", "Front Desk"),
        new("Ipd", "Ward Board", "/ipd/board", "p", "i", "Indoor (IPD)"),
        new("Ipd", "Certificates", "/ipd/certificates", "p", "i", "Indoor (IPD)"),
    ];

    [Fact]
    public void A_group_label_is_never_shown_twice()
    {
        var nav = NavComposer.Compose(Mixed, ["p"], ["Registration", "Ipd"]);

        Assert.Equal(nav.Select(g => g.Label).Distinct(), nav.Select(g => g.Label));
    }

    [Fact]
    public void An_items_own_label_survives_a_module_that_leads_with_another()
    {
        var nav = NavComposer.Compose(Mixed, ["p"], ["Registration", "Ipd"]);

        // Before the fix both of these failed: every Ipd item landed under "Front Desk".
        var indoor = Assert.Single(nav, g => g.Label == "Indoor (IPD)");
        Assert.Equal(["Ward Board", "Certificates"], indoor.Items.Select(i => i.Title));

        var front = Assert.Single(nav, g => g.Label == "Front Desk");
        Assert.Equal(["New Patient", "Help Desk"], front.Items.Select(i => i.Title));
    }

    [Fact]
    public void Groups_keep_registry_order()
        => Assert.Equal(
            ["Front Desk", "Indoor (IPD)"],
            NavComposer.Compose(Mixed, ["p"], ["Registration", "Ipd"]).Select(g => g.Label));

    [Fact]
    public void Merging_two_modules_into_one_label_still_respects_entitlement()
    {
        // Ipd unlicensed: "Front Desk" must survive with the Registration item only, never
        // gaining Help Desk because it shares the heading (ADR-0016 — merging is cosmetic).
        var nav = NavComposer.Compose(Mixed, ["p"], ["Registration"]);

        var front = Assert.Single(nav);
        Assert.Equal("Front Desk", front.Label);
        Assert.Equal(["New Patient"], front.Items.Select(i => i.Title));
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
