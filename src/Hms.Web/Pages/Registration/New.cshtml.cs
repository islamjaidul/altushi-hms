using System.ComponentModel.DataAnnotations;
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
    OrgIdentity hospital, TimeProvider clock) : HmsPageModel
{
    // Nullable and no [Required] on FullName — BOTH matter: an unconscious emergency case
    // legitimately posts a blank name with "identity unknown" ticked (edge 25, spec 0032
    // LC-REG-20), the input gate judges every posted field unconditionally, and a non-nullable
    // string carries MVC's *implicit* required rule, which would refuse the ER path at the
    // gate before the handler's conditional rule could allow it. The handler enforces the rule.
    [BindProperty, StringLength(Bounds.Name)] public string? FullName { get; set; }
    [BindProperty] public string Sex { get; set; } = "M";
    [BindProperty, StringLength(Bounds.Code)] public string? AgeOrDob { get; set; }
    [BindProperty, StringLength(Bounds.Phone)] public string? Phone { get; set; }
    [BindProperty, StringLength(Bounds.Name)] public string? Guardian { get; set; }
    [BindProperty, StringLength(Bounds.Name)] public string? Area { get; set; }
    [BindProperty, StringLength(Bounds.Address)] public string? Address { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? BloodGroup { get; set; }
    /// <summary>Spec 0042 (§5 M5 "alert flags"): free text, rendered as a red banner on every
    /// clinical screen. Blank means "not asked", which the screens show differently from none.</summary>
    [BindProperty, StringLength(Bounds.Note)] public string? Allergies { get; set; }
    [BindProperty] public string PatientType { get; set; } = "general";
    [BindProperty] public bool UnknownIdentity { get; set; }
    /// <summary>Set by the "register anyway" button under the duplicate list (edge 23).</summary>
    [BindProperty] public bool DuplicatesAcknowledged { get; set; }

    public IReadOnlyList<DuplicateCandidate> Duplicates { get; private set; } = [];
    public string NextUhidHint { get; private set; } = "";

    /// <summary>
    /// The rows whose <em>name</em> matched — the ones that really may be this patient already.
    /// These keep the red warning and the "register anyway" override.
    /// </summary>
    public IEnumerable<DuplicateCandidate> LikelyDuplicates => Duplicates.Where(d => d.NameMatch);

    /// <summary>
    /// Matched on the phone alone (spec 0043). A husband, a wife, a son on one household mobile —
    /// shown so the operator can spot a genuine mistake, but never called a duplicate, because
    /// nine times out of ten it is not one and a warning that cries wolf stops being read.
    /// </summary>
    public IEnumerable<DuplicateCandidate> SharedPhone => Duplicates.Where(d => d.IsPhoneOnly);

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

        // An age is 1–3 digits with at most a unit word after them ("45", "45y", "8 months").
        // Anything else — above all a date-shaped string that did NOT parse, like "31/02/2026"
        // or "2026-13-45" — is refused as a whole rather than silently read as the age "31"
        // (spec 0039 WP1, AUD-VAL-09).
        var m = System.Text.RegularExpressions.Regex.Match(raw, @"^(\d{1,3})\s*([A-Za-z]*)$");
        if (!m.Success || !short.TryParse(m.Groups[1].Value, out var n)) return (null, null, null, false);

        var isMonths = m.Groups[2].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase);
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

        // A DOB the product cannot mean: before 1900 Npgsql-adjacent extremes like 0001-01-01
        // land as ±infinity, and a birth after today is a person not yet born (AUD-VAL-09).
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        if (dob is { } d && (d < new DateOnly(1900, 1, 1) || d > today))
        {
            Fail("That date of birth cannot be right — it must be between 1900 and today. " +
                 "Enter an age (e.g. 45) or a date like 12/03/1980.");
            return Page();
        }
        if (years is > 130)
        {
            Fail("That age doesn't look right — check it, or enter a date of birth like 12/03/1980.");
            return Page();
        }

        // Non-blocking duplicate warning: show it once, let the operator confirm (edge 23).
        if (!DuplicatesAcknowledged && !UnknownIdentity)
        {
            Duplicates = await tx.RunAsync(s =>
                registration.FindDuplicatesAsync(s.Reg, FullName, phone, years));
            if (Duplicates.Count > 0) return Page();
        }

        // A submit button carries one name/value pair, and the confirm buttons spend theirs on
        // DuplicatesAcknowledged — so `action` arrived null and the ID card silently never
        // printed for any patient who tripped the duplicate check. Confirming *is* the decision
        // to register this patient, and a registered patient gets a card (spec 0043).
        if (DuplicatesAcknowledged && string.IsNullOrEmpty(action)) action = "print";

        try
        {
            var patient = await tx.RunAsync(async s =>
            {
                var p = await registration.RegisterAsync(s.Reg, s.Kernel, new RegisterPatientCommand(
                    BranchId, FullName, Sex.FirstOrDefault('M'), dob, years, months, estimated,
                    phone, Guardian, Area, Address, BloodGroup, PatientType,
                    UnknownIdentity, ActorId, ActorName, Allergies));

                // The welcome SMS commits with the patient, or not at all (§9A.2 module 8).
                await SmsSender.SendAsync(s, sms, BranchId, Hms.Notifications.Data.SmsEvent.Registration,
                    p.Phone, new Dictionary<string, string?>
                    { ["hospital"] = hospital.Name, ["patient"] = p.FullName, ["uhid"] = p.Uhid });
                await s.Notif.SaveChangesAsync();
                return p;
            });

            // The name is bounded at 200 above, but the toast is one sentence read at a glance —
            // keep the interpolated part short even if the bound ever widens (spec 0039 WP1.3).
            var shownName = patient.FullName.Length <= 60
                ? patient.FullName : patient.FullName[..60] + "…";
            Toast($"Registered {shownName} — {patient.Uhid}", "badge");
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
