using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Ot.Data;

// ot.* per spec 0025 (PRD §5 M7, §11 OT Case). The theatre's own truth: what is scheduled, who
// is operating, what was found, what was used. Money lives in bill.* as always — completion
// posts folio charge lines through the composition root, and this schema keeps only the link.

/// <summary>§11: Scheduled → Patient Ready → In-Theatre → Completed / Cancelled / Postponed.</summary>
public static class CaseState
{
    public const string Scheduled = "scheduled";
    public const string PatientReady = "patient_ready";
    public const string InTheatre = "in_theatre";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Postponed = "postponed";
}

public static class TeamRole
{
    public const string Surgeon = "surgeon";
    public const string Assistant = "assistant";
    public const string Anaesthetist = "anaesthetist";
    public const string Scrub = "scrub";
}

public class Theatre
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// One operation. An indoor case bills to the folio; a day case bills to the counter's visit —
/// exactly one of the two, enforced by check constraint, the same shape as an invoice's parent.
/// </summary>
public class OtCase
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string CaseNo { get; set; }
    public long PatientId { get; set; }
    public long? FolioId { get; set; }
    public long? EncounterId { get; set; }
    public long TheatreId { get; set; }
    /// <summary>adm.service id for the operation itself; its effective-dated rate is the fee.</summary>
    public long OperationServiceId { get; set; }
    public required string OperationName { get; set; }      // snapshot: a renamed catalogue entry
                                                            // must not rewrite an old register line
    public DateTimeOffset ScheduledFrom { get; set; }
    public DateTimeOffset ScheduledTo { get; set; }
    public string State { get; set; } = CaseState.Scheduled;
    public string? AnaesthesiaType { get; set; }
    public string? Findings { get; set; }
    public string? ProcedurePerformed { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? CompletedBy { get; set; }
}

/// <summary>
/// Who did what, and what it was worth. US7.3: the payout data M17 needs is captured at
/// completion, so consultant payments never come from a month-end spreadsheet.
/// </summary>
public class CaseTeamMember
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public required string Role { get; set; }
    public long PersonId { get; set; }
    public required string PersonName { get; set; }         // snapshot, same reasoning as above
    /// <summary>adm.service whose rate is this role's fee for this operation.</summary>
    public long? FeeServiceId { get; set; }
    /// <summary>What actually posted, filled at completion. Zero until then.</summary>
    public long AmountPosted { get; set; }
    public long? ChargeLineId { get; set; }
}

public class CaseConsumable
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public long ProductId { get; set; }
    public required string ProductName { get; set; }
    public int Qty { get; set; }
    public required string BatchNo { get; set; }
    public long UnitPrice { get; set; }
    public long ChargeLineId { get; set; }
    public DateTimeOffset At { get; set; }
    public long IssuedBy { get; set; }
}

public class OtDbContext(DbContextOptions<OtDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<Theatre> Theatres => Set<Theatre>();
    public DbSet<OtCase> Cases => Set<OtCase>();
    public DbSet<CaseTeamMember> Team => Set<CaseTeamMember>();
    public DbSet<CaseConsumable> Consumables => Set<CaseConsumable>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Spec 0039 WP2: domain-value CHECKs, state CHECKs from CaseState above, intra-schema
        // FKs and HasMaxLength — same posture as BillDbContext (additive only, NOT VALID in
        // the migration for pre-existing tables).
        b.HasDefaultSchema("ot");

        b.Entity<Theatre>(e =>
        {
            e.ToTable("theatre");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.BranchId, x.Name }).IsUnique();
        });

        b.Entity<OtCase>(e =>
        {
            e.ToTable("ot_case", t =>
            {
                t.HasCheckConstraint("ck_case_parent", "num_nonnulls(folio_id, encounter_id) = 1");
                t.HasCheckConstraint("ck_case_window", "scheduled_to > scheduled_from");
                t.HasCheckConstraint("ck_case_state",
                    "state IN ('scheduled','patient_ready','in_theatre','completed',"
                    + "'cancelled','postponed')");
                // OtService stamps these on the transitions; completion is only reachable
                // from in_theatre, so a completed case has both timestamps, and both cancel
                // and postpone demand a reason (audit §2).
                t.HasCheckConstraint("ck_case_started",
                    "state NOT IN ('in_theatre','completed') OR started_at IS NOT NULL");
                t.HasCheckConstraint("ck_case_finished",
                    "state <> 'completed' OR finished_at IS NOT NULL");
                t.HasCheckConstraint("ck_case_cancel_reason",
                    "state NOT IN ('cancelled','postponed') OR cancel_reason IS NOT NULL");
            });
            e.Property(x => x.CaseNo).HasMaxLength(40);
            e.Property(x => x.OperationName).HasMaxLength(200);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.AnaesthesiaType).HasMaxLength(40);
            e.Property(x => x.Findings).HasMaxLength(10000);
            e.Property(x => x.ProcedurePerformed).HasMaxLength(10000);
            e.Property(x => x.CancelReason).HasMaxLength(4000);
            e.HasIndex(x => x.CaseNo).IsUnique();
            e.HasIndex(x => new { x.TheatreId, x.ScheduledFrom });
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.PatientId);
            e.HasOne<Theatre>().WithMany().HasForeignKey(x => x.TheatreId);
        });

        b.Entity<CaseTeamMember>(e =>
        {
            e.ToTable("case_team", t => t.HasCheckConstraint(
                "ck_case_team_amount", "amount_posted >= 0"));
            e.Property(x => x.Role).HasMaxLength(40);
            e.Property(x => x.PersonName).HasMaxLength(200);
            // One person holds one role on a case; the same surgeon cannot be billed twice for it.
            e.HasIndex(x => new { x.CaseId, x.Role, x.PersonId }).IsUnique();
            e.HasOne<OtCase>().WithMany().HasForeignKey(x => x.CaseId);
        });

        b.Entity<CaseConsumable>(e =>
        {
            e.ToTable("case_consumable", t => t.HasCheckConstraint(
                "ck_case_consumable_qty", "qty > 0 AND unit_price >= 0"));
            e.Property(x => x.ProductName).HasMaxLength(200);
            e.Property(x => x.BatchNo).HasMaxLength(40);
            e.HasIndex(x => x.CaseId);
            e.HasOne<OtCase>().WithMany().HasForeignKey(x => x.CaseId);
            // product_id points at pharm.product — cross-schema, so no FK (ADR-0003).
        });
        // Hard rule 4: the schema must never delete on its own. Every relationship this model
        // declares refuses a parent delete instead of cascading it — corrections are reversals,
        // and a cascade is a delete machine (spec 0039 WP2, ADR-0028).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
    }
}

public class OtDbContextFactory : IDesignTimeDbContextFactory<OtDbContext>
{
    public OtDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<OtDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "ot"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
