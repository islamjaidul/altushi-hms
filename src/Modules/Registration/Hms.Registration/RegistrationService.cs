using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Registration.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Registration;

/// <summary>
/// A patient the operator should look at before registering a new one. <paramref name="NameMatch"/>
/// and <paramref name="PhoneMatch"/> say *why* the row came back, because the two mean very
/// different things to a receptionist and spec 0043 requires the screen to tell them apart.
/// </summary>
public sealed record DuplicateCandidate(
    long Id, string Uhid, string FullName, string? Phone, short? AgeYears, char Sex,
    bool NameMatch, bool PhoneMatch)
{
    /// <summary>
    /// A phone match and nothing else. In Bangladesh one mobile routinely serves a whole
    /// household, so this is usually a family member, not a duplicate — presenting it as one
    /// trains the operator to dismiss the warning that will one day be real (spec 0043).
    /// </summary>
    public bool IsPhoneOnly => PhoneMatch && !NameMatch;
}

/// <summary>
/// Spec 0045. Note what is <b>absent</b>: no UHID, no branch, no creation stamps. The identity
/// the rest of the product refers to cannot be reached by any posted field (AC4).
/// </summary>
public sealed record UpdatePatientCommand(
    long PatientId, string? FullName, char Sex, DateOnly? Dob, short? AgeYears, short? AgeMonths,
    bool AgeEstimated, string? Phone, string? Guardian, string? Area, string? Address,
    string? BloodGroup, string? PatientType, long ActorId, string ActorName,
    string? Allergies = null);

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
    public const string UhidFormat = "SEH-{n:D6}";   // Sylhet Evergreen Hospital (spec 0050)

    /// <summary>
    /// Same phone OR (phonetic name match AND age band ±2y) → candidates (03 §2).
    /// <para>
    /// The rule is unchanged by spec 0043; what changed is that each row now carries which of the
    /// two branches matched it, so the screen can say "same phone — probably a family member"
    /// instead of calling a wife a possible duplicate of her husband. Both branches are projected
    /// rather than re-derived in C#: `dmetaphone` lives in the database and the caller has no way
    /// to reproduce it.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        RegDbContext reg, string fullName, string? phone, short? ageYears, CancellationToken ct = default)
    {
        var phoneNorm = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        return await reg.Database.SqlQuery<DuplicateCandidate>($"""
            SELECT id, uhid, full_name, phone, age_years, sex,
                   (name_phonetic = dmetaphone({fullName})
                    AND ({ageYears}::smallint IS NULL OR age_years IS NULL
                         OR abs(age_years - {ageYears}) <= 2))          AS name_match,
                   ({phoneNorm}::text IS NOT NULL AND phone = {phoneNorm}) AS phone_match
            FROM reg.patient
            WHERE merged_into IS NULL AND active
              AND (
                    ({phoneNorm}::text IS NOT NULL AND phone = {phoneNorm})
                 OR (name_phonetic = dmetaphone({fullName})
                     AND ({ageYears}::smallint IS NULL OR age_years IS NULL
                          OR abs(age_years - {ageYears}) <= 2))
                  )
            -- Real duplicates first: a name match is the one worth reading.
            ORDER BY (name_phonetic = dmetaphone({fullName})) DESC, id DESC
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

    /// <summary>
    /// Completes or corrects an existing patient's identity (spec 0045, US1.4 "details completed
    /// later"). Until this existed the service could only insert, so a casualty registered with
    /// no name kept <c>UNKNOWN (ALT-…)</c> for life and the only way to give him his name was a
    /// second UHID — the exact split §5 M1 exists to prevent.
    /// <para>
    /// <b>UHID, branch and creation stamps are not on the command at all.</b> Refusing to carry
    /// them is stronger than validating them: there is no posted field that could reach them.
    /// </para>
    /// </summary>
    public async Task<Patient> UpdateAsync(
        RegDbContext reg, KernelDbContext kernel, UpdatePatientCommand cmd,
        CancellationToken ct = default)
    {
        var patient = await reg.Patients.SingleOrDefaultAsync(p => p.Id == cmd.PatientId, ct)
            ?? throw new ArgumentException("That patient no longer exists.");

        var named = !string.IsNullOrWhiteSpace(cmd.FullName);

        // The same asymmetry registration has, for the same reason: the ER path legitimately has
        // no name, ordinary correction never does. An identified patient cannot be un-named.
        if (!named && !patient.UnknownIdentity)
            throw new ArgumentException("A patient's name cannot be removed.");

        if (cmd.Dob is null && cmd.AgeYears is null && cmd.AgeMonths is null
            && !patient.UnknownIdentity)
            throw new ArgumentException("Either DOB or age is required.");

        // The diff is captured BEFORE anything is assigned — an audit that records only the new
        // value cannot answer "what did it say before?", which is the one question anyone asks
        // of a correction (hard rule 4).
        var changes = new Dictionary<string, object?>();
        void Track(string field, object? from, object? to)
        {
            if (!Equals(from?.ToString(), to?.ToString()))
                changes[field] = new { from, to };
        }

        var newName = named ? cmd.FullName!.Trim() : patient.FullName;
        var newPhone = string.IsNullOrWhiteSpace(cmd.Phone) ? null : cmd.Phone.Trim();

        Track("full_name", patient.FullName, newName);
        Track("sex", patient.Sex, cmd.Sex);
        Track("dob", patient.Dob, cmd.Dob);
        Track("age_years", patient.AgeYears, cmd.Dob is null ? cmd.AgeYears : null);
        Track("age_months", patient.AgeMonths, cmd.Dob is null ? cmd.AgeMonths : null);
        Track("phone", patient.Phone, newPhone);
        Track("guardian", patient.Guardian, cmd.Guardian);
        Track("area", patient.Area, cmd.Area);
        Track("address", patient.Address, cmd.Address);
        Track("blood_group", patient.BloodGroup, cmd.BloodGroup);
        Track("allergies", patient.Allergies, cmd.Allergies);
        Track("patient_type", patient.PatientType, cmd.PatientType);
        // Giving a casualty his name is the moment he stops being a casualty on paper.
        if (patient.UnknownIdentity && named) Track("unknown_identity", true, false);

        patient.FullName = newName;
        patient.Sex = cmd.Sex;
        patient.Dob = cmd.Dob;
        // DOB wins over BOTH age columns, exactly as at registration (02 §2.2).
        patient.AgeYears = cmd.Dob is null ? cmd.AgeYears : null;
        patient.AgeMonths = cmd.Dob is null ? cmd.AgeMonths : null;
        patient.AgeAsOf = cmd.Dob is null && (cmd.AgeYears is not null || cmd.AgeMonths is not null)
            ? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) : patient.AgeAsOf;
        patient.Phone = newPhone;
        patient.Guardian = string.IsNullOrWhiteSpace(cmd.Guardian) ? null : cmd.Guardian.Trim();
        patient.Area = string.IsNullOrWhiteSpace(cmd.Area) ? null : cmd.Area.Trim();
        patient.Address = string.IsNullOrWhiteSpace(cmd.Address) ? null : cmd.Address.Trim();
        patient.BloodGroup = string.IsNullOrWhiteSpace(cmd.BloodGroup) ? null : cmd.BloodGroup.Trim();
        patient.Allergies = string.IsNullOrWhiteSpace(cmd.Allergies) ? null : cmd.Allergies.Trim();
        if (!string.IsNullOrWhiteSpace(cmd.PatientType)) patient.PatientType = cmd.PatientType.Trim();

        if (named && patient.UnknownIdentity)
        {
            patient.UnknownIdentity = false;
            // The age was "estimated" only because nobody knew it. A real one is not an estimate.
            if (cmd.Dob is not null || cmd.AgeYears is not null || cmd.AgeMonths is not null)
                patient.AgeEstimated = cmd.AgeEstimated;
        }

        await reg.SaveChangesAsync(ct);

        audit.Append(kernel, patient.BranchId, cmd.ActorId, cmd.ActorName,
            "patient.update", "reg.patient", patient.Id,
            after: new { patient.Uhid, changed = changes });
        await kernel.SaveChangesAsync(ct);
        return patient;
    }
}
