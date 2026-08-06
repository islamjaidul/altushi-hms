using Hms.Kernel.Data;
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

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
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

    // lifecycle (spec 0056)
    public DbSet<EmployeeDependant> Dependants => Set<EmployeeDependant>();
    public DbSet<EmployeeDocument> Documents => Set<EmployeeDocument>();
    public DbSet<Separation> Separations => Set<Separation>();
    public DbSet<ClearanceItem> ClearanceItems => Set<ClearanceItem>();
    public DbSet<SettlementLine> SettlementLines => Set<SettlementLine>();
    public DbSet<LetterTemplate> LetterTemplates => Set<LetterTemplate>();
    public DbSet<IssuedLetter> IssuedLetters => Set<IssuedLetter>();
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();
    public DbSet<EmploymentPolicy> EmploymentPolicies => Set<EmploymentPolicy>();

    // money (spec 0057)
    public DbSet<SalaryHold> SalaryHolds => Set<SalaryHold>();
    public DbSet<VarianceNote> VarianceNotes => Set<VarianceNote>();
    public DbSet<BonusSheet> BonusSheets => Set<BonusSheet>();
    public DbSet<BonusLine> BonusLines => Set<BonusLine>();
    public DbSet<CompensationRun> CompensationRuns => Set<CompensationRun>();
    public DbSet<CompensationLine> CompensationLines => Set<CompensationLine>();
    public DbSet<Disbursement> Disbursements => Set<Disbursement>();
    public DbSet<DisbursementLine> DisbursementLines => Set<DisbursementLine>();

    // time
    public DbSet<Roster> Rosters => Set<Roster>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<Punch> Punches => Set<Punch>();
    public DbSet<AttendanceDay> AttendanceDays => Set<AttendanceDay>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<PunchImportBatch> PunchImportBatches => Set<PunchImportBatch>();

    // time depth (spec 0058)
    public DbSet<AttendanceDevice> Devices => Set<AttendanceDevice>();
    public DbSet<RegularizationRequest> RegularizationRequests => Set<RegularizationRequest>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<OvertimeBankEntry> OvertimeBank => Set<OvertimeBankEntry>();
    public DbSet<CompOffRequest> CompOffRequests => Set<CompOffRequest>();
    public DbSet<ShortLeaveRequest> ShortLeaveRequests => Set<ShortLeaveRequest>();
    public DbSet<ShiftSwapRequest> ShiftSwapRequests => Set<ShiftSwapRequest>();
    public DbSet<RosterPattern> RosterPatterns => Set<RosterPattern>();
    public DbSet<RosterPatternStep> RosterPatternSteps => Set<RosterPatternStep>();
    public DbSet<ShiftRequirement> ShiftRequirements => Set<ShiftRequirement>();

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
            // Unique per BRANCH (spec 0057). NumberSeriesService is keyed
            // (branch_id, doc_type, fiscal_year), so every branch issues EMP-00001 — a global
            // index rejected the second employer's first hire. Same defect, same shape, on
            // payroll_run, payslip and leave_application below.
            e.HasIndex(x => new { x.BranchId, x.EmployeeCode }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PersonRef);
            // Self-service reads its own row through the identity link; must be fast and unique.
            e.HasIndex(x => x.UserRef).IsUnique().HasFilter("user_ref IS NOT NULL");
            e.Property(x => x.DocumentsJson).HasColumnType("jsonb");
            // The two registers spec 0056 adds, each a partial index over the few rows that qualify
            // rather than a scan of the whole directory.
            e.HasIndex(x => new { x.BranchId, x.ProbationDueOn })
                .HasFilter("probation_due_on IS NOT NULL AND separated_on IS NULL");
            e.HasIndex(x => new { x.BranchId, x.ContractEndsOn })
                .HasFilter("contract_ends_on IS NOT NULL AND separated_on IS NULL");
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

        // ---- lifecycle (spec 0056). Intra-schema foreign keys with RESTRICT, never cascade: hard
        // rule 4 forbids the delete a cascade would amplify, and a settlement line orphaned from its
        // separation is a money document nobody can explain.
        b.Entity<EmployeeDependant>(e =>
        {
            e.ToTable("employee_dependant");
            e.HasIndex(x => x.EmployeeId);
            // The nominee-share query reads exactly this: live nominations for one person.
            e.HasIndex(x => new { x.EmployeeId, x.IsNominee, x.SupersededAt });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EmployeeDocument>(e =>
        {
            e.ToTable("employee_document");
            e.HasIndex(x => x.EmployeeId);
            // The expiry register's whole query: live documents in a branch, ordered by expiry.
            e.HasIndex(x => new { x.BranchId, x.ExpiresOn })
                .HasFilter("expires_on IS NOT NULL AND superseded_at IS NULL");
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Separation>(e =>
        {
            e.ToTable("separation");
            // One live separation per employment. A second is a rehire, which is a new employment.
            e.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("state <> 'cancelled'");
            e.HasIndex(x => new { x.BranchId, x.State });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ClearanceItem>(e =>
        {
            e.ToTable("clearance_item");
            e.HasIndex(x => new { x.SeparationId, x.Department }).IsUnique();
            e.HasOne<Separation>().WithMany().HasForeignKey(x => x.SeparationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SettlementLine>(e =>
        {
            e.ToTable("settlement_line");
            e.HasIndex(x => new { x.SeparationId, x.Ordinal });
            e.HasOne<Separation>().WithMany().HasForeignKey(x => x.SeparationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LetterTemplate>(e =>
        {
            e.ToTable("letter_template");
            // One active template per kind: "which one did it use" must never be a question.
            e.HasIndex(x => new { x.BranchId, x.Kind }).IsUnique().HasFilter("active");
        });
        b.Entity<IssuedLetter>(e =>
        {
            e.ToTable("issued_letter");
            // Unique per BRANCH, not globally. NumberSeriesService is keyed
            // (branch_id, doc_type, fiscal_year), so a second branch issues LTR-2026-27-0001 too;
            // a global unique index would reject the second employer's first letter.
            //
            // hr.payroll_run.run_no, hr.payslip.payslip_no and hr.leave_application.application_no
            // are all per-branch series behind globally unique indexes and have the same latent
            // collision — a separate, tracked cleanup (spec 0056 notes), not this table's problem.
            e.HasIndex(x => new { x.BranchId, x.LetterNo }).IsUnique();
            e.HasIndex(x => new { x.EmployeeId, x.IssuedOn });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<NotificationSetting>(e =>
        {
            e.ToTable("notification_setting");
            e.HasIndex(x => new { x.BranchId, x.Kind }).IsUnique();
        });
        b.Entity<EmploymentPolicy>(e =>
        {
            e.ToTable("employment_policy");
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom });
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
            e.HasOne<PunchImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId)
                .OnDelete(DeleteBehavior.Restrict);
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
            e.HasIndex(x => new { x.BranchId, x.ApplicationNo }).IsUnique();
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
            e.HasIndex(x => new { x.BranchId, x.RunNo }).IsUnique();
            // One regular run per branch per month — the constraint, not a check in code, is what
            // stops two officers generating March twice.
            e.HasIndex(x => new { x.BranchId, x.Period, x.Kind, x.Sequence }).IsUnique();
            e.HasIndex(x => x.State);
            // A locked run is reversed once and only once (spec 0052 WP3). The service checks
            // first, but a check is a race; the row lock in LoadAsync serialises the transitions
            // and this makes the invariant a database fact rather than a consequence of timing.
            e.HasIndex(x => x.ReversalOfRunId).IsUnique()
                .HasFilter("reversal_of_run_id IS NOT NULL");
            e.Property(x => x.JournalJson).HasColumnType("jsonb");
        });
        // Intra-schema foreign keys (spec 0039 WP2 §2.3 / AUD-M16-10): the database, not luck,
        // holds a line to its run. RESTRICT, never cascade — hard rule 4 forbids the delete a
        // cascade would amplify. No navigations: services keep addressing rows by id.
        b.Entity<PayrollLine>(e =>
        {
            e.ToTable("payroll_line");
            e.HasIndex(x => new { x.RunId, x.EmployeeId }).IsUnique();
            e.HasIndex(x => x.EmployeeId);
            e.Property(x => x.PolicyStampJson).HasColumnType("jsonb");
            e.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PayrollComponentLine>(e =>
        {
            e.ToTable("payroll_component_line");
            e.HasIndex(x => x.PayrollLineId);
            e.HasIndex(x => x.RunId);
            e.HasOne<PayrollLine>().WithMany().HasForeignKey(x => x.PayrollLineId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Payslip>(e =>
        {
            e.ToTable("payslip");
            e.HasIndex(x => new { x.BranchId, x.PayslipNo }).IsUnique();
            e.HasIndex(x => x.PayrollLineId).IsUnique();
            e.HasIndex(x => x.EmployeeId);
            e.HasOne<PayrollLine>().WithMany().HasForeignKey(x => x.PayrollLineId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Loan>(e =>
        {
            e.ToTable("loan");
            // Per branch, not globally: the number series is keyed (branch, doc_type, fiscal_year),
            // so a second branch's first loan would collide on a global index (spec 0056's finding).
            e.HasIndex(x => new { x.BranchId, x.LoanNo }).IsUnique();
            e.HasIndex(x => new { x.EmployeeId, x.State });
            // Derived from principal and recovered, so it can never disagree with the installments.
            e.Ignore(x => x.OutstandingTaka);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
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
        // ---- money (spec 0057). RESTRICT everywhere: hard rule 4 forbids the delete a cascade
        // would amplify, and a disbursement line orphaned from its run is money nobody can explain.
        b.Entity<SalaryHold>(e =>
        {
            e.ToTable("salary_hold");
            // One live hold per person. A second would make "is she held" ambiguous.
            e.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("released_at IS NULL");
            e.HasIndex(x => new { x.BranchId, x.ReleasedAt });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<VarianceNote>(e =>
        {
            e.ToTable("variance_note");
            e.HasIndex(x => new { x.RunId, x.EmployeeId }).IsUnique();
            e.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<BonusSheet>(e =>
        {
            e.ToTable("bonus_sheet");
            e.HasIndex(x => new { x.BranchId, x.SheetNo }).IsUnique();
            e.HasIndex(x => new { x.BranchId, x.Period, x.State });
        });
        b.Entity<BonusLine>(e =>
        {
            e.ToTable("bonus_line");
            e.HasIndex(x => new { x.SheetId, x.EmployeeId }).IsUnique();
            e.HasOne<BonusSheet>().WithMany().HasForeignKey(x => x.SheetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CompensationRun>(e =>
        {
            e.ToTable("compensation_run");
            e.HasIndex(x => new { x.BranchId, x.RunNo }).IsUnique();
            e.HasIndex(x => new { x.BranchId, x.EffectiveFrom, x.State });
        });
        b.Entity<CompensationLine>(e =>
        {
            e.ToTable("compensation_line");
            e.HasIndex(x => new { x.RunId, x.EmployeeId }).IsUnique();
            e.HasOne<CompensationRun>().WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Disbursement>(e =>
        {
            e.ToTable("disbursement");
            e.HasIndex(x => new { x.BranchId, x.BatchNo }).IsUnique();
            // One batch per run: "has March been paid" must have exactly one answer.
            e.HasIndex(x => x.RunId).IsUnique();
            e.HasOne<PayrollRun>().WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DisbursementLine>(e =>
        {
            e.ToTable("disbursement_line");
            e.HasIndex(x => new { x.DisbursementId, x.EmployeeId }).IsUnique();
            e.HasIndex(x => x.PayrollLineId).IsUnique();
            e.HasOne<Disbursement>().WithMany().HasForeignKey(x => x.DisbursementId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PayrollLine>().WithMany().HasForeignKey(x => x.PayrollLineId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- time depth (spec 0058). RESTRICT throughout: hard rule 4 forbids the delete a
        // cascade would amplify, and a bank entry orphaned from its employee is minutes nobody owns.
        b.Entity<AttendanceDevice>(e =>
        {
            e.ToTable("attendance_device");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.BranchId, x.Active, x.LastSeenAt });
        });
        b.Entity<RegularizationRequest>(e =>
        {
            e.ToTable("regularization_request");
            // One live request per employee-day: two people arguing about one Tuesday is a
            // conversation, not two corrections.
            e.HasIndex(x => new { x.EmployeeId, x.OnDate }).IsUnique()
                .HasFilter("state IN ('raised', 'recommended', 'approved')");
            e.HasIndex(x => new { x.BranchId, x.State });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OvertimeRequest>(e =>
        {
            e.ToTable("overtime_request");
            e.HasIndex(x => new { x.EmployeeId, x.OnDate }).IsUnique()
                .HasFilter("state IN ('raised', 'recommended', 'approved')");
            e.HasIndex(x => new { x.BranchId, x.State, x.OnDate });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OvertimeBankEntry>(e =>
        {
            e.ToTable("overtime_bank_entry");
            e.HasIndex(x => new { x.EmployeeId, x.OnDate });
            e.HasIndex(x => new { x.BranchId, x.Kind, x.ExpiresOn });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CompOffRequest>(e =>
        {
            e.ToTable("comp_off_request");
            e.HasIndex(x => new { x.EmployeeId, x.OnDate }).IsUnique()
                .HasFilter("state IN ('raised', 'recommended', 'approved', 'applied')");
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ShortLeaveRequest>(e =>
        {
            e.ToTable("short_leave_request");
            e.HasIndex(x => new { x.EmployeeId, x.OnDate });
            e.HasIndex(x => new { x.BranchId, x.State });
            e.Ignore(x => x.Minutes);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ShiftSwapRequest>(e =>
        {
            e.ToTable("shift_swap_request");
            e.HasIndex(x => new { x.BranchId, x.State });
            e.HasIndex(x => new { x.RequesterEmployeeId, x.RequesterDate });
        });
        b.Entity<RosterPattern>(e =>
        {
            e.ToTable("roster_pattern");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });
        b.Entity<RosterPatternStep>(e =>
        {
            e.ToTable("roster_pattern_step");
            e.HasIndex(x => new { x.PatternId, x.Ordinal }).IsUnique();
            e.HasOne<RosterPattern>().WithMany().HasForeignKey(x => x.PatternId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ShiftRequirement>(e =>
        {
            e.ToTable("shift_requirement");
            e.HasIndex(x => new { x.OrgUnitId, x.ShiftId, x.Weekday }).IsUnique();
        });

        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
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
