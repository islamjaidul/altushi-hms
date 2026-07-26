namespace Hms.Registration.Contracts;

/// <summary>04 §1: the only way other modules see patients — id, UHID, banner data.</summary>
public sealed record PatientBanner(
    long Id, string Uhid, string FullName, char Sex, string AgeDisplay, string? Phone, bool HasDue);

public interface IPatientLookup
{
    Task<PatientBanner?> FindAsync(long patientId, CancellationToken ct = default);
}
