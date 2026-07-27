using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Emr.Data;

// emr.* per spec 0024 (PRD §5 M5, §5A-7). The clinical record: what the patient complained of,
// what was found, what was prescribed. Money never enters this schema — an ordered test becomes
// a bill.charge_line through the Diagnostics contract, exactly as the counter's own order does.
//
// §10's entity table says a prescription is immutable after the visit closes. That is enforced
// here as state + supersession, never as an UPDATE: a correction is a new note pointing at the
// one it replaces, the same shape as a financial reversal (hard rule 4).

public static class NoteState
{
    public const string Draft = "draft";
    public const string Final = "final";
    public const string Superseded = "superseded";
}

public static class DoseState
{
    public const string Scheduled = "scheduled";
    public const string Given = "given";
    public const string Missed = "missed";
    public const string Refused = "refused";
}

/// <summary>
/// One consultation. Outdoor notes hang off a bill.encounter (the visit the counter opened);
/// indoor notes hang off an ipd.admission. Exactly one, enforced by check constraint.
/// </summary>
public class Note
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PatientId { get; set; }
    public long? EncounterId { get; set; }
    public long? AdmissionId { get; set; }
    public long DoctorId { get; set; }
    public string? Complaint { get; set; }
    public string? OnExamination { get; set; }
    public string? Diagnosis { get; set; }
    public string? Advice { get; set; }
    public DateOnly? FollowUpOn { get; set; }
    public string State { get; set; } = NoteState.Draft;
    /// <summary>Set on the *new* note: the finalised note this one corrects (§10 immutability).</summary>
    public long? SupersedesId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? FinalisedAt { get; set; }
    public long? FinalisedBy { get; set; }
}

/// <summary>
/// A prescribed line. `ProductId` points at pharm.product when the drug is on the formulary —
/// which it normally is, because the picker is the pharmacy master (§5 M5 [S]). The name is
/// snapshotted regardless, so a renamed product never rewrites a printed prescription.
/// </summary>
public class NoteDrug
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public long? ProductId { get; set; }
    public required string DrugName { get; set; }
    public string? Dose { get; set; }               // "1 tab", "10 ml"
    public string? Frequency { get; set; }          // "1+0+1", "8 hourly"
    public string? Duration { get; set; }           // "7 days"
    public string? Instruction { get; set; }        // "after food"
    public int Ordinal { get; set; }
}

/// <summary>
/// Pre-checkup vitals (US5.3). Temperature and weight are stored in tenths as integers: a
/// clinical number that has to round-trip through a form is not a job for binary floating point.
/// </summary>
public class Vitals
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PatientId { get; set; }
    public long? EncounterId { get; set; }
    public long? AdmissionId { get; set; }
    public short? Systolic { get; set; }
    public short? Diastolic { get; set; }
    public short? Pulse { get; set; }
    public short? TemperatureTenthsC { get; set; }
    public short? WeightTenthsKg { get; set; }
    public short? SpO2 { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long RecordedBy { get; set; }
}

/// <summary>A doctor's own template (§5 M5 [S]) — the thing that makes US5.1's three minutes real.</summary>
public class NoteTemplate
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public required string Name { get; set; }
    public string? Complaint { get; set; }
    public string? OnExamination { get; set; }
    public string? Diagnosis { get; set; }
    public string? Advice { get; set; }
    /// <summary>Drug lines as JSON: [{drugName,dose,frequency,duration,instruction}].</summary>
    public string Drugs { get; set; } = "[]";
    public bool Active { get; set; } = true;
}

/// <summary>US5.1 AC: a favourite is ≤ 3 keystrokes away because it is already on the screen.</summary>
public class Favourite
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public long ProductId { get; set; }
    public required string DrugName { get; set; }
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    public string? Duration { get; set; }
}

/// <summary>5A-7 medication administration record: one row per scheduled dose.</summary>
public class MarDose
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AdmissionId { get; set; }
    public required string DrugName { get; set; }
    public string? Dose { get; set; }
    public string? Route { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public string State { get; set; } = DoseState.Scheduled;
    public string? StateReason { get; set; }        // why missed or refused
    public DateTimeOffset? AdministeredAt { get; set; }
    public long? AdministeredBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>5A-7 diabetic chart. Glucose in tenths of mmol/L, same reasoning as vitals.</summary>
public class GlucoseReading
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AdmissionId { get; set; }
    public DateTimeOffset At { get; set; }
    public short GlucoseTenths { get; set; }
    public string? Timing { get; set; }             // fasting|pre-meal|post-meal|bedtime
    public short? InsulinUnits { get; set; }
    public string? InsulinRoute { get; set; }
    public long RecordedBy { get; set; }
}

/// <summary>5A-7 patient receive note: the handover record when a patient reaches the ward.</summary>
public class ReceiveNote
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AdmissionId { get; set; }
    public string? ReceivedFrom { get; set; }       // OPD, Emergency, OT, another ward
    public string? Condition { get; set; }
    public string? Belongings { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public long ReceivedBy { get; set; }
}

public class EmrDbContext(DbContextOptions<EmrDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<NoteDrug> NoteDrugs => Set<NoteDrug>();
    public DbSet<Vitals> Vitals => Set<Vitals>();
    public DbSet<NoteTemplate> Templates => Set<NoteTemplate>();
    public DbSet<Favourite> Favourites => Set<Favourite>();
    public DbSet<MarDose> MarDoses => Set<MarDose>();
    public DbSet<GlucoseReading> GlucoseReadings => Set<GlucoseReading>();
    public DbSet<ReceiveNote> ReceiveNotes => Set<ReceiveNote>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("emr");

        b.Entity<Note>(e =>
        {
            e.ToTable("note", t => t.HasCheckConstraint(
                "ck_note_parent", "num_nonnulls(encounter_id, admission_id) = 1"));
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.EncounterId);
            e.HasIndex(x => x.AdmissionId);
            e.HasIndex(x => x.State);
            // A finalised note may be corrected once; a second correction chains off the newer
            // one. Uniqueness stops two doctors superseding the same note in parallel.
            e.HasIndex(x => x.SupersedesId).IsUnique().HasFilter("supersedes_id IS NOT NULL");
        });

        b.Entity<NoteDrug>(e =>
        {
            e.ToTable("note_drug");
            e.HasIndex(x => x.NoteId);
        });

        b.Entity<Vitals>(e =>
        {
            e.ToTable("vitals", t => t.HasCheckConstraint(
                "ck_vitals_parent", "num_nonnulls(encounter_id, admission_id) = 1"));
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.EncounterId);
        });

        b.Entity<NoteTemplate>(e =>
        {
            e.ToTable("template");
            e.HasIndex(x => new { x.DoctorId, x.Name }).IsUnique();
            e.Property(x => x.Drugs).HasColumnType("jsonb");
        });

        b.Entity<Favourite>(e =>
        {
            e.ToTable("favourite");
            e.HasIndex(x => new { x.DoctorId, x.ProductId }).IsUnique();
        });

        b.Entity<MarDose>(e =>
        {
            e.ToTable("mar_dose");
            e.HasIndex(x => new { x.AdmissionId, x.ScheduledAt });
            e.HasIndex(x => x.State);
        });

        b.Entity<GlucoseReading>(e =>
        {
            e.ToTable("glucose_reading");
            e.HasIndex(x => new { x.AdmissionId, x.At });
        });

        b.Entity<ReceiveNote>(e =>
        {
            e.ToTable("receive_note");
            e.HasIndex(x => x.AdmissionId).IsUnique();   // one handover per admission
        });
    }
}

public class EmrDbContextFactory : IDesignTimeDbContextFactory<EmrDbContext>
{
    public EmrDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<EmrDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "emr"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
