using Hms.Registration.Data;

namespace Hms.Web;

/// <summary>
/// The identity strip every clinical screen shows (spec 0042, §5 M5 "alert flags on every
/// clinical screen"). Built in one place so a screen cannot show less than the safety minimum:
/// the 0042 audit found Charts/Tasks/Indoor showing a bare name while blood group sat unread in
/// reg and allergies did not exist at all — on a screen that prescribes.
/// </summary>
public sealed record PatientBanner(
    string Uhid, string? AgeSex, string? BloodGroup, string? Allergies, string? Diagnosis)
{
    public static PatientBanner Build(Patient patient, string? diagnosis, DateOnly today) => new(
        patient.Uhid,
        $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)} · "
            + Ui.SexShort(patient.Sex),
        patient.BloodGroup,
        patient.Allergies,
        diagnosis);
}
