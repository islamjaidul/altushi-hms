using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Ipd.Data;

// ipd.* per spec 0017. The folio is the M6 integration spine (PRD §6.3): every chargeable
// indoor event becomes a folio-parented bill.charge_line; this schema holds the clinical/
// occupancy truth around it. Money stays in bill.* — corrections are reversals (hard rule 4).

public static class BedState
{
    public const string Free = "free";
    public const string Reserved = "reserved";
    public const string Occupied = "occupied";
    public const string Cleaning = "cleaning";
    public const string OutOfService = "out_of_service";
}

/// <summary>Ward classes per PRD §5 M6 / US2.1.</summary>
public class Ward
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    public required string Class { get; set; }         // general|cabin|icu|ccu|hdu|nicu
    public bool Active { get; set; } = true;
}

public class Bed
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long WardId { get; set; }
    public required string Code { get; set; }          // e.g. "GW-03", "CAB-2"
    /// <summary>adm.service id whose effective-dated rate is this bed's per-day tariff (hard rule 5).</summary>
    public long TariffServiceId { get; set; }
    public string State { get; set; } = BedState.Free;
    public string? StateReason { get; set; }           // out-of-service note
}

public static class AdmissionState
{
    public const string Reserved = "reserved";
    public const string Admitted = "admitted";
    public const string Blocked = "blocked";                       // R4 due-hold ⚿
    public const string DischargeInitiated = "discharge_initiated";
    public const string ClinicallyCleared = "clinically_cleared";
    public const string FinanciallySettled = "financially_settled";
    public const string Discharged = "discharged";
    public const string Death = "death";
    public const string Absconded = "absconded";
}

public class Admission
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string AdmissionNo { get; set; }
    public long PatientId { get; set; }
    public long? DoctorId { get; set; }                // admitting consultant
    public required string Source { get; set; }        // opd|er|direct
    public string? ProvisionalDx { get; set; }
    public long? PackageId { get; set; }
    /// <summary>5A-9: applied over eligible folio charges at settlement; snapshot at admission.</summary>
    public short ServiceChargePct { get; set; }
    public string State { get; set; } = AdmissionState.Admitted;
    /// <summary>State to restore on R4 release (block can interrupt admitted or discharge flow).</summary>
    public string? BlockedFrom { get; set; }
    public long? BlockApprovalId { get; set; }
    public string? ClinicalSummary { get; set; }
    public DateTimeOffset AdmittedAt { get; set; }
    public DateTimeOffset? DischargedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>Time-stamped bed history (US6.3) — one open stay (ToAt null) per admission.</summary>
public class BedStay
{
    public long Id { get; set; }
    public long AdmissionId { get; set; }
    public long BedId { get; set; }
    public DateTimeOffset FromAt { get; set; }
    public DateTimeOffset? ToAt { get; set; }
}

/// <summary>
/// One charged bed day (P18 rule). The UNIQUE(admission,date) constraint is what makes bed-day
/// catch-up idempotent — a concurrent double-post loses on the index, not on luck.
/// </summary>
public class BedDay
{
    public long Id { get; set; }
    public long AdmissionId { get; set; }
    public DateOnly OnDate { get; set; }
    public long BedId { get; set; }
    public long ChargeLineId { get; set; }
}

public static class FolioState
{
    public const string Open = "open";
    public const string Blocked = "blocked";           // R4 service-hold
    public const string SettlementDraft = "settlement_draft";
    public const string Locked = "locked";
}

public class Folio
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AdmissionId { get; set; }
    public long PatientId { get; set; }
    public string State { get; set; } = FolioState.Open;
    /// <summary>Advance applied to the settlement invoice's due at creation (spec 0017 AC 4).</summary>
    public long AdvanceApplied { get; set; }
    public long? SettlementInvoiceId { get; set; }
}

/// <summary>5A-9 Admission Package master; price lives in the effective-dated rate plan.</summary>
public class AdmissionPackage
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    /// <summary>adm.service id carrying the package's effective-dated price.</summary>
    public long ServiceCatalogId { get; set; }
    public short DefaultServiceChargePct { get; set; }
    public bool Active { get; set; } = true;
}

public static class IndentState
{
    public const string Requested = "requested";
    public const string Issued = "issued";
    public const string Cancelled = "cancelled";
}

/// <summary>5A-9 Medicine Indent — the controlled ward requisition M11 issues against.</summary>
public class Indent
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AdmissionId { get; set; }
    public long FolioId { get; set; }
    public string State { get; set; } = IndentState.Requested;
    public string? Note { get; set; }
    public long RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public long? IssuedBy { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
}

public class IndentItem
{
    public long Id { get; set; }
    public long IndentId { get; set; }
    public long ProductId { get; set; }
    public int QtyRequested { get; set; }
    public int QtyIssued { get; set; }
    public int QtyReturned { get; set; }               // discharge-time return (0016 deferral #11)
}

/// <summary>Discharge/Death/Birth certificates — sequential numbers, reprint audited (§5 M6).</summary>
public class Certificate
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AdmissionId { get; set; }
    public required string Kind { get; set; }          // discharge|death|birth
    public required string CertNo { get; set; }
    public required string Body { get; set; }          // jsonb snapshot of what was printed
    public DateTimeOffset IssuedAt { get; set; }
    public long IssuedBy { get; set; }
    public int PrintCount { get; set; }
}

public class IpdDbContext(DbContextOptions<IpdDbContext> options) : DbContext(options)
{
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<Admission> Admissions => Set<Admission>();
    public DbSet<BedStay> BedStays => Set<BedStay>();
    public DbSet<BedDay> BedDays => Set<BedDay>();
    public DbSet<Folio> Folios => Set<Folio>();
    public DbSet<AdmissionPackage> Packages => Set<AdmissionPackage>();
    public DbSet<Indent> Indents => Set<Indent>();
    public DbSet<IndentItem> IndentItems => Set<IndentItem>();
    public DbSet<Certificate> Certificates => Set<Certificate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("ipd");
        b.Entity<Ward>(e => e.ToTable("ward"));
        b.Entity<Bed>(e =>
        {
            e.ToTable("bed");
            e.HasIndex(x => new { x.WardId, x.Code }).IsUnique();
        });
        b.Entity<Admission>(e =>
        {
            e.ToTable("admission");
            e.HasIndex(x => x.AdmissionNo).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.State);
        });
        b.Entity<BedStay>(e =>
        {
            e.ToTable("bed_stay");
            e.HasIndex(x => x.AdmissionId);
            e.HasIndex(x => x.BedId);
        });
        b.Entity<BedDay>(e =>
        {
            e.ToTable("bed_day");
            e.HasIndex(x => new { x.AdmissionId, x.OnDate }).IsUnique();   // idempotency anchor
        });
        b.Entity<Folio>(e =>
        {
            e.ToTable("folio");
            e.HasIndex(x => x.AdmissionId).IsUnique();
        });
        b.Entity<AdmissionPackage>(e => e.ToTable("admission_package"));
        b.Entity<Indent>(e =>
        {
            e.ToTable("indent");
            e.HasIndex(x => x.State);
        });
        b.Entity<IndentItem>(e => { e.ToTable("indent_item"); e.HasIndex(x => x.IndentId); });
        b.Entity<Certificate>(e =>
        {
            e.ToTable("certificate");
            e.HasIndex(x => x.CertNo).IsUnique();
            e.Property(x => x.Body).HasColumnType("jsonb");
        });
    }
}

public class IpdDbContextFactory : IDesignTimeDbContextFactory<IpdDbContext>
{
    public IpdDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<IpdDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "ipd"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
