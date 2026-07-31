using Hms.Kernel.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Shell.Pages;

[AllowAnonymous]
public class LoginModel(SignInManager<AppUser> signIn) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";

    public bool Failed { get; private set; }
    public bool LockedOut { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        // lockoutOnFailure: ADR-0019 login throttling; failures audited in S5's audit viewer scope
        var result = await signIn.PasswordSignInAsync(Username, Password,
            isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
            return LocalRedirect(returnUrl ?? "/");

        LockedOut = result.IsLockedOut;
        Failed = !result.IsLockedOut;
        return Page();
    }
}
