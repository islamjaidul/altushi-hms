using Hms.Registration.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// The one patient-matching rule (spec 0020, ADR-0020 §2: predicate in SQL, never
/// fetch-then-filter). An operator types what is in front of them — a name fragment, a UHID,
/// or a phone number **as digits** — and the same query understands all three. Phone matching
/// runs against the database's generated <c>phone_digits</c> column, so the stored display
/// form (`01712-345999`) and the typed form (`01712345999`, `+8801712345999`) agree.
/// </summary>
public static class PatientSearch
{
    /// <summary>Below this, a digit run is more likely an age or a serial than a phone.</summary>
    private const int MinPhoneDigits = 4;

    /// <summary>The digits in the term, or null when it does not look like a number search.</summary>
    public static string? PhoneDigitsOf(string term)
    {
        var digits = new string(term.Where(char.IsDigit).ToArray());
        if (digits.Length < MinPhoneDigits) return null;
        // Typed with the country code: 8801712345999 / +8801712345999 → 01712345999.
        if (digits.StartsWith("880") && digits.Length > 3) digits = "0" + digits[3..];
        return digits;
    }

    /// <summary>Name / UHID / phone-digits match, evaluated entirely in SQL.</summary>
    public static IQueryable<Patient> Matching(this IQueryable<Patient> patients, string term)
    {
        var like = $"%{term.Trim()}%";
        var digits = PhoneDigitsOf(term);
        if (digits is null)
        {
            return patients.Where(p =>
                EF.Functions.ILike(p.FullName, like) || EF.Functions.ILike(p.Uhid, like));
        }

        var digitLike = $"%{digits}%";
        return patients.Where(p =>
            EF.Functions.ILike(p.FullName, like) ||
            EF.Functions.ILike(p.Uhid, like) ||
            (p.PhoneDigits != null && EF.Functions.ILike(p.PhoneDigits, digitLike)));
    }

    /// <summary>Only live, unmerged records are offered anywhere (edge 23).</summary>
    public static IQueryable<Patient> Searchable(this IQueryable<Patient> patients)
        => patients.Where(p => p.Active && p.MergedInto == null);
}
