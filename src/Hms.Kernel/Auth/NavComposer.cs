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
    /// <summary>What the operator reads above the group; falls back to the module name.</summary>
    public string Label => Items.Count > 0 ? Items[0].GroupLabel ?? Module : Module;
}

/// <summary>
/// Menu tree = permissions ∩ entitlements (ADR-0016 choke point 1 of 3).
/// Server-side endpoint policies remain the actual control (G10) — this only shapes the sidebar.
/// </summary>
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
            .GroupBy(i => i.Module)
            .Select(g => new NavGroup(g.Key, g.ToList()))
            .ToList();
    }
}
