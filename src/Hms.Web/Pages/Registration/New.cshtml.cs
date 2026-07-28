using Hms.Notifications;
using Hms.Registration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Web.Pages.Registration;

/// <summary>
/// §9A.2 module 1 / 05 §5 screen 2 — the ≤ 60-second screen (§9A.4). Keyboard-only: every field
/// is tab-ordered, Enter advances, and the primary action is reachable without the mouse.
/// The duplicate warning is deliberately non-blocking (edge 23) — it informs, the operator decides.
/// </summary>
[Authorize(Policy = Perm.RegistrationCreate)]
public class NewModel(
    HmsTx tx, RegistrationService registration, SmsQueue sms,
    HospitalIdentity hospital) : HmsPageModel
{
    [BindProperty] public string FullName { get; set; } = "";
    [BindProperty] public string Sex { get; set; } = "M";
    [BindProperty] public string? AgeOrDob { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Guardian { get; set; }
    [BindProperty] public string? Area { get; set; }
    [BindProperty] public string? Address { get; set; }
    [BindProperty] public string? BloodGroup { get; set; }
    [BindProperty] public string PatientType { get; set; } = "general";
    [BindProperty] public bool UnknownIdentity { get; set; }
    /// <summary>Set by the "register anyway" button under the duplicate list (edge 23).</summary>
    [BindProperty] public bool DuplicatesAcknowledged { get; set; }

    public IReadOnlyList<DuplicateCandidate> Duplicates { get; private set; } = [];
    public string NextUhidHint { get; private set; } = "";

    public void OnGet() { }

    /// <summary>
    /// §7 U13: "45", "45y", "8 months", "12/03/1980" and "1980-03-12" all mean something
    /// definite to an operator, so all of them parse. DOB wins when we can get one (02 §2.2).
    /// </summary>
    public static (DateOnly? Dob, short? Years, short? Months, bool Estimated) ParseAge(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, null, null, false);
        var raw = input.Trim();

        // The date branch is the kernel contract (ADR-0020) — same formats everywhere.
        if (Hms.Kernel.Time.FlexibleDate.TryParse(raw, out var dob))
            return (dob, null, null, false);

        var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0 || !short.TryParse(digits, out var n)) return (null, null, null, false);

        var isMonths = raw.Contains("month", StringComparison.OrdinalIgnoreCase)
                       || raw.EndsWith('m') || raw.EndsWith("mo", StringComparison.OrdinalIgnoreCase);
        // An age typed as a round number is an estimate by nature — record it as one (edge 26).
        return isMonths ? (null, null, n, true) : (null, n, null, true);
    }

    /// <summary>Bangladeshi mobiles read as 01XXX-XXXXXX (§7 U13).</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("880")) digits = "0" + digits[3..];
        return digits.Length == 11 ? $"{digits[..5]}-{digits[5..]}" : phone.Trim();
    }

    public async Task<IActionResult> OnPostAsync(string? action)
    {
        // Spec 0032 LC-REG-20. Model binding converts an EMPTY form field to null, not to "" —
        // `ConvertEmptyStringToNull` is on by default — so the initialisers on these three
        // properties do not survive a post that leaves them blank. Every ordinary registration
        // fills the name in, which is why this stood for so long; the unconscious-emergency path
        // (edge 25) is the one case that legitimately posts a blank name, and `FullName.Trim()`
        // threw NullReferenceException on it. The ER could not register a patient at all, and the
        // screen that must never fail returned a 500. The service was never wrong — its own test
        // passes a real "" — so nothing below the screen could have caught this.
        FullName = (FullName ?? "").Trim();
        Sex = string.IsNullOrWhiteSpace(Sex) ? "M" : Sex;
        PatientType = string.IsNullOrWhiteSpace(PatientType) ? "general" : PatientType;

        var phone = NormalizePhone(Phone);
        var (dob, years, months, estimated) = ParseAge(AgeOrDob);

        // Edge 24/25/26: a name is the one thing we insist on — unless this is an unknown
        // emergency admission, where even that is unavailable and the UHID becomes the name.
        if (string.IsNullOrWhiteSpace(FullName) && !UnknownIdentity)
        {
            Fail("Patient name is required. For an unconscious emergency case, tick \"identity unknown\".");
            return Page();
        }
        if (dob is null && years is null && months is null && !UnknownIdentity)
        {
            Fail("Enter an age (e.g. 45) or a date of birth (e.g. 12/03/1980).");
            return Page();
        }

        // Non-blocking duplicate warning: show it once, let the operator confirm (edge 23).
        if (!DuplicatesAcknowledged && !UnknownIdentity)
        {
            Duplicates = await tx.RunAsync(s =>
                registration.FindDuplicatesAsync(s.Reg, FullName, phone, years));
            if (Duplicates.Count > 0) return Page();
        }

        try
        {
            var patient = await tx.RunAsync(async s =>
            {
                var p = await registration.RegisterAsync(s.Reg, s.Kernel, new RegisterPatientCommand(
                    BranchId, FullName, Sex.FirstOrDefault('M'), dob, years, months, estimated,
                    phone, Guardian, Area, Address, BloodGroup, PatientType,
                    UnknownIdentity, ActorId, ActorName));

                // The welcome SMS commits with the patient, or not at all (§9A.2 module 8).
                await SmsSender.SendAsync(s, sms, BranchId, Hms.Notifications.Data.SmsEvent.Registration,
                    p.Phone, new Dictionary<string, string?>
                    { ["hospital"] = hospital.Name, ["patient"] = p.FullName, ["uhid"] = p.Uhid });
                await s.Notif.SaveChangesAsync();
                return p;
            });

            Toast($"Registered {patient.FullName} — {patient.Uhid}", "badge");
            return action == "print"
                ? Redirect($"/registration/{patient.Id}/card")
                : Redirect($"/registration?q={Uri.EscapeDataString(patient.Uhid)}");
        }
        catch (ArgumentException e)
        {
            Fail(e.Message);
            return Page();
        }
    }
}
