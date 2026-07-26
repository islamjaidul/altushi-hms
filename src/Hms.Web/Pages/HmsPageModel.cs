using System.Security.Claims;
using Hms.Kernel.Auth;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Web.Pages;

/// <summary>
/// What every screen needs from the signed-in principal. Attribution is not optional —
/// every financial and clinical write carries actor id + name snapshot (§8 N5, ADR-0011).
/// </summary>
public abstract class HmsPageModel : PageModel
{
    /// <summary>MVP is single-branch; multi-branch resolution lands with ADR-0007.</summary>
    public const long BranchId = 1;

    public long ActorId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string ActorName =>
        User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "unknown";

    public string ActorRole =>
        User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    public bool Can(string permission) => User.HasClaim(PermissionPolicy.ClaimType, permission);

    /// <summary>Non-blocking confirmation on the next render (§7 U8).</summary>
    protected void Toast(string message, string icon = "task_alt")
    {
        TempData["Toast"] = message;
        TempData["ToastIcon"] = icon;
    }

    /// <summary>The operator-facing failure path: a plain sentence, never a stack trace.</summary>
    public string? ErrorMessage { get; set; }

    protected void Fail(string message) => ErrorMessage = message;
}
