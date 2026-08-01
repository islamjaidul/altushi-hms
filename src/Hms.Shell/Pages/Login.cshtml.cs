using Hms.Kernel.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Shell.Pages;

[AllowAnonymous]
public class LoginModel(SignInManager<AppUser> signIn) : PageModel
{
    // Nullable on purpose. MVC model binding has ConvertEmptyStringToNull on by default, so an
    // empty box arrives as null and quietly overwrites any `= ""` initialiser. Declaring these
    // non-nullable would be a lie the compiler believes and the first blank submit disproves.
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }

    public bool Failed { get; private set; }
    public bool LockedOut { get; private set; }

    public string? UsernameError { get; private set; }
    public string? PasswordError { get; private set; }

    /// <summary>
    /// Where the cursor goes on a re-render. Operators are keyboard-driven and slow typists (§7),
    /// so returning them to a field they already filled correctly costs a needless correction.
    /// </summary>
    public bool FocusPassword => UsernameError is null && PasswordError is not null;

    /// <summary>
    /// Field-level validation, kept pure so it can be tested without standing up a
    /// <see cref="SignInManager{T}"/>. Null means the field is fine.
    /// </summary>
    /// <remarks>
    /// Both parameters are nullable because that is what model binding hands over for an empty
    /// field. <c>IsNullOrEmpty</c> rather than <c>IsNullOrWhiteSpace</c> on the password: a
    /// password of spaces is a password, and refusing it would lock someone out of a credential
    /// their account genuinely has.
    /// </remarks>
    public static (string? Username, string? Password) Validate(string? username, string? password)
        => (string.IsNullOrWhiteSpace(username) ? "Enter your username." : null,
            string.IsNullOrEmpty(password) ? "Enter your password." : null);

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        // Operators scan and paste into this box, and a trailing space is not a different person.
        // The password is never trimmed: a space there is a character someone deliberately chose.
        Username = Username?.Trim();

        (UsernameError, PasswordError) = Validate(Username, Password);

        // Return before the sign-in call, not merely before the redirect. PasswordSignInAsync runs
        // with lockoutOnFailure, and Identity counts a wrong password against the *account* — so a
        // real username submitted with an empty password burns one of that account's attempts.
        // Enough stray Enter presses and an operator locks out a colleague who did nothing at all.
        // A blank box is a typing slip, not a failed credential, and must never be charged as one.
        if (UsernameError is not null || PasswordError is not null) return Page();

        // lockoutOnFailure: ADR-0019 login throttling; failures audited in S5's audit viewer scope
        // Non-null past the guard above: Validate rejects null or blank for both.
        var result = await signIn.PasswordSignInAsync(Username!, Password!,
            isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
            return LocalRedirect(returnUrl ?? "/");

        LockedOut = result.IsLockedOut;
        Failed = !result.IsLockedOut;
        return Page();
    }
}
