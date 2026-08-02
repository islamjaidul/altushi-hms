using System.Security.Claims;
using System.Text;
using Hms.Kernel.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Shell;

/// <summary>
/// What every screen needs from the signed-in principal. Attribution is not optional —
/// every financial and clinical write carries actor id + name snapshot (§8 N5, ADR-0011).
/// </summary>
public abstract class HmsPageModel : PageModel
{
    /// <summary>
    /// The branch this request acts on — resolved from the signed-in user's <c>branch_id</c>
    /// claim (spec 0039 WP5, AUD-ARCH-01; §5 M21 per-user binding), never a constant. Falls
    /// back to branch 1, which is what every existing deployment is.
    /// </summary>
    public virtual long BranchId =>
        long.TryParse(User.FindFirstValue("branch_id"), out var b) && b > 0 ? b : 1;

    public long ActorId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string ActorName =>
        User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "unknown";

    public string ActorRole =>
        User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    public bool Can(string permission) => User.HasClaim(PermissionPolicy.ClaimType, permission);

    /// <summary>Non-blocking confirmation on the next render (§7 U8). Truncated: the toast
    /// travels in TempData, and interpolated user input must never grow into header size
    /// (spec 0039 WP1.3 — a 100 KB name once locked an operator out with HTTP 431).</summary>
    protected void Toast(string message, string icon = "task_alt")
    {
        TempData["Toast"] = Clip(message, 300);
        TempData["ToastIcon"] = icon;
    }

    /// <summary>The operator-facing failure path: a plain sentence, never a stack trace.</summary>
    public string? ErrorMessage { get; set; }

    protected void Fail(string message) => ErrorMessage = message;

    // ------------------------------------------------------------------ the input gate
    // Spec 0039 WP1.1: the model binder writes `default` into a bare [BindProperty] when a
    // value cannot be parsed, and historically no handler inspected ModelState — a mistyped
    // payment became a silently receipted 0 Tk. This filter makes every binding or annotation
    // failure fail closed, for every handler on every page deriving from this class, before
    // the handler (or the view) can act on a substituted default.
    //
    // Only *posted* fields are judged (AttemptedValue/RawValue present): pages with several
    // forms bind one form's fields at a time, and a range rule on a field the submitted form
    // never carried must not block an unrelated action.
    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (HttpMethods.IsGet(Request.Method) || ModelState.IsValid)
        {
            await next();
            return;
        }

        var offenders = ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 } e &&
                         (e.AttemptedValue is not null || e.RawValue is not null))
            .Take(3)
            .ToList();

        if (offenders.Count == 0)
        {
            await next();
            return;
        }

        var sb = new StringBuilder("Nothing was saved — ");
        for (var i = 0; i < offenders.Count; i++)
        {
            if (i > 0) sb.Append(" · ");
            var (key, entry) = (offenders[i].Key, offenders[i].Value!);
            var label = Humanize(key);
            var attempted = Clip(entry.AttemptedValue ?? "", 30);
            var message = entry.Errors[0].ErrorMessage;
            if (string.IsNullOrWhiteSpace(message) ||
                message.Contains("is not valid", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(attempted.Length == 0
                    ? $"{label} is required"
                    : $"{label}: “{attempted}” is not a value this box accepts. " +
                      "Money and quantities are whole numbers (no decimals, no letters)");
            }
            else
            {
                sb.Append(message.TrimEnd('.'));
            }
        }
        sb.Append(". Check the highlighted entry and try again.");

        TempData["Error"] = Clip(sb.ToString(), 500);
        context.Result = RedirectToSelf();
        await Task.CompletedTask;
    }

    /// <summary>Back to this page's own GET, dropping the handler so the redirect re-renders
    /// the screen (with its own OnGet data) rather than re-entering the refused action.</summary>
    private RedirectResult RedirectToSelf()
    {
        var query = Request.Query
            .Where(kv => !kv.Key.Equals("handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value, (kv, v) =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v ?? "")}")
            .ToList();
        var target = Request.Path + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return Redirect(target);
    }

    /// <summary>"PaidNow" → "Paid now", "Qtys[0]" → "Qtys row 1" — the operator's label,
    /// never the raw property path.</summary>
    internal static string Humanize(string key)
    {
        var name = key;
        var row = "";
        var bracket = key.IndexOf('[');
        if (bracket >= 0)
        {
            var end = key.IndexOf(']', bracket);
            if (end > bracket && int.TryParse(key[(bracket + 1)..end], out var ix))
                row = $" row {ix + 1}";
            name = key[..bracket];
        }
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];

        var sb = new StringBuilder();
        foreach (var (c, i) in name.Select((c, i) => (c, i)))
        {
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb + row;
    }

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
