using Hms.Hr.Screens;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Hr.Web.Pages;

/// <summary>
/// The root of the HRM SKU. It exists because <c>Login</c> redirects to <c>"/"</c> when there is no
/// return URL (<c>Login.cshtml.cs</c>), and the ERP satisfies that with its dashboard at
/// <c>@page "/"</c>. Without an equivalent here, a correct sign-in landed on 404 — which reads to
/// an operator as "login is broken", not "the landing page is missing".
/// <para>
/// Where it sends you depends on what you hold, because the HRM has two kinds of user. Staff with
/// <c>hr.read</c> get the dashboard; an employee whose only grant is <c>hr.leave.apply</c> (the
/// self-service role) would be denied there, so they get their own page instead. Ordering matters:
/// HR staff hold both claims and must land on the dashboard.
/// </para>
/// </summary>
[Authorize]
public class IndexModel : HmsPageModel
{
    public IActionResult OnGet()
    {
        if (Can(HrPerm.Claim.Read)) return Redirect("/hr");
        if (Can(HrPerm.Claim.LeaveApply)) return Redirect("/hr/me");

        // Authenticated but holding no HR grant at all. /denied explains it; a bare 404 would not.
        return Redirect("/denied");
    }
}
