using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Appointments.Data;

// appt.* per 03 §3 — §9A.2 keeps this module honestly "lite": serial issue + today's queue.

/// <summary>
/// The doctor master (spec 0039 WP2.5, AUD-ARCH-05). Before this table a doctor existed only as
/// schedule rows and ids were minted MAX+1 — two concurrent additions could merge two clinicians
/// into one identity across nine tables. The id is a real identity column now, and the schedule
/// and appointment tables carry foreign keys to it. Other modules' doctor_id columns remain
/// snapshot references by the ADR-0003 boundary. Deactivation, never deletion — the name must
/// stay readable on every document already issued (§8 N5).
/// </summary>
public class Doctor
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

public class DoctorSchedule
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public required string DoctorName { get; set; }    // display snapshot; master is appt.doctor
    public int Weekday { get; set; }
    public TimeOnly SlotFrom { get; set; }
    public TimeOnly SlotTo { get; set; }
    public string? Room { get; set; }
    public int MaxSerials { get; set; } = 40;
}

public static class AppointmentState
{
    public const string Booked = "booked";
    public const string Arrived = "arrived";
    public const string InChamber = "in_chamber";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string NoShow = "no_show";
}

public class Appointment
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PatientId { get; set; }
    public long DoctorId { get; set; }
    public DateOnly OnDate { get; set; }
    public int SerialNo { get; set; }
    public string State { get; set; } = AppointmentState.Booked;
    public string Source { get; set; } = "counter";
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public class ApptDbContext(DbContextOptions<ApptDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorSchedule> Schedules => Set<DoctorSchedule>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("appt");
        b.Entity<Doctor>(e =>
        {
            e.ToTable("doctor");
            e.Property(x => x.Name).HasMaxLength(200);
        });
        b.Entity<DoctorSchedule>(e =>
        {
            e.ToTable("doctor_schedule");
            e.Property(x => x.DoctorName).HasMaxLength(200);
            e.Property(x => x.Room).HasMaxLength(40);
            // The MAX+1 mint scanned this unindexed column (AUD-ARCH-04); the mint is gone but
            // the lookups ("this doctor's slots") remain.
            e.HasIndex(x => x.DoctorId);
            e.HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId);
            e.ToTable(t => t.HasCheckConstraint("ck_schedule_max_serials", "max_serials > 0"));
        });
        b.Entity<Appointment>(e =>
        {
            e.ToTable("appointment");
            // ADR-0015 #1: one serial per (doctor, day, number) by constraint — losers retry the
            // allocation, the operator sees "Serial N was just taken".
            e.HasIndex(x => new { x.DoctorId, x.OnDate, x.SerialNo }).IsUnique();
            // The queue board, front desk and /public/queue all ask "today, all doctors" — the
            // unique index above cannot serve that, and the table grows forever (AUD-ARCH-04).
            e.HasIndex(x => x.OnDate);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.Source).HasMaxLength(40);
            e.HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId);
            // WP2.2 (AUD-VAL-11): the set mirrors AppointmentState exactly; AdvanceAsync
            // validates the same list, so a bad `to` is refused twice — code and schema.
            e.ToTable(t => t.HasCheckConstraint("ck_appointment_state",
                "state IN ('booked','arrived','in_chamber','done','cancelled','no_show')"));
        });
        // Hard rule 4: the schema must never delete on its own. Every relationship this model
        // declares refuses a parent delete instead of cascading it — corrections are reversals,
        // and a cascade is a delete machine (spec 0039 WP2, ADR-0028).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
    }
}

public class ApptDbContextFactory : IDesignTimeDbContextFactory<ApptDbContext>
{
    public ApptDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<ApptDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "appt"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
