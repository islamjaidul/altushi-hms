using Hms.Kernel.Data;
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

/// <summary>
/// Spec 0046: the clinical unit above wards — Medicine, Critical Care, Private Wing… A ward
/// belongs to at most one department; nursing staff are scoped to departments via
/// <see cref="DepartmentStaff"/>. Lives in ipd (P27: ward vocabulary is module-owned), and
/// membership is read per request rather than baked into a cookie claim, so an assignment
/// change takes effect on the next page load.
/// </summary>
public class Department
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>A user's standing assignment to a department (spec 0046). Same posture as
/// <see cref="DutyAssignment"/>: user_id is an adm scalar with no FK (ADR-0003), staff_name is
/// a snapshot so the row reads without joining adm, removal is deactivate, never delete.</summary>
public class DepartmentStaff
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long DepartmentId { get; set; }
    public long UserId { get; set; }                   // adm.app_user — cross-schema, no FK
    public required string StaffName { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Ward classes per PRD §5 M6 / US2.1.</summary>
public class Ward
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long? DepartmentId { get; set; }            // spec 0046 — null = not yet grouped
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

/// <summary>
/// One consultant's round on one admission on one day — §5 M6 [M] "which consultant saw patient
/// which day", the fact M17 will compute payouts from (spec 0042). Written by the composition
/// root when an indoor prescription is signed; the unique (admission, doctor, day) key is the
/// BedDay-style idempotency anchor, so five notes in one round still make one visit and one
/// charge. `ChargeLineId` is null when the folio was not postable at signing (R4 hold) — the
/// visit is still a fact; the money follows the late-post path.
/// </summary>
public class ConsultantVisit
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AdmissionId { get; set; }
    public long DoctorId { get; set; }                 // adm doctor master — cross-schema, no FK
    public DateOnly OnDate { get; set; }
    public long? NoteId { get; set; }                  // emr.note — cross-schema, no FK
    public long? ChargeLineId { get; set; }            // bill.charge_line — cross-schema, no FK
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public static class DutyShift
{
    public const string Morning = "morning";
    public const string Evening = "evening";
    public const string Night = "night";
}

public static class DutyRole
{
    public const string Nurse = "nurse";
    public const string WardBoy = "ward-boy";
    public const string Aya = "aya";
}

/// <summary>
/// R5 (spec 0041): who covers this ward, this shift, this day — M6's `[S]` duty-assignment item.
/// Ward vocabulary stays in ipd (P27); employee_id is an hr scalar with no FK (ADR-0003), and
/// staff_name is a snapshot so the row still reads when hr is empty or the aya was never hired
/// into HR. Removal is deactivate-with-reason, never delete.
/// </summary>
public class DutyAssignment
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long WardId { get; set; }
    public DateOnly OnDate { get; set; }
    public required string ShiftLabel { get; set; }    // morning|evening|night
    public required string StaffRole { get; set; }     // nurse|ward-boy|aya
    public long? EmployeeId { get; set; }              // hr.employee — cross-schema, no FK
    public required string StaffName { get; set; }
    public bool Active { get; set; } = true;
    public string? EndedReason { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long? EndedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
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

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentStaff> DepartmentStaff => Set<DepartmentStaff>();
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
    public DbSet<DutyAssignment> DutyAssignments => Set<DutyAssignment>();
    public DbSet<ConsultantVisit> ConsultantVisits => Set<ConsultantVisit>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Spec 0039 WP2: domain-value CHECKs, state CHECKs from the *State constants above,
        // intra-schema FKs, HasMaxLength and xmin tokens — same posture as BillDbContext
        // (additive only, NOT VALID in the migration for pre-existing tables).
        //
        // MIGRATION-SQL: the two Tier-2 constraints below cannot be expressed fluently.
        // The orchestrator must append these statements to the generated migration:
        //
        //   CREATE EXTENSION IF NOT EXISTS btree_gist;
        //
        //   -- Tier 2 #9: one admission cannot occupy two overlapping stays, and one bed
        //   -- cannot host two overlapping stays (open stay = to_at NULL = unbounded range).
        //   ALTER TABLE ipd.bed_stay ADD CONSTRAINT ex_bed_stay_admission
        //     EXCLUDE USING gist (admission_id WITH =,
        //       tstzrange(from_at, COALESCE(to_at, 'infinity'::timestamptz)) WITH &&);
        //   ALTER TABLE ipd.bed_stay ADD CONSTRAINT ex_bed_stay_bed
        //     EXCLUDE USING gist (bed_id WITH =,
        //       tstzrange(from_at, COALESCE(to_at, 'infinity'::timestamptz)) WITH &&);
        //
        //   -- Tier 2 #10: one open admission per patient (terminal states verified against
        //   -- AdmissionState above) — turns IpdService's read-then-write check into a constraint.
        //   CREATE UNIQUE INDEX ux_admission_open_per_patient ON ipd.admission (patient_id)
        //     WHERE state NOT IN ('discharged','death','absconded');
        b.HasDefaultSchema("ipd");
        b.Entity<Department>(e =>
        {
            e.ToTable("department");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.BranchId, x.Name }).IsUnique();
        });
        b.Entity<DepartmentStaff>(e =>
        {
            e.ToTable("department_staff");
            e.Property(x => x.StaffName).HasMaxLength(200);
            // One active assignment per user per department; deactivate-then-reassign is legal
            // (the history keeps both rows — same shape as duty_assignment).
            e.HasIndex(x => new { x.DepartmentId, x.UserId }).IsUnique().HasFilter("active");
            e.HasIndex(x => x.UserId);                   // the station's hottest predicate
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId);
        });
        b.Entity<Ward>(e =>
        {
            e.ToTable("ward");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Class).HasMaxLength(40);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId);
        });
        b.Entity<Bed>(e =>
        {
            e.ToTable("bed", t => t.HasCheckConstraint("ck_bed_state",
                "state IN ('free','reserved','occupied','cleaning','out_of_service')"));
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.StateReason).HasMaxLength(4000);
            e.HasIndex(x => new { x.WardId, x.Code }).IsUnique();
            e.HasIndex(x => x.State);                    // every bed picker filters state='free'
            e.HasOne<Ward>().WithMany().HasForeignKey(x => x.WardId);
        });
        b.Entity<Admission>(e =>
        {
            e.ToTable("admission", t =>
            {
                t.HasCheckConstraint("ck_admission_state",
                    "state IN ('reserved','admitted','blocked','discharge_initiated',"
                    + "'clinically_cleared','financially_settled','discharged','death','absconded')");
                // blocked_from holds the state to restore on R4 release — same legal set.
                t.HasCheckConstraint("ck_admission_blocked_from",
                    "blocked_from IS NULL OR blocked_from IN ('reserved','admitted','blocked',"
                    + "'discharge_initiated','clinically_cleared','financially_settled',"
                    + "'discharged','death','absconded')");
                t.HasCheckConstraint("ck_admission_service_charge_pct",
                    "service_charge_pct BETWEEN 0 AND 100");
                // Every terminal path (discharge, death, abscond) stamps discharged_at (audit §2).
                t.HasCheckConstraint("ck_admission_discharged",
                    "state NOT IN ('discharged','death','absconded') OR discharged_at IS NOT NULL");
            });
            e.Property(x => x.AdmissionNo).HasMaxLength(40);
            e.Property(x => x.Source).HasMaxLength(40);
            e.Property(x => x.ProvisionalDx).HasMaxLength(10000);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.BlockedFrom).HasMaxLength(40);
            e.Property(x => x.ClinicalSummary).HasMaxLength(10000);
            e.HasIndex(x => x.AdmissionNo).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.State);
            // PostgreSQL maintains xmin on every write — a token that cannot be forgotten
            // (the AUD-ARCH-02 lesson from bill.invoice).
            e.Property<uint>("xmin").IsRowVersion();
        });
        b.Entity<BedStay>(e =>
        {
            e.ToTable("bed_stay");
            e.HasIndex(x => x.AdmissionId);
            e.HasIndex(x => x.BedId);
            // The board's hottest predicate: the open stay for an admission (audit §5).
            e.HasIndex(x => x.AdmissionId, "ix_bed_stay_open_admission")
                .HasFilter("to_at IS NULL");
            e.HasOne<Admission>().WithMany().HasForeignKey(x => x.AdmissionId);
            e.HasOne<Bed>().WithMany().HasForeignKey(x => x.BedId);
        });
        b.Entity<BedDay>(e =>
        {
            e.ToTable("bed_day");
            e.HasIndex(x => new { x.AdmissionId, x.OnDate }).IsUnique();   // idempotency anchor
            e.HasOne<Admission>().WithMany().HasForeignKey(x => x.AdmissionId);
            e.HasOne<Bed>().WithMany().HasForeignKey(x => x.BedId);
        });
        b.Entity<Folio>(e =>
        {
            e.ToTable("folio", t =>
            {
                t.HasCheckConstraint("ck_folio_state",
                    "state IN ('open','blocked','settlement_draft','locked')");
                t.HasCheckConstraint("ck_folio_advance", "advance_applied >= 0");
                // Locking and the settlement invoice are one write (FolioService) — a locked
                // folio with no settlement invoice is a settled stay with no bill (audit §2).
                t.HasCheckConstraint("ck_folio_locked",
                    "state <> 'locked' OR settlement_invoice_id IS NOT NULL");
            });
            e.Property(x => x.State).HasMaxLength(40);
            e.HasIndex(x => x.AdmissionId).IsUnique();
            e.HasOne<Admission>().WithMany().HasForeignKey(x => x.AdmissionId);
            e.Property<uint>("xmin").IsRowVersion();
        });
        b.Entity<AdmissionPackage>(e =>
        {
            e.ToTable("admission_package", t => t.HasCheckConstraint(
                "ck_admission_package_pct", "default_service_charge_pct BETWEEN 0 AND 100"));
            e.Property(x => x.Name).HasMaxLength(200);
        });
        b.Entity<Indent>(e =>
        {
            e.ToTable("indent", t =>
            {
                t.HasCheckConstraint("ck_indent_state",
                    "state IN ('requested','issued','cancelled')");
                t.HasCheckConstraint("ck_indent_issued",
                    "state <> 'issued' OR (issued_by IS NOT NULL AND issued_at IS NOT NULL)");
            });
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.Note).HasMaxLength(4000);
            e.HasIndex(x => x.State);
            e.HasOne<Admission>().WithMany().HasForeignKey(x => x.AdmissionId);
            e.HasOne<Folio>().WithMany().HasForeignKey(x => x.FolioId);
        });
        b.Entity<IndentItem>(e =>
        {
            e.ToTable("indent_item", t => t.HasCheckConstraint("ck_indent_item_qty",
                "qty_requested > 0 AND qty_issued BETWEEN 0 AND qty_requested "
                + "AND qty_returned BETWEEN 0 AND qty_issued"));
            e.HasIndex(x => x.IndentId);
            e.HasOne<Indent>().WithMany().HasForeignKey(x => x.IndentId);
            // product_id points at pharm.product — cross-schema, so no FK (ADR-0003).
        });
        b.Entity<DutyAssignment>(e =>
        {
            e.ToTable("duty_assignment", t =>
            {
                t.HasCheckConstraint("ck_duty_shift",
                    "shift_label IN ('morning','evening','night')");
                t.HasCheckConstraint("ck_duty_role",
                    "staff_role IN ('nurse','ward-boy','aya')");
                // An ended assignment keeps who ended it and why — the row is the history
                // (audit §2, same posture as bed out-of-service).
                t.HasCheckConstraint("ck_duty_ended",
                    "active OR (ended_reason IS NOT NULL "
                    + "AND ended_at IS NOT NULL AND ended_by IS NOT NULL)");
            });
            e.Property(x => x.ShiftLabel).HasMaxLength(40);
            e.Property(x => x.StaffRole).HasMaxLength(40);
            e.Property(x => x.StaffName).HasMaxLength(200);
            e.Property(x => x.EndedReason).HasMaxLength(4000);
            // One person once per ward/shift/day *while active* — partial, so end-with-reason
            // followed by reassignment of the same name is legal (the history keeps both rows).
            e.HasIndex(x => new { x.WardId, x.OnDate, x.ShiftLabel, x.StaffName })
                .IsUnique().HasFilter("active");
            e.HasIndex(x => new { x.OnDate, x.WardId });
            e.HasOne<Ward>().WithMany().HasForeignKey(x => x.WardId);
        });
        b.Entity<ConsultantVisit>(e =>
        {
            e.ToTable("consultant_visit");
            // One visit per doctor per admission per day — the idempotency anchor that makes
            // "sign twice" and "two notes in one round" charge once (same shape as bed_day).
            e.HasIndex(x => new { x.AdmissionId, x.DoctorId, x.OnDate }).IsUnique();
            e.HasIndex(x => new { x.DoctorId, x.OnDate });     // M17's future read: a day's rounds
            e.HasOne<Admission>().WithMany().HasForeignKey(x => x.AdmissionId);
        });
        b.Entity<Certificate>(e =>
        {
            e.ToTable("certificate");
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.CertNo).HasMaxLength(40);
            e.HasIndex(x => x.CertNo).IsUnique();
            e.Property(x => x.Body).HasColumnType("jsonb");
            e.HasOne<Admission>().WithMany().HasForeignKey(x => x.AdmissionId);
        });
        // Hard rule 4: the schema must never delete on its own. Every relationship this model
        // declares refuses a parent delete instead of cascading it — corrections are reversals,
        // and a cascade is a delete machine (spec 0039 WP2, ADR-0028).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
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
