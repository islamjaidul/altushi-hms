using System.Reflection;
using System.Text.RegularExpressions;
using Hms.Kernel.Auth;

namespace Hms.Architecture.Tests;

/// <summary>
/// Spec 0032 M20-F1, and the other side of the coin from <see cref="HandlerPermissionTests"/>.
///
/// There, a view-only grant silently conferred *write* — the handler had no gate. The mirror
/// image is a screen that renders a button its own page policy cannot press: `/notifications/tray`
/// is `[Authorize(Perm.NotificationsRead)]` and offers a **Resend** posting to
/// `/admin/sms?handler=Resend`, which is `[Authorize(Perm.AdminMastersManage)]`. Handing a
/// non-technical operator (§7) a button that 403s is a bug even though it leaks nothing.
///
/// The tray already gates that button on `Model.Can("admin.masters.manage")`, so the behaviour
/// is right; what was missing is anything that keeps it right. §12 is editable data at
/// `/admin/users` and spec 0030 F1 showed grants drift in practice, so the guard and the policy
/// it shadows have to be joined by something that fails when they part company.
///
/// Note the shape of `Can`: `HmsPageModel.Can(p)` tests the raw **claim**, while `Perm.*` are
/// **policy** names carrying the `perm:` prefix that `PermissionPolicy.TryParse` strips. So
/// `Can(Perm.AdminMastersManage)` compiles and is silently always false — a whole class of
/// bug that looks correct in review. The last test here forbids it outright.
/// </summary>
public class ViewGuardPermissionTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static string WebDir => Path.Combine(RepoRoot(), "src", "Hms.Web");

    private static IEnumerable<string> SourceFiles(string extension) =>
        Directory.EnumerateFiles(WebDir, extension, SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"));

    /// <summary>Every permission `Perm.cs` declares, with the `perm:` policy prefix stripped.</summary>
    private static HashSet<string> DeclaredPermissions() =>
        typeof(Hms.Web.Perm)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Select(v => PermissionPolicy.TryParse(v, out var p) ? p : v)
            .ToHashSet(StringComparer.Ordinal);

    // ---- the M20-F1 join ------------------------------------------------------

    /// <summary>
    /// The tray's Resend button must be hidden behind exactly the permission the handler it
    /// posts to demands. Chosen over moving the handler: resend is template/masters work and
    /// belongs on `/admin/sms` with the rest of it, and `notifications.read` is deliberately the
    /// weaker "look at the log" grant.
    /// </summary>
    [Fact]
    public void The_trays_resend_button_is_gated_on_the_permission_its_handler_requires()
    {
        var tray = File.ReadAllText(Path.Combine(WebDir, "Pages", "Notifications", "Tray.cshtml"));

        // The button exists at all — otherwise this test passes vacuously forever.
        Assert.Contains("/admin/sms?handler=Resend", tray, StringComparison.Ordinal);

        // The guard that wraps it, and the permission it names.
        var guard = Regex.Match(tray, @"@if \(Model\.Can\(""([a-z][a-z.]+)""\)[^)]*\)\s*\{\s*<form[^>]*action=""/admin/sms\?handler=Resend""");
        Assert.True(guard.Success,
            "The Resend form is no longer wrapped in a Model.Can(...) guard — a read-only "
            + "grant now renders a button that 403s (spec 0032 M20-F1).");

        // The policy the handler actually enforces, reduced to its claim.
        var handler = File.ReadAllText(Path.Combine(WebDir, "Pages", "Admin", "Sms.cshtml.cs"));
        var policy = Regex.Match(handler, @"\[Authorize\(Policy = Perm\.(\w+)\)\]");
        Assert.True(policy.Success, "Sms.cshtml.cs no longer carries an [Authorize(Policy = Perm.X)].");

        var value = (string)typeof(Hms.Web.Perm)
            .GetField(policy.Groups[1].Value, BindingFlags.Public | BindingFlags.Static)!
            .GetRawConstantValue()!;
        PermissionPolicy.TryParse(value, out var required);

        Assert.Equal(required, guard.Groups[1].Value);
    }

    // ---- the general rules the join depends on --------------------------------

    /// <summary>A guard naming a permission that does not exist is a button nobody can ever
    /// see — the silent opposite of the 403, and just as invisible in review.</summary>
    [Fact]
    public void Every_view_guard_names_a_permission_that_exists()
    {
        var declared = DeclaredPermissions();
        var offenders = new List<string>();

        foreach (var file in SourceFiles("*.cshtml").Concat(SourceFiles("*.cs")))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\bCan\(""([^""]+)""\)"))
                if (!declared.Contains(m.Groups[1].Value))
                    offenders.Add($"{Path.GetFileName(file)}: Can(\"{m.Groups[1].Value}\")");
        }

        Assert.True(offenders.Count == 0,
            "A Can(...) guard names a permission absent from Perm.cs, so it is always false:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// `Can` takes the claim, `Perm.*` are policy names with the `perm:` prefix. Passing a
    /// prefixed string compiles and always returns false, hiding a control from everyone —
    /// including the role that holds it.
    /// </summary>
    [Fact]
    public void No_view_guard_is_passed_a_policy_name_instead_of_a_permission()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles("*.cshtml").Concat(SourceFiles("*.cs")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\bCan\(\s*(""perm:[^""]*""|Perm\.\w+)\s*\)"))
                offenders.Add($"{Path.GetFileName(file)}: Can({m.Groups[1].Value})");
        }

        Assert.True(offenders.Count == 0,
            "Can() takes the bare permission; a 'perm:' policy name is silently always false:\n  "
            + string.Join("\n  ", offenders));
    }
}
