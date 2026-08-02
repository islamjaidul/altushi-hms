namespace Hms.Architecture.Tests;

/// <summary>
/// Every routable page must end up inside a layout.
/// <para>
/// A razor class library does not inherit the host's <c>_ViewStart.cshtml</c> — the lookup walks the
/// view's own path, and an RCL's pages do not live under <c>src/Hms.Web/Pages</c>. Both RCLs
/// shipped without one, so all nine HR screens plus <c>/denied</c> and <c>/logout</c> rendered as
/// bare fragments: no <c>&lt;html&gt;</c>, no stylesheet, no sidebar. The response was still 200
/// and the markup was still correct, which is why route checks, permission checks and the
/// entitlement checks all passed over it.
/// </para>
/// <para>
/// Worse, the missing shell was mistaken for evidence: a <c>grep</c> for sidebar links on
/// <c>/hr</c> returned only <c>/hr</c>, and that was reported as the entitlement filter working.
/// There was no sidebar to filter. This test exists so the shell's presence is asserted rather
/// than inferred from a page that happens to lack links.
/// </para>
/// </summary>
public class RazorLayoutTests
{
    [Fact]
    public void Every_pages_root_with_layout_less_pages_has_a_view_start()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var pagesDir in Directory.EnumerateDirectories(Path.Combine(root, "src"), "Pages",
                     SearchOption.AllDirectories))
        {
            if (pagesDir.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || pagesDir.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            if (File.Exists(Path.Combine(pagesDir, "_ViewStart.cshtml"))) continue;

            // No _ViewStart here, so every routable page must set Layout itself. Files beginning
            // with "_" are partials and imports, and Shared/ holds the layout and its pieces.
            var unshelled = Directory
                .EnumerateFiles(pagesDir, "*.cshtml", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('_'))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}"))
                .Where(f => !File.ReadAllText(f).Contains("Layout"))
                .Select(f => Path.GetRelativePath(root, f))
                .ToList();

            offenders.AddRange(unshelled);
        }

        Assert.True(offenders.Count == 0,
            "These pages render with no layout — no <html>, no stylesheet, no sidebar. Add a "
            + "_ViewStart.cshtml to their Pages root, or set Layout explicitly if the page is "
            + "deliberately standalone:\n  " + string.Join("\n  ", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
