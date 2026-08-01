using Hms.Shell.Pages;

namespace Hms.Web.Tests;

/// <summary>
/// The login form had no validation at all: both boxes were plain strings, nothing checked them,
/// and an empty submit went straight to <c>PasswordSignInAsync</c>.
///
/// That is not only a missing message. The sign-in call runs with <c>lockoutOnFailure: true</c>,
/// and ASP.NET Core Identity charges a wrong password against the *account* — so a real username
/// submitted with an empty password consumed one of that account's attempts. Enough stray Enter
/// presses on a shared counter PC and an operator locks out a colleague who did nothing wrong.
/// A blank box is a typing slip, not a failed credential.
///
/// <see cref="LoginModel.Validate"/> is deliberately pure so this costs no database, no HTTP and
/// no <c>SignInManager</c> — the same reasoning as <see cref="RegistrationInputTests"/>.
/// </summary>
public class LoginValidationTests
{
    [Theory]
    // null is not a hypothetical: MVC binds an empty form field to null, not "",
    // because ConvertEmptyStringToNull defaults to true. The first version of these
    // tests used only "" — they passed while the deployed page threw
    // NullReferenceException on every blank submit. Test what the framework hands
    // you, not what the property declaration suggests.
    [InlineData(null, null)]
    [InlineData(null, "Demo#1234")]
    [InlineData("farid", null)]
    [InlineData("", "")]
    [InlineData("", "Demo#1234")]
    [InlineData("   ", "Demo#1234")]     // whitespace is not a username
    [InlineData("farid", "")]            // the dangerous one: a real account, no password
    public void Blank_input_is_rejected_before_the_sign_in_call(string? user, string? password)
    {
        var (u, p) = LoginModel.Validate(user, password);
        Assert.True(u is not null || p is not null,
            "Validation passed blank input through to PasswordSignInAsync, which would charge "
            + "the attempt against the account's lockout counter.");
    }

    [Fact]
    public void Validate_never_throws_on_null()
    {
        // The regression that reached production. Asserted explicitly so it cannot come back
        // disguised as a refactor.
        var ex = Record.Exception(() => LoginModel.Validate(null, null));
        Assert.Null(ex);
    }

    [Fact]
    public void Each_field_is_named_separately()
    {
        var (u, p) = LoginModel.Validate(null, null);
        Assert.NotNull(u);
        Assert.NotNull(p);

        // "Sign-in failed — check the username and password" is the right message for a wrong
        // credential and the wrong one for an empty box: it does not say which box.
        Assert.NotEqual(u, p);
    }

    [Fact]
    public void A_filled_form_passes()
    {
        var (u, p) = LoginModel.Validate("farid", "Demo#1234");
        Assert.Null(u);
        Assert.Null(p);
    }

    [Fact]
    public void A_password_that_is_only_spaces_is_accepted()
    {
        // Passwords are never trimmed and never judged on content. Whatever the account was
        // created with must remain enterable, or we lock people out of their own credential.
        var (_, p) = LoginModel.Validate("farid", "   ");
        Assert.Null(p);
    }
}
