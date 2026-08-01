namespace Hms.Architecture.Tests;

/// <summary>
/// Every host must answer <c>"/"</c>.
/// <para>
/// <c>Login.cshtml.cs</c> ends with <c>LocalRedirect(returnUrl ?? "/")</c>, so a user who signs in
/// from the login page directly — rather than by being bounced off a protected URL — is always sent
/// to the root. The ERP satisfies that with its dashboard; the HRM SKU shipped without any page at
/// <c>"/"</c> and a correct sign-in therefore landed on 404. Operators reported that, accurately
/// enough, as "login is not working".
/// </para>
/// <para>
/// The bug is invisible to route-by-route checking: every HR page returned 200 and the login POST
/// returned a 302 with a valid cookie. Only following the redirect shows it. A host is not usable
/// because its pages work; it is usable because the path a user actually walks works.
/// </para>
/// </summary>
public class HostRootRouteTests
{
    /// <summary>Directories that compile into a host executable, relative to the repo root.</summary>
    private static readonly string[] Hosts = ["src/Hms.Web", "src/Hms.Hr.Web"];

    [Fact]
    public void Every_host_has_a_page_routed_at_root()
    {
        var root = FindRepoRoot();
        var missing = new List<string>();

        foreach (var host in Hosts)
        {
            var pages = Path.Combine(root, host, "Pages");
            var claimsRoot = Directory.Exists(pages)
                && Directory.EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories)
                    .Any(f => File.ReadLines(f).FirstOrDefault()?.Replace(" ", "") is "@page\"/\"");

            if (!claimsRoot) missing.Add(host);
        }

        Assert.True(missing.Count == 0,
            "These hosts have no page at \"/\", so a successful login redirects to 404: "
            + string.Join(", ", missing));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
