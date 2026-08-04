using Hms.Hr.Screens;
using Hms.Kernel.Auth;
using Hms.Web;

namespace Hms.Architecture.Tests;

/// <summary>
/// Properties of the real shipped nav registries, as opposed to <c>NavComposerTests</c>, which
/// proves the composer against a synthetic one.
/// <para>
/// The defect these exist for (spec 0043): the sidebar rendered <b>FRONT DESK</b> twice and
/// <b>Indoor (IPD)</b> never, because <c>Compose</c> grouped by module while <c>NavGroup.Label</c>
/// read <c>Items[0].GroupLabel</c> — so the Ipd module, which leads with Help Desk ("Front Desk"),
/// silently discarded the label on its seven ward screens. Every unit test passed; only an
/// operator looking at the menu could see it.
/// </para>
/// </summary>
public class NavRegistryTests
{
    /// <summary>Everything a superuser holds — the widest sidebar the product ever renders.</summary>
    private static IReadOnlyList<NavGroup> ComposeAll(IReadOnlyList<NavItem> registry) =>
        NavComposer.Compose(
            registry,
            registry.Select(i => i.Permission).Distinct().ToHashSet(),
            registry.Select(i => i.Module).Distinct().ToHashSet());

    public static TheoryData<string, IReadOnlyList<NavItem>> Registries => new()
    {
        { "ERP", ModuleNav.Registry },
        { "HRM", HrNav.Registry },
    };

    [Theory, MemberData(nameof(Registries))]
    public void No_group_heading_is_rendered_twice(string sku, IReadOnlyList<NavItem> registry)
    {
        var labels = ComposeAll(registry).Select(g => g.Label).ToList();
        var repeated = labels.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(repeated.Count == 0,
            $"{sku} sidebar repeats these headings: {string.Join(", ", repeated)}. "
          + "Two modules may share a heading — the composer merges them — but the same heading "
          + "must never be drawn as two separate blocks.");
    }

    [Theory, MemberData(nameof(Registries))]
    public void Every_declared_group_label_reaches_the_sidebar(string sku, IReadOnlyList<NavItem> registry)
    {
        var declared = registry.Select(i => i.GroupLabel ?? i.Module).Distinct().ToHashSet();
        var rendered = ComposeAll(registry).Select(g => g.Label).ToHashSet();

        var lost = declared.Except(rendered).ToList();
        Assert.True(lost.Count == 0,
            $"{sku}: these labels are declared in the registry but never render: "
          + $"{string.Join(", ", lost)}. An item's own GroupLabel is being discarded.");
    }

    /// <summary>
    /// The specific regression: the ward screens carry "Indoor (IPD)" and must be found under it,
    /// not under the Help Desk's "Front Desk". Named rather than derived so the intent survives a
    /// future registry edit.
    /// </summary>
    [Fact]
    public void The_ward_screens_render_under_Indoor_not_Front_Desk()
    {
        var nav = ComposeAll(ModuleNav.Registry);

        var indoor = Assert.Single(nav, g => g.Label == "Indoor (IPD)");
        Assert.Contains(indoor.Items, i => i.Url == "/ipd/board");
        Assert.Contains(indoor.Items, i => i.Url == "/ipd/admit");
        Assert.Contains(indoor.Items, i => i.Url == "/ipd/certificates");

        var frontDesk = Assert.Single(nav, g => g.Label == "Front Desk");
        Assert.DoesNotContain(frontDesk.Items, i => i.Url.StartsWith("/ipd/"));
        // Help Desk is an Ipd-module screen that genuinely belongs to the front desk — the
        // merge that the composer performs is the point, not a side effect.
        Assert.Contains(frontDesk.Items, i => i.Url == "/frontdesk");
        Assert.Contains(frontDesk.Items, i => i.Url == "/registration/new");
    }

    /// <summary>A heading with nothing under it is a composer bug, not a registry one.</summary>
    [Theory, MemberData(nameof(Registries))]
    public void No_group_is_empty(string sku, IReadOnlyList<NavItem> registry)
        => Assert.True(ComposeAll(registry).All(g => g.Items.Count > 0), $"{sku} has an empty group.");
}
