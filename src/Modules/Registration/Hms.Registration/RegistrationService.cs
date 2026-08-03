using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Registration.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Registration;

public sealed record DuplicateCandidate(long Id, string Uhid, string FullName, string? Phone, short? AgeYears);

public sealed record RegisterPatientCommand(
    long BranchId, string FullName, char Sex, DateOnly? Dob, short? AgeYears, short? AgeMonths,
    bool AgeEstimated, string? Phone, string? Guardian, string? Area, string? Address,
    string? BloodGroup, string? PatientType,
    bool UnknownIdentity, long ActorId, string ActorName, string? Allergies = null);

/// <summary>
/// §9A.2 module 1. UHID issuance + patient insert + audit are one transaction (G19).
/// The dup-warning is non-blocking (edge 23): callers show candidates, the operator decides.
/// </summary>
public sealed class RegistrationService(NumberSeriesService numbers, AuditWriter audit, TimeProvider clock)
{
    /// <summary>UHID series never fiscal-resets (ADR-0004) — scope key is the literal "ALL".</summary>
    public const string UhidSeries = "uhid";
    public const string UhidScope = "ALL";
    public const string UhidFormat = "ALT-{n:D6}";

    /// <summary>Same phone OR (phonetic name match AND age band ±2y) → candidates (03 §2).</summary>
    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        RegDbContext reg, string fullName, string? phone, short? ageYears, CancellationToken ct = default)
    {
        var phoneNorm = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        return await reg.Database.SqlQuery<DuplicateCandidate>($"""
            SELECT id, uhid, full_name, phone, age_years
            FROM reg.patient
            WHERE merged_into IS NULL AND active
              AND (
                    ({phoneNorm}::text IS NOT NULL AND phone = {phoneNorm})
                 OR (name_phonetic = dmetaphone({fullName})
                     AND ({ageYears}::smallint IS NULL OR age_years IS NULL
                          OR abs(age_years - {ageYears}) <= 2))
                  )
            LIMIT 8
            """).ToListAsync(ct);
    }

    /// <summary>Registers inside the caller's transaction; returns the new patient with UHID.</summary>
    public async Task<Patient> RegisterAsync(
        RegDbContext reg, KernelDbContext kernel, RegisterPatientCommand cmd, CancellationToken ct = default)
    {
        // §7 U13 lets an operator type "8 months", and an infant has no whole year to give, so a
        // months-only age is a complete identity — the same claim `ck_identity` now makes in SQL.
        if (cmd.Dob is null && cmd.AgeYears is null && cmd.AgeMonths is null && !cmd.UnknownIdentity)
            throw new ArgumentException(
                "Either DOB or age is required unless unknown-identity (edge 25/26).");

        var (_, uhid) = await numbers.IssueAsync(kernel, cmd.BranchId, UhidSeries, UhidScope, UhidFormat, ct);

        var patient = new Patient
        {
            BranchId = cmd.BranchId,
            Uhid = uhid,
            FullName = cmd.UnknownIdentity && string.IsNullOrWhiteSpace(cmd.FullName)
                ? $"UNKNOWN ({uhid})" : cmd.FullName,
            Sex = cmd.Sex,
            Dob = cmd.Dob,
            // DOB wins over BOTH age columns; age is derived at display (02 §2.2). Years and
            // months are components of one age ("1 y 6 m"), not rivals, so months never clears
            // years — only a DOB clears either.
            AgeYears = cmd.Dob is null ? cmd.AgeYears : null,
            AgeMonths = cmd.Dob is null ? cmd.AgeMonths : null,
            AgeEstimated = cmd.AgeEstimated || cmd.UnknownIdentity,
            // An age is only meaningful with the day it was taken on — true of months as of years.
            AgeAsOf = cmd.Dob is null && (cmd.AgeYears is not null || cmd.AgeMonths is not null)
                ? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) : null,
            Phone = string.IsNullOrWhiteSpace(cmd.Phone) ? null : cmd.Phone.Trim(),
            Guardian = cmd.Guardian,
            Area = cmd.Area,
            Address = cmd.Address,
            BloodGroup = string.IsNullOrWhiteSpace(cmd.BloodGroup) ? null : cmd.BloodGroup.Trim(),
            Allergies = string.IsNullOrWhiteSpace(cmd.Allergies) ? null : cmd.Allergies.Trim(),
            PatientType = string.IsNullOrWhiteSpace(cmd.PatientType) ? "general" : cmd.PatientType.Trim(),
            UnknownIdentity = cmd.UnknownIdentity,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = cmd.ActorId,
        };
        reg.Patients.Add(patient);
        await reg.SaveChangesAsync(ct);

        audit.Append(kernel, cmd.BranchId, cmd.ActorId, cmd.ActorName,
            "patient.register", "reg.patient", patient.Id,
            after: new { patient.Uhid, patient.FullName, patient.Phone });
        await kernel.SaveChangesAsync(ct);
        return patient;
    }
}
