using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Hr.Data;

/// <summary>
/// The <c>hr</c> schema (11-build-plan-phase2 §3 names it). One schema per module, and nothing else
/// writes to it (ADR-0003).
/// <para>
/// House style keeps entities and context in one file; this module has enough of them that they are
/// grouped into <c>HrEntities.*.cs</c> instead. The context and its design-time factory stay here,
/// where every other module puts them. The deviation is recorded in the spec's notes.
/// </para>
/// </summary>
public class HrDbContext(DbContextOptions<HrDbContext> options) : DbContext(options)
{
    // masters
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<PayScale> PayScales => Set<PayScale>();
    public DbSet<PayComponent> PayComponents => Set<PayComponent>();
    public DbSet<WorkLocation> WorkLocations => Set<WorkLocation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<WeeklyOffPattern> WeeklyOffPatterns => Set<WeeklyOffPattern>();
    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();

    // people
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeAssignment> Assignments => Set<EmployeeAssignment>();
    public DbSet<EmployeePayStructure> PayStructures => Set<EmployeePayStructure>();
    public DbSet<EmployeePayComponent> PayStructureComponents => Set<EmployeePayComponent>();
    public DbSet<EmploymentEvent> EmploymentEvents => Set<EmploymentEvent>();

    // time
    public DbSet<Roster> Rosters => Set<Roster>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<Punch> Punches => Set<Punch>();
    public DbSet<AttendanceDay> AttendanceDays => Set<AttendanceDay>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<PunchImportBatch> PunchImportBatches => Set<PunchImportBatch>();

    // leave
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<LeaveEncashment> LeaveEncashments => Set<LeaveEncashment>();

    // pay
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollLine> PayrollLines => Set<PayrollLine>();
    public DbSet<PayrollComponentLine> PayrollComponentLines => Set<PayrollComponentLine>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanInstallment> LoanInstallments => Set<LoanInstallment>();
    public DbSet<EmployeeLedgerEntry> LedgerEntries => Set<EmployeeLedgerEntry>();

    // policy (ADR-0027)
    public DbSet<PayrollPolicy> PayrollPolicies => Set<PayrollPolicy>();
    public DbSet<TaxSlab> TaxSlabs => Set<TaxSlab>();
    public DbSet<PfPolicy> PfPolicies => Set<PfPolicy>();
    public DbSet<GratuityRule> GratuityRules => Set<GratuityRule>();
    public DbSet<OvertimeRule> OvertimeRules => Set<OvertimeRule>();
    public DbSet<GraceTimeRule> GraceTimeRules => Set<GraceTimeRule>();
    public DbSet<HolidayPayPolicy> HolidayPayPolicies => Set<HolidayPayPolicy>();
    public DbSet<DeductionRule> DeductionRules => Set<DeductionRule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("hr");

        // ---- masters
        b.Entity<OrgUnit>(e =>
        {
            e.ToTable("org_unit");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
            e.HasIndex(x => x.ParentId);
        });
        b.Entity<Designation>(e =>
        {
            e.ToTable("designation");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });
        b.Entity<Grade>(e =>
        {
            e.ToTable("grade");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });
        b.Entity<PayScale>(e =>
        {
            e.ToTable("pay_scale");
            e.HasIndex(x => new { x.GradeId, x.EffectiveFrom });
        });
        b.Entity<PayComponent>(e =>
        {
            e.ToTable("pay_component");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });
        b.Entity<WorkLocation>(e =>
        {
            e.ToTable("work_location");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });
        b.Entity<Shift>(e =>
        {
            e.ToTable("shift");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });
        b.Entity<WeeklyOffPattern>(e =>
        {
            e.ToTable("weekly_off_pattern");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
        b.Entity<HolidayCalendar>(e => e.ToTable("holiday_calendar"));
        b.Entity<Holiday>(e =>
        {
            e.ToTable("holiday");
            e.HasIndex(x => new { x.CalendarId, x.OnDate }).IsUnique();
        });
        b.Entity<LeaveType>(e =>
        {
            e.ToTable("leave_type");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });

        // ---- people
        b.Entity<Employee>(e =>
        {
            e.ToTable("employee");
            e.HasIndex(x => x.EmployeeCode).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PersonRef);
            // Self-service reads its own row through the identity link; must be fast and unique.
            e.HasIndex(x => x.UserRef).IsUnique().HasFilter("user_ref IS NOT NULL");
            e.Property(x => x.DocumentsJson).HasColumnType("jsonb");
        });
        b.Entity<EmployeeAssignment>(e =>
        {
            e.ToTable("employee_assignment");
            e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
            e.HasIndex(x => x.OrgUnitId);
        });
        b.Entity<EmployeePayStructure>(e =>
        {
            e.ToTable("employee_pay_structure");
            e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
        });
        b.Entity<EmployeePayComponent>(e =>
        {
            e.ToTable("employee_pay_component");
            e.HasIndex(x => new { x.PayStructureId, x.ComponentId }).IsUnique();
        });
        b.Entity<EmploymentEvent>(e =>
        {
            e.ToTable("employment_event");
            e.HasIndex(x => new { x.EmployeeId, x.OnDate });
            e.Property(x => x.DetailJson).HasColumnType("jsonb");
        });

        // ---- time
        b.Entity<Roster>(e =>
        {
            e.ToTable("roster");
            e.HasIndex(x => new { x.OrgUnitId, x.FromDate });
        });
        b.Entity<RosterEntry>(e =>
        {
            e.ToTable("roster_entry");
            e.HasIndex(x => new { x.EmployeeId, x.OnDate }).IsUnique();
            e.HasIndex(x => x.RosterId);
        });
        b.Entity<Punch>(e =>
        {
            e.ToTable("punch");
            // Re-importing the same device export is a no-op, not a duplicated day.
            e.HasIndex(x => new { x.EmployeeId, x.DeviceId, x.PunchedAt }).IsUnique();
            e.HasIndex(x => x.ImportBatchId);
        });
        b.Entity<AttendanceDay>(e =>
        {
            e.ToTable("attendance_day");
            e.HasIndex(x => new { x.EmployeeId, x.OnDate }).IsUnique();   // idempotency anchor
            e.HasIndex(x => new { x.BranchId, x.OnDate, x.Status });
        });
        b.Entity<AttendanceCorrection>(e =>
        {
            e.ToTable("attendance_correction");
            e.HasIndex(x => x.AttendanceDayId);
            e.HasIndex(x => new { x.ArrearsForRunId, x.ArrearsSettled });
        });
        b.Entity<PunchImportBatch>(e =>
        {
            e.ToTable("punch_import_batch");
            e.Property(x => x.RejectionsJson).HasColumnType("jsonb");
        });

        // ---- leave
        b.Entity<LeavePolicy>(e =>
        {
            e.ToTable("leave_policy");
            e.HasIndex(x => new { x.LeaveTypeId, x.EffectiveFrom });
        });
        b.Entity<LeaveBalance>(e =>
        {
            e.ToTable("leave_balance");
            e.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.LeaveYear }).IsUnique();
            // Two approvers deciding the same employee's leave must not both spend the last day.
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.AvailableBp);
        });
        b.Entity<LeaveApplication>(e =>
        {
            e.ToTable("leave_application");
            e.HasIndex(x => x.ApplicationNo).IsUnique();
            e.HasIndex(x => new { x.EmployeeId, x.FromDate });
            e.HasIndex(x => x.State);
        });
        b.Entity<LeaveEncashment>(e =>
        {
            e.ToTable("leave_encashment");
            e.HasIndex(x => new { x.EmployeeId, x.LeaveYear });
        });

        // ---- pay
        b.Entity<PayrollRun>(e =>
        {
            e.ToTable("payroll_run");
            e.HasIndex(x => x.RunNo).IsUnique();
            // One regular run per branch per month — the constraint, not a check in code, is what
            // stops two officers generating March twice.
            e.HasIndex(x => new { x.BranchId, x.Period, x.Kind, x.Sequence }).IsUnique();
            e.HasIndex(x => x.State);
            e.Property(x => x.JournalJson).HasColumnType("jsonb");
        });
        b.Entity<PayrollLine>(e =>
        {
            e.ToTable("payroll_line");
            e.HasIndex(x => new { x.RunId, x.EmployeeId }).IsUnique();
            e.HasIndex(x => x.EmployeeId);
            e.Property(x => x.PolicyStampJson).HasColumnType("jsonb");
        });
        b.Entity<PayrollComponentLine>(e =>
        {
            e.ToTable("payroll_component_line");
            e.HasIndex(x => x.PayrollLineId);
            e.HasIndex(x => x.RunId);
        });
        b.Entity<Payslip>(e =>
        {
            e.ToTable("payslip");
            e.HasIndex(x => x.PayslipNo).IsUnique();
            e.HasIndex(x => x.PayrollLineId).IsUnique();
            e.HasIndex(x => x.EmployeeId);
        });
        b.Entity<Loan>(e =>
        {
            e.ToTable("loan");
            e.HasIndex(x => x.LoanNo).IsUnique();
            e.HasIndex(x => new { x.EmployeeId, x.State });
        });
        b.Entity<LoanInstallment>(e =>
        {
            e.ToTable("loan_installment");
            e.HasIndex(x => new { x.LoanId, x.Period }).IsUnique();
        });
        b.Entity<EmployeeLedgerEntry>(e =>
        {
            e.ToTable("employee_ledger_entry");
            e.HasIndex(x => new { x.EmployeeId, x.Kind, x.OnDate });
        });

        // ---- policy (all effective-dated; overlap is refused by exclusion constraints added in
        // the migration, because EF cannot express them — see InitHr.)
        b.Entity<PayrollPolicy>(e =>
        {
            e.ToTable("payroll_policy");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
        b.Entity<TaxSlab>(e =>
        {
            e.ToTable("tax_slab");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom, x.Ordinal });
        });
        b.Entity<PfPolicy>(e =>
        {
            e.ToTable("pf_policy");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
        b.Entity<GratuityRule>(e =>
        {
            e.ToTable("gratuity_rule");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
        b.Entity<OvertimeRule>(e =>
        {
            e.ToTable("overtime_rule");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
        b.Entity<GraceTimeRule>(e =>
        {
            e.ToTable("grace_time_rule");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
        b.Entity<HolidayPayPolicy>(e =>
        {
            e.ToTable("holiday_pay_policy");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
        b.Entity<DeductionRule>(e =>
        {
            e.ToTable("deduction_rule");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
        });
    }
}

public class HrDbContextFactory : IDesignTimeDbContextFactory<HrDbContext>
{
    public HrDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<HrDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "hr"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
