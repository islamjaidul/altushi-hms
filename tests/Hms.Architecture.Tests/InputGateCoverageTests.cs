using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Architecture.Tests;

/// <summary>
/// Spec 0039 WP1.1. The input gate lives on <see cref="Hms.Shell.HmsPageModel"/> — a binding or
/// annotation failure on a posted field is refused before any handler runs. That protection is
/// structural only while every screen actually derives from the base class, so this test fails
/// the moment someone adds a page straight off <see cref="PageModel"/>.
///
/// The allowlist is the deliberate exceptions: the auth pair (no operator data, must work when
/// nothing else does) and the two anonymous public surfaces (read-only by design, spec 0019).
/// Adding a name here is a decision, not a formality — an unlisted page outside the gate is a
/// screen where a mistyped payment can silently become 0 Tk again.
/// </summary>
public class InputGateCoverageTests
{
    private static readonly string[] OutsideTheGate =
    [
        "Hms.Shell.Pages.LoginModel",
        "Hms.Shell.Pages.LogoutModel",
        "Hms.Web.Pages.Public.QueueModel",
        "Hms.Web.Pages.Public.ReportStatusModel",
    ];

    [Fact]
    public void Every_page_model_in_both_hosts_derives_from_HmsPageModel()
    {
        var assemblies = new[]
        {
            typeof(Hms.Web.Pages.Billing.OpdModel).Assembly,        // ERP host
            typeof(Hms.Shell.HmsPageModel).Assembly,                // shared shell pages
            typeof(Hms.Hr.Screens.HrPerm).Assembly,                 // HRM screens
        };

        var strays = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(PageModel).IsAssignableFrom(t)
                        && !typeof(Hms.Shell.HmsPageModel).IsAssignableFrom(t)
                        && !OutsideTheGate.Contains(t.FullName))
            .Select(t => t.FullName)
            .OrderBy(n => n)
            .ToList();

        Assert.True(strays.Count == 0,
            "These pages do not derive from HmsPageModel, so the input gate does not cover "
            + "them — a parse failure would silently bind default again:\n  "
            + string.Join("\n  ", strays));
    }

    [Fact]
    public void The_allowlist_names_only_pages_that_exist()
    {
        // A renamed page must not leave a stale allowlist entry silently widening the exemption.
        var all = new[]
            {
                typeof(Hms.Web.Pages.Billing.OpdModel).Assembly,
                typeof(Hms.Shell.HmsPageModel).Assembly,
                typeof(Hms.Hr.Screens.HrPerm).Assembly,
            }
            .SelectMany(a => a.GetTypes()).Select(t => t.FullName).ToHashSet();

        var stale = OutsideTheGate.Where(n => !all.Contains(n)).ToList();
        Assert.True(stale.Count == 0,
            "Allowlist entries with no matching page:\n  " + string.Join("\n  ", stale));
    }
}
