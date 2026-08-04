namespace Hms.Kernel.Auth;

/// <summary>
/// One nav entry a module registers at composition (05 §2). <paramref name="Icon"/> is a
/// Material Symbols ligature name; <paramref name="GroupLabel"/> is the operator-facing group
/// heading (§7 U15 vocabulary) when it differs from the technical module name.
/// </summary>
public sealed record NavItem(
    string Module, string Title, string Url, string Permission,
    string Icon = "circle", string? GroupLabel = null);

public sealed record NavGroup(string Module, IReadOnlyList<NavItem> Items)
{
    /// <summary>
    /// What the operator reads above the group. Set by <see cref="NavComposer.Compose"/>, which
    /// groups on this value — see the remarks there for why it is not derived from Items[0].
    /// </summary>
    public string Label { get; init; } = Module;
}

/// <summary>
/// Menu tree = permissions ∩ entitlements (ADR-0016 choke point 1 of 3).
/// Server-side endpoint policies remain the actual control (G10) — this only shapes the sidebar.
/// </summary>
/// <remarks>
/// Grouping is by the operator-facing <see cref="NavItem.GroupLabel"/>, not by module (spec 0043).
/// Grouping by module and then titling the group from its first item silently discarded every
/// other label in that module: the Ipd module leads with Help Desk ("Front Desk"), so its ward
/// screens rendered under a second "FRONT DESK" heading and "Indoor (IPD)" appeared nowhere.
/// Two modules that deliberately share a heading now merge into one group.
///
/// Entitlement and permission are filtered *before* grouping, so a merged group can never carry
/// an item the user is not entitled to.
/// </remarks>
public static class NavComposer
{
    public static IReadOnlyList<NavGroup> Compose(
        IEnumerable<NavItem> registry,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> enabledModules)
    {
        var perms = permissions as ISet<string> ?? permissions.ToHashSet(StringComparer.Ordinal);
        var modules = enabledModules as ISet<string> ?? enabledModules.ToHashSet(StringComparer.Ordinal);

        return registry
            .Where(i => modules.Contains(i.Module) && perms.Contains(i.Permission))
            // Registry order decides where a group sits and which module names it — GroupBy is
            // documented to preserve first-appearance order of keys and order within each group.
            .GroupBy(i => i.GroupLabel ?? i.Module, StringComparer.Ordinal)
            .Select(g => new NavGroup(g.First().Module, g.ToList()) { Label = g.Key })
            .ToList();
    }
}
