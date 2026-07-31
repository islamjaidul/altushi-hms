using Microsoft.AspNetCore.Mvc;

namespace Hms.Shell.Pages;

/// <summary>
/// Where the cookie handler sends an authenticated user who lacks the endpoint's permission
/// (G10). The nav never offers these links, but a bookmark, a typed URL or a role changed
/// mid-shift can still land here — and an operator meeting a 404 assumes the system is broken.
/// </summary>
public class DeniedModel : HmsPageModel
{
    public string? Attempted { get; private set; }

    public void OnGet([FromQuery] string? returnUrl)
    {
        // Only ever echo a local path back — never an attacker-supplied absolute URL.
        Attempted = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
    }
}
