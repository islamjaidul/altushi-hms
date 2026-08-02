using Hms.Web.Pages.Registration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0032 LC-REG-20 — the defect the case was opened to look for.
///
/// ASP.NET model binding converts an <b>empty</b> form field to <c>null</c>
/// (`ConvertEmptyStringToNull` defaults to true), so `public string FullName { get; set; } = ""`
/// is null after a post that leaves the name blank. Exactly one screen path may legitimately
/// post a blank name — the unconscious emergency of edge 25, where the box is ticked and the
/// UHID becomes the name — and on that path `FullName.Trim()` threw NullReferenceException.
/// `/registration/new` returned <b>HTTP 500</b>: the ER could not register a patient at all.
///
/// Nothing below the screen could have caught it. `RegistrationTests.Unknown_emergency_registers_
/// without_identity` passes a real <c>""</c> and is green throughout, which is precisely why the
/// QA engineer refused to let a service-level fact discharge a row that names a receptionist.
///
/// Dependencies are `null!` on purpose, as in <c>HandlerPermissionTests</c>: normalisation and
/// the two validation guards run before anything touches them, so a handler that reaches its
/// dependencies has already proved it got past the blank name.
/// </summary>
public class RegistrationScreenTests
{
    private static NewModel Screen(string? fullName, bool unknownIdentity,
        string? sex = "M", string? ageOrDob = null, string? patientType = "general")
    {
        var http = new DefaultHttpContext();
        // A real clock, not null: the DOB plausibility guard (spec 0039 WP1) needs "today".
        var page = new NewModel(null!, null!, null!, null!, TimeProvider.System)
        {
            FullName = fullName!,
            Sex = sex!,
            PatientType = patientType!,
            AgeOrDob = ageOrDob,
            UnknownIdentity = unknownIdentity,
            PageContext = new PageContext(new ActionContext(
                http, new RouteData(), new ActionDescriptor(), new ModelStateDictionary())),
        };
        page.TempData = new TempDataDictionary(http, new NullTempDataProvider());
        return page;
    }

    // ---- the defect -----------------------------------------------------------

    /// <summary>
    /// The ER post: no name, no age, box ticked. It must get past the guards — reaching the
    /// deliberately-null transaction IS the proof it did — and the blank name must have been
    /// normalised on the way, rather than thrown on.
    /// </summary>
    [Fact]
    public async Task An_unknown_identity_post_with_a_null_name_survives_to_the_transaction()
    {
        var page = Screen(fullName: null, unknownIdentity: true, ageOrDob: null);

        await Assert.ThrowsAsync<NullReferenceException>(() => page.OnPostAsync("save"));

        // The NRE above came from the null HmsTx, not from the name: had the name thrown, the
        // property would still be null here.
        Assert.Equal("", page.FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_normalised_not_dereferenced(string? typed)
    {
        var page = Screen(fullName: typed, unknownIdentity: true);

        await Assert.ThrowsAsync<NullReferenceException>(() => page.OnPostAsync("save"));
        Assert.Equal("", page.FullName);
    }

    /// <summary>Blank selects bind to null the same way; neither may reach `.FirstOrDefault`
    /// or the patient's type column as one.</summary>
    [Fact]
    public async Task Blank_sex_and_patient_type_fall_back_to_their_defaults()
    {
        var page = Screen(fullName: null, unknownIdentity: true, sex: null, patientType: null);

        await Assert.ThrowsAsync<NullReferenceException>(() => page.OnPostAsync("save"));
        Assert.Equal("M", page.Sex);
        Assert.Equal("general", page.PatientType);
    }

    // ---- the guards must still refuse what they always refused ----------------

    /// <summary>Without the box, a blank name is a refusal on the screen — a plain sentence,
    /// never a 500 and never a silent save (§7).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task A_blank_name_without_the_box_is_refused_on_the_page(string? typed)
    {
        var page = Screen(fullName: typed, unknownIdentity: false, ageOrDob: "45");

        Assert.IsType<PageResult>(await page.OnPostAsync("save"));
        Assert.Contains("name is required", page.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identity unknown", page.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_age_without_the_box_is_refused_on_the_page()
    {
        var page = Screen(fullName: "Rahim Uddin", unknownIdentity: false, ageOrDob: null);

        Assert.IsType<PageResult>(await page.OnPostAsync("save"));
        Assert.Contains("age", page.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A name of only whitespace is a blank name, not a patient called " ".</summary>
    [Fact]
    public async Task A_whitespace_only_name_without_the_box_is_refused()
    {
        var page = Screen(fullName: "   ", unknownIdentity: false, ageOrDob: "45");

        Assert.IsType<PageResult>(await page.OnPostAsync("save"));
        Assert.Contains("name is required", page.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Surrounding whitespace is trimmed off a real name before it is stored, so
    /// " Rahim " and "Rahim" are one patient rather than two.</summary>
    [Fact]
    public async Task A_typed_name_is_trimmed_before_it_reaches_the_service()
    {
        var page = Screen(fullName: "  Rahim Uddin  ", unknownIdentity: true);

        await Assert.ThrowsAsync<NullReferenceException>(() => page.OnPostAsync("save"));
        Assert.Equal("Rahim Uddin", page.FullName);
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
