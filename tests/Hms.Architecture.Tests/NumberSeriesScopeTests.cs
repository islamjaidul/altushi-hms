using System.Text.RegularExpressions;

namespace Hms.Architecture.Tests;

/// <summary>
/// A number series is keyed <c>(branch_id, doc_type, fiscal_year)</c>, so a fiscal-year scope
/// restarts the counter every year. That is correct for documents that belong to a period — an
/// invoice, a receipt — whose display format carries <c>{fy}</c> and therefore stays unique.
/// <para>
/// It is a defect when the format has no <c>{fy}</c>: the first document of each new fiscal year is
/// issued the same string as the first of the last one, and the unique index rejects it. The failure
/// arrives on the day the fiscal year turns over, on a screen that worked all year, as a 500.
/// </para>
/// <para>
/// <c>EmployeeService</c> shipped exactly this — <c>EMP-{n:D5}</c> issued against
/// <c>FiscalYearOf(JoinedOn)</c>. It survived every test because the fixtures hire people inside one
/// year; seeding a hundred with joining dates spread over seven is what broke it (spec 0036). This
/// test is the general form, over all fourteen modules rather than the one that was caught.
/// </para>
/// </summary>
public class NumberSeriesScopeTests
{
    /// <summary>
    /// Scope arguments that are deliberately not a fiscal year. The counter never resets under
    /// these, so the format needs no year token to stay unique.
    /// </summary>
    private static readonly string[] NonYearScopes = ["\"all\"", "UhidScope"];

    [Fact]
    public void A_fiscal_year_scoped_series_carries_the_year_in_its_format()
    {
        var offenders = new List<string>();
        var root = FindRepoRoot();

        foreach (var file in Directory
                     .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(NotBuildOutput))
        {
            var text = File.ReadAllText(file);

            // numbers.IssueAsync(db, branchId, docType, <scope>, "<format>", ct)
            foreach (Match m in Regex.Matches(
                         text,
                         @"Issue(?:Value)?Async\(\s*\w+\s*,\s*\w+\s*,\s*([^,]+?)\s*,\s*([^,]+?)\s*,\s*""([^""]*)""",
                         RegexOptions.Singleline))
            {
                var (docType, scope, format) = (m.Groups[1].Value.Trim(),
                                                m.Groups[2].Value.Trim(),
                                                m.Groups[3].Value);

                if (NonYearScopes.Contains(scope)) continue;
                if (format.Contains("{fy}")) continue;

                offenders.Add(
                    $"{Path.GetRelativePath(root, file)}: doc type {docType} is issued per fiscal "
                    + $"year ({scope}) but its format \"{format}\" has no {{fy}} — the counter "
                    + "restarts each year and reissues the same string.");
            }
        }

        Assert.True(offenders.Count == 0,
            "Number formats that collide across a fiscal-year boundary:\n  "
            + string.Join("\n  ", offenders));
    }

    private static bool NotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
