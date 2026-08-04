using System.ComponentModel.DataAnnotations;
using Hms.Registration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Registration;

/// <summary>
/// §5 M1 US1.4, second half: "details completed later" (spec 0045). Until this screen existed a
/// casualty registered with no name kept <c>UNKNOWN (ALT-…)</c> for life, and correcting a
/// misheard name or a wrong sex was impossible — a wrong sex silently mis-flags every future
/// haemoglobin, because it selects the reference band (§5 M9).
/// <para>
/// Deliberately the same field names, parsing and bounds as <see cref="NewModel"/>: an operator
/// who can use one can use the other, and the two cannot drift on what a valid age is.
/// </para>
/// </summary>
[Authorize(Policy = Perm.RegistrationCreate)]
public class EditModel(HmsTx tx, RegistrationService registration) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    // Nullable and no [Required], for the same reason NewModel documents at length: a record
    // still flagged unknown legitimately posts a blank name, and a non-nullable string carries
    // MVC's implicit required rule, which would refuse it at the input gate before the handler's
    // conditional rule could allow it.
    [BindProperty, StringLength(Bounds.Name)] public string? FullName { get; set; }
    [BindProperty] public string Sex { get; set; } = "M";
    [BindProperty, StringLength(Bounds.Code)] public string? AgeOrDob { get; set; }
    [BindProperty, StringLength(Bounds.Phone)] public string? Phone { get; set; }
    [BindProperty, StringLength(Bounds.Name)] public string? Guardian { get; set; }
    [BindProperty, StringLength(Bounds.Name)] public string? Area { get; set; }
    [BindProperty, StringLength(Bounds.Address)] public string? Address { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? BloodGroup { get; set; }
    [BindProperty, StringLength(Bounds.Note)] public string? Allergies { get; set; }
    [BindProperty] public string PatientType { get; set; } = "general";
    /// <summary>Set by the "save anyway" button under the duplicate list (edge 23).</summary>
    [BindProperty] public bool DuplicatesAcknowledged { get; set; }

    public string Uhid { get; private set; } = "";
    public bool WasUnknown { get; private set; }
    public IReadOnlyList<DuplicateCandidate> Duplicates { get; private set; } = [];

    public IEnumerable<DuplicateCandidate> LikelyDuplicates => Duplicates.Where(d => d.NameMatch);
    public IEnumerable<DuplicateCandidate> SharedPhone => Duplicates.Where(d => d.IsPhoneOnly);

    public async Task<IActionResult> OnGetAsync()
    {
        var found = await tx.RunAsync(async s =>
            await s.Reg.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == Id));
        if (found is null) return NotFound();

        Uhid = found.Uhid;
        WasUnknown = found.UnknownIdentity;
        // An unknown record opens with an empty name box rather than "UNKNOWN (ALT-14)" — the
        // operator is completing an identity, not editing a placeholder into shape.
        FullName = found.UnknownIdentity ? null : found.FullName;
        Sex = found.Sex.ToString();
        AgeOrDob = found.Dob?.ToString("dd/MM/yyyy")
                   ?? (found.AgeYears is { } y ? y.ToString()
                       : found.AgeMonths is { } m ? $"{m} months" : null);
        Phone = found.Phone;
        Guardian = found.Guardian;
        Area = found.Area;
        Address = found.Address;
        BloodGroup = found.BloodGroup;
        Allergies = found.Allergies;
        PatientType = found.PatientType;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var found = await tx.RunAsync(async s =>
            await s.Reg.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == Id));
        if (found is null) return NotFound();
        Uhid = found.Uhid;
        WasUnknown = found.UnknownIdentity;

        FullName = (FullName ?? "").Trim();
        var phone = NewModel.NormalizePhone(Phone);
        var (dob, years, months, estimated) = NewModel.ParseAge(AgeOrDob);

        if (string.IsNullOrWhiteSpace(FullName) && !found.UnknownIdentity)
        {
            Fail("A name is required.");
            return Page();
        }
        // Same message as registration, deliberately — an operator meets one wording, not two.
        if (!string.IsNullOrWhiteSpace(AgeOrDob) && dob is null && years is null && months is null)
        {
            Fail("That age or date of birth could not be read. " +
                 "Enter an age (e.g. 45) or a date like 12/03/1980.");
            return Page();
        }
        if (years is > 130)
        {
            Fail("That age doesn't look right — check it, or enter a date of birth like 12/03/1980.");
            return Page();
        }

        // Renaming UNKNOWN to a name the hospital already holds is a functional duplicate that
        // merge (US1.3, [S]) does not yet exist to repair — so surface it. Non-blocking, exactly
        // as registration is (edge 23), and classified as spec 0043 classifies it.
        if (!DuplicatesAcknowledged && !string.IsNullOrWhiteSpace(FullName))
        {
            Duplicates = (await tx.RunAsync(s =>
                    registration.FindDuplicatesAsync(s.Reg, FullName, phone, years)))
                .Where(d => d.Id != Id)                 // he is not his own duplicate
                .ToList();
            if (Duplicates.Count > 0) return Page();
        }

        try
        {
            var patient = await tx.RunAsync(s => registration.UpdateAsync(
                s.Reg, s.Kernel, new UpdatePatientCommand(
                    Id, FullName, Sex.FirstOrDefault('M'), dob, years, months, estimated,
                    phone, Guardian, Area, Address, BloodGroup, PatientType,
                    ActorId, ActorName, Allergies)));

            var shown = patient.FullName.Length <= 60 ? patient.FullName : patient.FullName[..60] + "…";
            Toast(WasUnknown
                ? $"Identity completed — {shown} is now {patient.Uhid}"
                : $"Details updated — {shown}", "badge");
            return Redirect($"/registration/{patient.Id}/card");
        }
        catch (ArgumentException e)
        {
            Fail(e.Message);
            return Page();
        }
    }
}
