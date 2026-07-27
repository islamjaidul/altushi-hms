using System.Text.RegularExpressions;

namespace Hms.Architecture.Tests;

/// <summary>
/// EF cannot join across two DbContext instances even when they share a connection — it throws
/// "Cannot use multiple context instances within a single query execution" at runtime, which
/// means a browser 500 rather than a build error. The module boundary (ADR-0003) is real and the
/// query layer has to respect it: read from one context, then join in memory.
///
/// This has bitten three times during spec 0012/0013, so it is now a build-time check.
/// </summary>
public class CrossContextQueryTests
{
    private static readonly string[] Scopes =
        ["s.Kernel", "s.Auth", "s.Reg", "s.Bill", "s.Diag", "s.Lis", "s.Adm", "s.Appt", "s.Notif"];

    [Fact]
    public void No_linq_query_expression_mixes_two_TxScope_contexts()
    {
        var root = FindRepoRoot();
        var web = Path.Combine(root, "src", "Hms.Web");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(web, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/") || file.Contains("/bin/")) continue;
            var text = File.ReadAllText(file);

            // A LINQ query expression: from ... join ... — check the contexts it names.
            foreach (Match m in Regex.Matches(text, @"from\s+\w+\s+in\s+(.+?)select\s", RegexOptions.Singleline))
            {
                var body = m.Value;
                if (!body.Contains("join", StringComparison.Ordinal)) continue;
                var used = Scopes.Where(sc => body.Contains(sc + ".", StringComparison.Ordinal)).ToList();
                if (used.Count > 1)
                    offenders.Add($"{Path.GetFileName(file)}: query joins {string.Join(" + ", used)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A LINQ query joins across DbContexts, which throws at runtime:\n  " +
            string.Join("\n  ", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
