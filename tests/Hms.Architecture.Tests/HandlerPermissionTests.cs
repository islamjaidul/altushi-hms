using System.Security.Claims;
using Hms.Kernel.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Hms.Architecture.Tests;

/// <summary>
/// G10, spec 0030 F2. A screen that two roles legitimately open carries the coarser *read*
/// permission as its page policy; each mutating handler must then carry the finer one itself.
/// Hiding the button is never the control.
///
/// `appointments.create` was declared in `Perm.cs`, granted to the Receptionist in
/// `DevSeed.cs`, and referenced nowhere else: `/appointments` carried only
/// `AppointmentsRead`, so `appointments.read` alone could issue and advance serials. No
/// seeded role could exploit it — the Receptionist holds both — but §12 is editable data at
/// `/admin/users`, so a deliberately view-only grant silently conferred write, and spec
/// 0030 F1 shows grants do drift in practice.
///
/// These tests fail against the code as it stood before that fix, which is the point (G5).
/// The dependencies are `null!` on purpose: the permission check is the first statement in
/// each handler, so a handler that reaches its dependencies has already failed the test.
/// </summary>
public class HandlerPermissionTests
{
    private static Hms.Web.Pages.Appointments.IndexModel QueueScreenFor(params string[] permissions)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "test")],
            authenticationType: "test");
        foreach (var p in permissions)
            identity.AddClaim(new Claim(PermissionPolicy.ClaimType, p));

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var page = new Hms.Web.Pages.Appointments.IndexModel(null!, null!, null!, null!, null!)
        {
            PageContext = new PageContext(
                new ActionContext(http, new RouteData(), new ActionDescriptor(), new ModelStateDictionary())),
        };
        page.TempData = new TempDataDictionary(http, new NullTempDataProvider());
        return page;
    }

    [Fact]
    public async Task Issuing_a_serial_needs_appointments_create()
    {
        var page = QueueScreenFor("appointments.read");
        Assert.IsType<ForbidResult>(await page.OnPostIssueAsync());
    }

    [Fact]
    public async Task Advancing_a_serial_needs_appointments_create()
    {
        // Advancing to Done / Cancelled / NoShow consumes the serial and frees it for reissue.
        var page = QueueScreenFor("appointments.read");
        Assert.IsType<ForbidResult>(
            await page.OnPostAdvanceAsync(1, "booked", "cancelled"));
    }

    [Fact]
    public async Task The_grant_holder_is_not_refused_by_the_permission_check()
    {
        // The gate must turn on the permission, not refuse everybody — a check that always
        // returns Forbid would pass the two tests above and break the Receptionist's shift.
        // With the grant the handler goes past the gate and straight into the dependencies
        // this test deliberately left null, so reaching them IS the proof it was let through.
        var page = QueueScreenFor("appointments.read", "appointments.create");
        await Assert.ThrowsAsync<NullReferenceException>(() => page.OnPostIssueAsync());
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
