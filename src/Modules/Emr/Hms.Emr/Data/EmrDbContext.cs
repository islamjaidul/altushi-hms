using Hms.Kernel.Data;
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

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
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
        // Spec 0039 WP2: domain-value CHECKs, state CHECKs from the *State constants above,
        // intra-schema FKs and HasMaxLength — same posture as BillDbContext (additive only,
        // NOT VALID in the migration for pre-existing tables).
        b.HasDefaultSchema("emr");

        b.Entity<Note>(e =>
        {
            e.ToTable("note", t =>
            {
                t.HasCheckConstraint(
                    "ck_note_parent", "num_nonnulls(encounter_id, admission_id) = 1");
                t.HasCheckConstraint("ck_note_state",
                    "state IN ('draft','final','superseded')");
                // A final or superseded note without its finalisation stamp is an unsigned
                // legal record (audit §2).
                t.HasCheckConstraint("ck_note_finalised",
                    "state NOT IN ('final','superseded') "
                    + "OR (finalised_at IS NOT NULL AND finalised_by IS NOT NULL)");
            });
            e.Property(x => x.Complaint).HasMaxLength(10000);
            e.Property(x => x.OnExamination).HasMaxLength(10000);
            e.Property(x => x.Diagnosis).HasMaxLength(10000);
            e.Property(x => x.Advice).HasMaxLength(10000);
            e.Property(x => x.State).HasMaxLength(40);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.EncounterId);
            e.HasIndex(x => x.AdmissionId);
            e.HasIndex(x => x.State);
            // A finalised note may be corrected once; a second correction chains off the newer
            // one. Uniqueness stops two doctors superseding the same note in parallel.
            e.HasIndex(x => x.SupersedesId).IsUnique().HasFilter("supersedes_id IS NOT NULL");
            e.HasOne<Note>().WithMany().HasForeignKey(x => x.SupersedesId);
        });

        b.Entity<NoteDrug>(e =>
        {
            e.ToTable("note_drug");
            e.Property(x => x.DrugName).HasMaxLength(200);
            e.Property(x => x.Dose).HasMaxLength(40);
            e.Property(x => x.Frequency).HasMaxLength(40);
            e.Property(x => x.Duration).HasMaxLength(40);
            e.Property(x => x.Instruction).HasMaxLength(200);
            e.HasIndex(x => x.NoteId);
            e.HasOne<Note>().WithMany().HasForeignKey(x => x.NoteId);
            // product_id points at pharm.product — cross-schema, so no FK (ADR-0003).
        });

        b.Entity<Vitals>(e =>
        {
            e.ToTable("vitals", t =>
            {
                t.HasCheckConstraint(
                    "ck_vitals_parent", "num_nonnulls(encounter_id, admission_id) = 1");
                // The SpO2=999 / BP −1/−9 defect class (audit §1d). All columns nullable —
                // absent is fine, impossible is not. One constraint per measurement so a
                // 23514 names the field the operator mistyped.
                t.HasCheckConstraint("ck_vitals_spo2",
                    "sp_o2 IS NULL OR sp_o2 BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_vitals_pulse",
                    "pulse IS NULL OR pulse BETWEEN 20 AND 300");
                t.HasCheckConstraint("ck_vitals_systolic",
                    "systolic IS NULL OR systolic BETWEEN 40 AND 300");
                t.HasCheckConstraint("ck_vitals_diastolic",
                    "diastolic IS NULL OR diastolic BETWEEN 20 AND 200");
                t.HasCheckConstraint("ck_vitals_temperature",
                    "temperature_tenths_c IS NULL OR temperature_tenths_c BETWEEN 250 AND 460");
                t.HasCheckConstraint("ck_vitals_weight",
                    "weight_tenths_kg IS NULL OR weight_tenths_kg BETWEEN 1 AND 4000");
            });
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.EncounterId);
        });

        b.Entity<NoteTemplate>(e =>
        {
            e.ToTable("template");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Complaint).HasMaxLength(10000);
            e.Property(x => x.OnExamination).HasMaxLength(10000);
            e.Property(x => x.Diagnosis).HasMaxLength(10000);
            e.Property(x => x.Advice).HasMaxLength(10000);
            e.HasIndex(x => new { x.DoctorId, x.Name }).IsUnique();
            e.Property(x => x.Drugs).HasColumnType("jsonb");
        });

        b.Entity<Favourite>(e =>
        {
            e.ToTable("favourite");
            e.Property(x => x.DrugName).HasMaxLength(200);
            e.Property(x => x.Dose).HasMaxLength(40);
            e.Property(x => x.Frequency).HasMaxLength(40);
            e.Property(x => x.Duration).HasMaxLength(40);
            e.HasIndex(x => new { x.DoctorId, x.ProductId }).IsUnique();
        });

        b.Entity<MarDose>(e =>
        {
            e.ToTable("mar_dose", t =>
            {
                t.HasCheckConstraint("ck_mar_dose_state",
                    "state IN ('scheduled','given','missed','refused')");
                // RecordDose stamps administered_at/by for every outcome and requires a reason
                // when the dose was not given — the chart is a legal record (audit §2).
                t.HasCheckConstraint("ck_mar_dose_given",
                    "state <> 'given' "
                    + "OR (administered_at IS NOT NULL AND administered_by IS NOT NULL)");
                t.HasCheckConstraint("ck_mar_dose_reason",
                    "state NOT IN ('missed','refused') OR state_reason IS NOT NULL");
            });
            e.Property(x => x.DrugName).HasMaxLength(200);
            e.Property(x => x.Dose).HasMaxLength(40);
            e.Property(x => x.Route).HasMaxLength(40);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.StateReason).HasMaxLength(4000);
            e.HasIndex(x => new { x.AdmissionId, x.ScheduledAt });
            e.HasIndex(x => x.State);
        });

        b.Entity<GlucoseReading>(e =>
        {
            e.ToTable("glucose_reading", t =>
            {
                t.HasCheckConstraint("ck_glucose_value",
                    "glucose_tenths BETWEEN 5 AND 500");
                t.HasCheckConstraint("ck_glucose_insulin",
                    "insulin_units IS NULL OR insulin_units BETWEEN 0 AND 200");
            });
            e.Property(x => x.Timing).HasMaxLength(40);
            e.Property(x => x.InsulinRoute).HasMaxLength(40);
            e.HasIndex(x => new { x.AdmissionId, x.At });
        });

        b.Entity<ReceiveNote>(e =>
        {
            e.ToTable("receive_note");
            e.Property(x => x.ReceivedFrom).HasMaxLength(200);
            e.Property(x => x.Condition).HasMaxLength(10000);
            e.Property(x => x.Belongings).HasMaxLength(4000);
            e.HasIndex(x => x.AdmissionId).IsUnique();   // one handover per admission
        });
        // Hard rule 4: the schema must never delete on its own. Every relationship this model
        // declares refuses a parent delete instead of cascading it — corrections are reversals,
        // and a cascade is a delete machine (spec 0039 WP2, ADR-0028).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
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
