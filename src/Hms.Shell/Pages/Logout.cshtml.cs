using Hms.Kernel.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Shell.Pages;

public class LogoutModel(SignInManager<AppUser> signIn) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        await signIn.SignOutAsync();

        // Hand the screen they were on to the login page, so signing back in returns them to
        // their work rather than the dashboard (spec 0040). The URL came from a form field, so
        // it is untrusted: SafeReturnUrl keeps it only if it is local and is not an auth page.
        var target = LoginModel.SafeReturnUrl(returnUrl, Url.IsLocalUrl(returnUrl));
        return Redirect(target == "/"
            ? "/login"
            : "/login?returnUrl=" + Uri.EscapeDataString(target));
    }
}
