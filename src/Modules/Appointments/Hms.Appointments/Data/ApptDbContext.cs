using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Appointments.Data;

// appt.* per 03 §3 — §9A.2 keeps this module honestly "lite": serial issue + today's queue.

public class DoctorSchedule
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public required string DoctorName { get; set; }    // denormalized until adm.doctor master (S5 masters)
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
    public DbSet<DoctorSchedule> Schedules => Set<DoctorSchedule>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("appt");
        b.Entity<DoctorSchedule>(e => e.ToTable("doctor_schedule"));
        b.Entity<Appointment>(e =>
        {
            e.ToTable("appointment");
            // ADR-0015 #1: one serial per (doctor, day, number) by constraint — losers retry the
            // allocation, the operator sees "Serial N was just taken".
            e.HasIndex(x => new { x.DoctorId, x.OnDate, x.SerialNo }).IsUnique();
        });
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
