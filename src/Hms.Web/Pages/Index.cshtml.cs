using Hms.Kernel.Auth;
using Hms.Kernel.Entitlements;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Web.Pages;

public class IndexModel(IReadOnlyList<NavItem> registry, EntitlementProvider entitlements) : PageModel
{
    public string DisplayName { get; private set; } = "";
    public IReadOnlyList<NavGroup> Groups { get; private set; } = [];

    public void OnGet()
    {
        DisplayName = User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "";
        var perms = User.FindAll(PermissionPolicy.ClaimType).Select(c => c.Value).ToHashSet();
        Groups = NavComposer.Compose(registry, perms, entitlements.EnabledModules.ToHashSet());
    }
}
