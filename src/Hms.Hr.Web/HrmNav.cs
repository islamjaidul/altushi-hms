using Hms.Hr.Screens;
using Hms.Kernel.Auth;
using Hms.Shell;

namespace Hms.Hr.Web;

/// <summary>
/// The HRM SKU's sidebar: the HR module's own entries, plus the administration screens every host
/// has whatever it sells.
/// <para>
/// Until spec 0036 this host registered <c>HrNav.Registry</c> alone, which left the standalone
/// product with no route to user management at all — §5 M21 is a platform capability that had been
/// filed under the ERP by accident of where its Razor page happened to live.
/// </para>
/// <para>
/// The entry is tagged with the <b>HR</b> module rather than an Admin one on purpose. Nav entries are
/// filtered by entitlement, and in a single-module SKU the HR licence <i>is</i> the product: if it
/// lapses, the whole application should go, not leave a lone administration screen behind.
/// </para>
/// </summary>
public static class HrmNav
{
    private const string Group = "Administration";

    public static readonly IReadOnlyList<NavItem> Platform =
    [
        // Nav takes the bare claim; [Authorize] on the page takes the perm:-prefixed policy.
        new(HrModule.Name, "Users & Roles", "/admin/users",
            PlatformPerm.Claim.UsersManage, "manage_accounts", Group),
    ];

    public static readonly IReadOnlyList<NavItem> Composed = [.. HrNav.Registry, .. Platform];
}
