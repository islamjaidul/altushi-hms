using Hms.Shell.Pages;

namespace Hms.Web.Tests;

/// <summary>
/// Where a successful sign-in lands (spec 0040 WP2).
///
/// Two defects met here. Signing out threw the operator's page away — `/logout` redirected to a
/// bare `/login`, so an interruption on `/billing/opd?PatientId=118` cost the navigation back.
/// And the destination came off the query string into <c>LocalRedirect</c>, which refuses a
/// foreign host by *throwing*: a pasted link produced the fault boundary's error page where the
/// dashboard was the right answer.
///
/// <see cref="LoginModel.SafeReturnUrl"/> is pure — the caller passes the framework's own
/// <c>IUrlHelper.IsLocalUrl</c> verdict — so the policy is testable with no HTTP, and the
/// security-critical "is this local" half is not re-implemented in our code.
/// </summary>
public class LoginReturnUrlTests
{
    [Theory]
    [InlineData("/billing/opd")]
    [InlineData("/billing/opd?PatientId=118")]          // the query string is the useful half
    [InlineData("/ipd/folio/54")]
    [InlineData("/loginhelp")]                          // NOT an auth page: a prefix test ate this
    [InlineData("/deniedreport")]
    public void A_local_page_is_honoured(string url)
        => Assert.Equal(url, LoginModel.SafeReturnUrl(url, isLocal: true));

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("http://localhost:5199/billing/opd")]    // absolute, so not ours to honour
    public void A_foreign_host_lands_on_the_dashboard_rather_than_throwing(string url)
    {
        // isLocal:false is what IsLocalUrl returns for all of these. The point of the assertion
        // is the *degradation*: "/" and no exception, where LocalRedirect raised into a 500.
        var target = LoginModel.SafeReturnUrl(url, isLocal: false);
        Assert.Equal("/", target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_remembered_means_the_dashboard(string? url)
        => Assert.Equal("/", LoginModel.SafeReturnUrl(url, isLocal: true));

    [Theory]
    // AUD-PHI-02: bouncing a freshly signed-in operator back to "access denied" is not a loop,
    // just wrong — and /login would ask them to sign in again having just done so.
    [InlineData("/denied")]
    [InlineData("/DENIED")]
    [InlineData("/denied?from=%2Fbilling")]
    [InlineData("/login")]
    [InlineData("/login?returnUrl=%2Flogin")]
    [InlineData("/logout")]
    [InlineData("/logout/")]
    public void The_auth_pages_are_never_the_destination(string url)
        => Assert.Equal("/", LoginModel.SafeReturnUrl(url, isLocal: true));

    [Fact]
    public void The_check_is_on_the_path_not_the_whole_url()
    {
        // A real page whose *query* mentions an auth path must still be honoured — otherwise
        // "?next=/login" anywhere in the product would silently send people to the dashboard.
        const string url = "/billing/opd?note=%2Flogin";
        Assert.Equal(url, LoginModel.SafeReturnUrl(url, isLocal: true));
    }
}
