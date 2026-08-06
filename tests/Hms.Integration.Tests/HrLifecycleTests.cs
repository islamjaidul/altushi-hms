using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Screens.Reports;
using Hms.Hr.Web;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0056 — the employment between the hire and the payslip, against a real database.
/// <para>
/// The cases here are the ones a unit test cannot hold: the separation state machine's refusals,
/// the clearance gate on computing money, the effective-dated read of a gratuity rule that has since
/// changed, an issued letter surviving an edit to its template, and branch isolation on seven new
/// tables.
/// </para>
/// </summary>
[Collection("postgres")]
public sealed class HrLifecycleTests(PostgresFixture pg)
{
    private static long _branchSeed = 7400;
    private static long NextBranch() => Interlocked.Increment(ref _branchSeed);

    private static AuditWriter Audit() => new(TimeProvider.System);
    private static EmployeeService Employees() =>
        new(new NumberSeriesService(), Audit(), TimeProvider.System);
    private static PolicyResolver Policies() => new(Audit(), TimeProvider.System);
    private static EmployeeFileService Files() => new(Audit(), TimeProvider.System);
    private static SeparationService Separations() =>
        new(Employees(), Policies(), new ApprovalEngine(Audit(), TimeProvider.System),
            Audit(), TimeProvider.System);
    private static LetterService Letters() =>
        new(new NumberSeriesService(), Audit(), new FiscalCalendar(7), TimeProvider.System);

    // ---- probation (G13) ---------------------------------------------------------------------

    [Fact]
    public async Task Confirming_probation_sets_the_status_the_date_and_the_event_together()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch, probationDue: new DateOnly(2026, 10, 1));

        await tx.RunAsync(s => Employees().ConfirmProbationAsync(
            s.Hr, s.Kernel, branch, id, new DateOnly(2026, 10, 1), "satisfactory", 1, "Farid"));

        await using var hr = HrContext();
        var employee = await hr.Employees.SingleAsync(e => e.Id == id);
        Assert.Equal(EmploymentStatus.Confirmed, employee.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), employee.ConfirmedOn);
        // Nothing is due once it is decided, or the register would go on listing them.
        Assert.Null(employee.ProbationDueOn);

        Assert.True(await hr.EmploymentEvents
            .AnyAsync(e => e.EmployeeId == id && e.Kind == EmploymentEventKind.Confirmed));
    }

    [Fact]
    public async Task Nobody_is_confirmed_twice()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch, probationDue: new DateOnly(2026, 10, 1));

        await tx.RunAsync(s => Employees().ConfirmProbationAsync(
            s.Hr, s.Kernel, branch, id, new DateOnly(2026, 10, 1), null, 1, "Farid"));

        var again = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Employees().ConfirmProbationAsync(
                s.Hr, s.Kernel, branch, id, new DateOnly(2026, 11, 1), null, 1, "Farid")));

        Assert.Contains("already confirmed", again.Message);
    }

    [Fact]
    public async Task An_extension_records_both_dates_and_the_reason()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch, probationDue: new DateOnly(2026, 10, 1));

        await tx.RunAsync(s => Employees().ExtendProbationAsync(
            s.Hr, s.Kernel, branch, id, new DateOnly(2027, 1, 1), "needs more supervision",
            1, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(new DateOnly(2027, 1, 1),
            (await hr.Employees.SingleAsync(e => e.Id == id)).ProbationDueOn);

        var evt = await hr.EmploymentEvents
            .SingleAsync(e => e.EmployeeId == id && e.Kind == EmploymentEventKind.ProbationExtended);
        Assert.Contains("2026-10-01", evt.DetailJson);
        Assert.Contains("2027-01-01", evt.DetailJson);
        Assert.Equal("needs more supervision", evt.Note);
    }

    [Fact]
    public async Task An_extension_must_move_the_date_forward()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch, probationDue: new DateOnly(2026, 10, 1));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Employees().ExtendProbationAsync(
                s.Hr, s.Kernel, branch, id, new DateOnly(2026, 9, 1), "why", 1, "Farid")));

        Assert.Contains("forward", e.Message);
    }

    // ---- nominees and documents (G14, G16, G21) ------------------------------------------------

    [Fact]
    public async Task A_superseded_nominee_stays_readable_and_stops_counting()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var files = Files();

        var first = await tx.RunAsync(s => files.AddDependantAsync(
            s.Hr, s.Kernel, branch, id, Nominee("Rahima Begum", 10_000), 1, "Farid"));

        await tx.RunAsync(s => files.SupersedeDependantAsync(
            s.Hr, s.Kernel, branch, first.Id, Nominee("Karim Mia", 10_000), 1, "Farid"));

        await using var hr = HrContext();
        var live = await hr.Dependants.Where(d => d.EmployeeId == id && d.SupersededAt == null)
            .ToListAsync();
        var all = await hr.Dependants.Where(d => d.EmployeeId == id).ToListAsync();

        Assert.Equal("Karim Mia", Assert.Single(live).Name);
        Assert.Equal(2, all.Count);                       // the old instruction is kept, never deleted
        Assert.Contains(all, d => d.Name == "Rahima Begum" && d.SupersededAt != null);

        var shares = await EmployeeFileService.NomineeSharesAsync(hr, id);
        Assert.Equal(EmployeeFileService.WholeShareBp, shares[NomineePurpose.Gratuity]);
    }

    [Fact]
    public async Task A_nomination_for_both_benefits_counts_against_each()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);

        await tx.RunAsync(s => Files().AddDependantAsync(
            s.Hr, s.Kernel, branch, id, Nominee("Rahima Begum", 10_000, NomineePurpose.Both),
            1, "Farid"));

        await using var hr = HrContext();
        var shares = await EmployeeFileService.NomineeSharesAsync(hr, id);

        // Not half an instruction each: one instruction covering two benefits.
        Assert.Equal(EmployeeFileService.WholeShareBp, shares[NomineePurpose.ProvidentFund]);
        Assert.Equal(EmployeeFileService.WholeShareBp, shares[NomineePurpose.Gratuity]);
    }

    [Fact]
    public async Task A_licence_needs_the_body_that_issued_it()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Files().AddDocumentAsync(s.Hr, s.Kernel, branch, id, new EmployeeDocument
            {
                Kind = DocumentKind.Licence,
                Title = "BMDC registration",
                CreatedByName = "Farid",
            }, 1, "Farid")));

        Assert.Contains("council or board", e.Message);
    }

    [Fact]
    public async Task Renewing_a_document_keeps_the_lapsed_one_and_points_at_it()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var files = Files();

        var original = await tx.RunAsync(s => files.AddDocumentAsync(
            s.Hr, s.Kernel, branch, id, Licence("BMDC", "A-1234", new DateOnly(2026, 6, 30)),
            1, "Farid"));

        await tx.RunAsync(s => files.RenewDocumentAsync(
            s.Hr, s.Kernel, branch, original.Id,
            Licence("BMDC", "A-1234", new DateOnly(2031, 6, 30)), 1, "Farid"));

        await using var hr = HrContext();
        var live = await hr.Documents.Where(d => d.EmployeeId == id && d.SupersededAt == null)
            .SingleAsync();
        var lapsed = await hr.Documents.Where(d => d.EmployeeId == id && d.SupersededAt != null)
            .SingleAsync();

        Assert.Equal(new DateOnly(2031, 6, 30), live.ExpiresOn);
        Assert.Equal(original.Id, live.SupersedesId);
        Assert.Equal(new DateOnly(2026, 6, 30), lapsed.ExpiresOn);
        // The renewal inherits the kind: a licence cannot be renewed as a passport.
        Assert.Equal(DocumentKind.Licence, live.Kind);
    }

    // ---- separation (G18, §9) --------------------------------------------------------------------

    [Fact]
    public async Task Notice_puts_the_employment_on_notice_and_opens_the_checklist()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);

        var sep = await InitiateAsync(tx, branch, id);

        await using var hr = HrContext();
        Assert.Equal(EmploymentStatus.OnNotice,
            (await hr.Employees.SingleAsync(e => e.Id == id)).Status);
        Assert.Equal(ClearanceDepartment.All.Length,
            await hr.ClearanceItems.CountAsync(c => c.SeparationId == sep.Id));
        // The employment has not ended: SeparatedOn is stamped when the money is paid, not now.
        Assert.Null((await hr.Employees.SingleAsync(e => e.Id == id)).SeparatedOn);
    }

    [Fact]
    public async Task One_live_separation_per_employment()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        await InitiateAsync(tx, branch, id);

        var e = await Assert.ThrowsAsync<HrException>(() => InitiateAsync(tx, branch, id));

        Assert.Contains("already has a separation in progress", e.Message);
    }

    /// <summary>
    /// The gate that matters: a statement computed over an incomplete clearance is a number that
    /// will change, and this one gets approved and paid.
    /// </summary>
    [Fact]
    public async Task A_settlement_cannot_be_computed_while_a_department_is_outstanding()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var sep = await InitiateAsync(tx, branch, id);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Separations().ComputeAsync(
                s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 8, 31), 1, "Farid")));

        Assert.Contains("Clearance is not complete", e.Message);
        // The message names who is holding it up, because "invalid state" is not actionable.
        Assert.Contains("HR", e.Message);
    }

    [Fact]
    public async Task Clearing_every_department_lets_the_settlement_compute()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var sep = await InitiateAsync(tx, branch, id);
        await ClearAllAsync(tx, branch, sep.Id);

        await tx.RunAsync(s => Separations().ComputeAsync(
            s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 8, 31), 1, "Farid"));

        await using var hr = HrContext();
        var after = await hr.Separations.SingleAsync(x => x.Id == sep.Id);
        Assert.Equal(SeparationState.Computed, after.State);
        Assert.NotNull(after.ComputedAt);
        // With no pay structure and no gratuity rule, the honest statement is no lines and a
        // sentence — never a zero that reads as "you earned nothing".
        Assert.NotNull(after.ComputationNotes);
        Assert.Contains("No pay structure is in force", after.ComputationNotes);
    }

    [Fact]
    public async Task Clearance_dues_land_on_the_statement_as_a_deduction()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var sep = await InitiateAsync(tx, branch, id);

        await using (var read = HrContext())
        {
            var items = await read.ClearanceItems.Where(c => c.SeparationId == sep.Id).ToListAsync();
            foreach (var item in items)
            {
                var withDues = item.Department == ClearanceDepartment.It;
                await tx.RunAsync(s => Separations().SignOffClearanceAsync(
                    s.Hr, s.Kernel, branch, item.Id,
                    withDues ? ClearanceStatus.ClearedWithDues : ClearanceStatus.Cleared,
                    withDues ? "laptop not returned" : null,
                    withDues ? 45_000 : 0, 1, "Farid"));
            }
        }

        await tx.RunAsync(s => Separations().ComputeAsync(
            s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 8, 31), 1, "Farid"));

        await using var hr = HrContext();
        var line = await hr.SettlementLines
            .SingleAsync(l => l.SeparationId == sep.Id && l.Source == SettlementSource.ClearanceDues);
        Assert.Equal(45_000, line.AmountTaka);
        Assert.Equal(PayComponentKind.Deduction, line.Kind);
        Assert.Equal(-45_000, (await hr.Separations.SingleAsync(x => x.Id == sep.Id)).NetPayableTaka);
    }

    [Fact]
    public async Task A_blocked_department_blocks_the_computation()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var sep = await InitiateAsync(tx, branch, id);

        await using (var read = HrContext())
        {
            var items = await read.ClearanceItems.Where(c => c.SeparationId == sep.Id).ToListAsync();
            foreach (var item in items)
            {
                var blocked = item.Department == ClearanceDepartment.Store;
                await tx.RunAsync(s => Separations().SignOffClearanceAsync(
                    s.Hr, s.Kernel, branch, item.Id,
                    blocked ? ClearanceStatus.Blocked : ClearanceStatus.Cleared,
                    blocked ? "uniform outstanding" : null, 0, 1, "Farid"));
            }
        }

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Separations().ComputeAsync(
                s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 8, 31), 1, "Farid")));

        Assert.Contains("Store", e.Message);
    }

    [Fact]
    public async Task A_settlement_cannot_be_paid_before_it_is_approved()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var sep = await InitiateAsync(tx, branch, id);
        await ClearAllAsync(tx, branch, sep.Id);
        await tx.RunAsync(s => Separations().ComputeAsync(
            s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 8, 31), 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Separations().MarkPaidAsync(s.Hr, s.Kernel, branch, sep.Id, 1, 1, "Farid")));

        // §9's order, refused by name: the message says where it actually is.
        Assert.Contains("computed", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Documents_cannot_be_issued_before_the_money_is_paid()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var sep = await InitiateAsync(tx, branch, id);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Separations().MarkDocumentsIssuedAsync(
                s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 9, 1), 1, "Farid")));

        Assert.Contains("not reached yet", e.Message);
    }

    [Fact]
    public async Task Withdrawing_a_separation_returns_the_employment_to_where_it_was()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch, confirmedOn: new DateOnly(2025, 7, 1));
        var sep = await InitiateAsync(tx, branch, id);

        await tx.RunAsync(s => Separations().CancelAsync(
            s.Hr, s.Kernel, branch, sep.Id, "resignation retracted", 1, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(SeparationState.Cancelled,
            (await hr.Separations.SingleAsync(x => x.Id == sep.Id)).State);
        Assert.Equal(EmploymentStatus.Confirmed,
            (await hr.Employees.SingleAsync(e => e.Id == id)).Status);

        // And a fresh separation is possible again — the unique index is filtered on cancelled.
        await InitiateAsync(tx, branch, id);
    }

    /// <summary>
    /// Hard rule 5's shape applied to a settlement: the gratuity rule that was in force on the last
    /// working day is the one that computes, not today's. A rule changed after someone leaves must
    /// not silently restate what they were owed.
    /// </summary>
    [Fact]
    public async Task The_settlement_reads_the_rule_in_force_on_the_last_working_day()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch, joinedOn: new DateOnly(2019, 1, 1));
        await SeedPayAsync(tx, branch, id, 30_000, new DateOnly(2019, 1, 1));

        // 30 days per year, in force from 2019.
        await tx.RunAsync(s => Policies().SetGratuityAsync(
            s.Hr, s.Kernel, branch, new DateOnly(2019, 1, 1), 12, 300_000, 1, "Farid"));

        var sep = await InitiateAsync(tx, branch, id);
        await ClearAllAsync(tx, branch, sep.Id);
        await tx.RunAsync(s => Separations().ComputeAsync(
            s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 8, 31), 1, "Farid"));

        await using var hr = HrContext();
        var gratuity = await hr.SettlementLines
            .SingleAsync(l => l.SeparationId == sep.Id && l.Source == SettlementSource.Gratuity);

        // 7 completed years × 30 days at 30,000/31 = 967/day.
        Assert.Equal(7 * 30 * (30_000 / 31), gratuity.AmountTaka);
        Assert.Contains("7 completed years", gratuity.Basis);
    }

    // ---- letters (G20) ----------------------------------------------------------------------------

    [Fact]
    public async Task An_issued_letter_does_not_change_when_its_template_does()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var id = await SeedEmployeeAsync(tx, branch);
        var letters = Letters();

        await tx.RunAsync(s => letters.SaveTemplateAsync(
            s.Hr, s.Kernel, branch, LetterKind.Experience, "Experience certificate",
            "This certifies that {{employee.name}} served with distinction.", 1, "Farid"));

        var issued = await tx.RunAsync(s => letters.IssueAsync(
            s.Hr, s.Kernel, branch, id, LetterKind.Experience, "Experience certificate",
            "This certifies that Lifecycle Subject served with distinction.",
            new DateOnly(2026, 8, 6), null, 1, "Farid"));

        // The employer rewrites the wording a year later.
        await tx.RunAsync(s => letters.SaveTemplateAsync(
            s.Hr, s.Kernel, branch, LetterKind.Experience, "Experience certificate",
            "{{employee.name}} was employed here.", 1, "Farid"));

        await using var hr = HrContext();
        var stored = await hr.IssuedLetters.SingleAsync(l => l.Id == issued.Id);
        Assert.Equal("This certifies that Lifecycle Subject served with distinction.", stored.Body);
        Assert.StartsWith("LTR-", stored.LetterNo);
    }

    [Fact]
    public async Task A_template_using_a_word_the_product_cannot_fill_is_refused()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Letters().SaveTemplateAsync(
                s.Hr, s.Kernel, branch, LetterKind.Noc, "NOC",
                "Dear {{employee.nickname}},", 1, "Farid")));

        Assert.Contains("employee.nickname", e.Message);
    }

    [Fact]
    public async Task One_active_template_per_kind()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var letters = Letters();

        await tx.RunAsync(s => letters.SaveTemplateAsync(
            s.Hr, s.Kernel, branch, LetterKind.Noc, "NOC", "First wording.", 1, "Farid"));
        await tx.RunAsync(s => letters.SaveTemplateAsync(
            s.Hr, s.Kernel, branch, LetterKind.Noc, "NOC", "Second wording.", 1, "Farid"));

        await using var hr = HrContext();
        var template = await hr.LetterTemplates
            .SingleAsync(t => t.BranchId == branch && t.Kind == LetterKind.Noc && t.Active);
        Assert.Equal("Second wording.", template.Body);
    }

    // ---- branch isolation (WP5) ---------------------------------------------------------------------

    /// <summary>
    /// Seven new tables, every one carrying <c>BranchId</c> so the global filter applies. A child
    /// table reached without one would leak across employers on a shared install.
    /// </summary>
    [Fact]
    public async Task No_lifecycle_table_leaks_across_branches()
    {
        var one = NextBranch();
        var two = NextBranch();

        BranchScope.Current = one;
        var tx = await TxAsync();
        var employeeOne = await SeedEmployeeAsync(tx, one);
        var sepOne = await InitiateAsync(tx, one, employeeOne);
        await tx.RunAsync(s => Files().AddDependantAsync(
            s.Hr, s.Kernel, one, employeeOne, Nominee("Branch One Nominee", 10_000), 1, "Farid"));
        await tx.RunAsync(s => Files().AddDocumentAsync(
            s.Hr, s.Kernel, one, employeeOne, Licence("BMDC", "ONE-1", new DateOnly(2030, 1, 1)),
            1, "Farid"));
        await tx.RunAsync(s => Letters().SaveTemplateAsync(
            s.Hr, s.Kernel, one, LetterKind.Noc, "NOC", "Branch one wording.", 1, "Farid"));
        await tx.RunAsync(s => Letters().IssueAsync(
            s.Hr, s.Kernel, one, employeeOne, LetterKind.Noc, "NOC", "Branch one body.",
            new DateOnly(2026, 8, 6), null, 1, "Farid"));

        // Now read everything as branch two.
        BranchScope.Current = two;
        await using var hr = HrContext();

        Assert.Empty(await hr.Separations.ToListAsync());
        Assert.Empty(await hr.ClearanceItems.ToListAsync());
        Assert.Empty(await hr.SettlementLines.ToListAsync());
        Assert.Empty(await hr.Dependants.ToListAsync());
        Assert.Empty(await hr.Documents.ToListAsync());
        Assert.Empty(await hr.LetterTemplates.ToListAsync());
        Assert.Empty(await hr.IssuedLetters.ToListAsync());
        Assert.Empty(await hr.NotificationSettings.ToListAsync());
        Assert.Empty(await hr.EmploymentPolicies.ToListAsync());

        // And the seeded data really exists — otherwise this test passes on an empty database.
        BranchScope.Current = one;
        await using var mine = HrContext();
        Assert.Single(await mine.Separations.Where(x => x.Id == sepOne.Id).ToListAsync());
        Assert.NotEmpty(await mine.Dependants.ToListAsync());
        Assert.NotEmpty(await mine.Documents.ToListAsync());
        Assert.NotEmpty(await mine.IssuedLetters.ToListAsync());
    }

    // ---- alerts (G57) -------------------------------------------------------------------------------

    [Fact]
    public async Task A_probation_due_inside_the_horizon_raises_an_alert_and_one_outside_does_not()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var today = new DateOnly(2026, 8, 6);

        var soon = await SeedEmployeeAsync(tx, branch, probationDue: today.AddDays(10), code: "SOON");
        await SeedEmployeeAsync(tx, branch, probationDue: today.AddDays(200), code: "LATER");

        await using var hr = HrContext();
        var settings = await HrAlertService.SettingsAsync(hr, branch);
        var alerts = await HrAlertService.DueAsync(hr, branch, today, settings);

        var probation = alerts.Where(a => a.Kind == HrNotificationKind.ProbationDue).ToList();
        Assert.Single(probation);
        Assert.Equal(soon, probation[0].EmployeeId);
        Assert.Equal(10, probation[0].DaysAway);
    }

    [Fact]
    public async Task An_expired_licence_stays_on_the_list_however_long_ago_it_lapsed()
    {
        // Dropping it off the horizon is exactly how a licence stays expired.
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var today = new DateOnly(2026, 8, 6);
        var id = await SeedEmployeeAsync(tx, branch);

        await tx.RunAsync(s => Files().AddDocumentAsync(
            s.Hr, s.Kernel, branch, id, Licence("BMDC", "OLD-1", new DateOnly(2024, 1, 1)),
            1, "Farid"));

        await using var hr = HrContext();
        var settings = await HrAlertService.SettingsAsync(hr, branch);
        var alerts = await HrAlertService.DueAsync(hr, branch, today, settings);

        var licence = Assert.Single(alerts, a => a.Kind == HrNotificationKind.LicenceExpiring);
        Assert.True(licence.DaysAway < 0);
        Assert.Equal("bad", licence.Tone);
        Assert.Contains("overdue", licence.When);
    }

    [Fact]
    public async Task A_separated_employees_expiring_licence_is_nobodys_problem()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var today = new DateOnly(2026, 8, 6);
        var id = await SeedEmployeeAsync(tx, branch);

        await tx.RunAsync(s => Files().AddDocumentAsync(
            s.Hr, s.Kernel, branch, id, Licence("BMDC", "GONE-1", today.AddDays(5)), 1, "Farid"));

        await tx.RunAsync(async s =>
        {
            var employee = await s.Hr.Employees.SingleAsync(e => e.Id == id);
            employee.SeparatedOn = today.AddDays(-1);
            employee.Status = EmploymentStatus.Resigned;
            await s.Hr.SaveChangesAsync();
        });

        await using var hr = HrContext();
        var settings = await HrAlertService.SettingsAsync(hr, branch);
        var alerts = await HrAlertService.DueAsync(hr, branch, today, settings);

        Assert.DoesNotContain(alerts, a => a.EmployeeId == id);
    }

    [Fact]
    public async Task An_unconfigured_employer_gets_the_stated_defaults_rather_than_nothing()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        await TxAsync();

        await using var hr = HrContext();
        var settings = await HrAlertService.SettingsAsync(hr, branch);

        Assert.Equal(HrNotificationKind.All.Length, settings.Count);
        Assert.All(settings.Values, s => Assert.False(s.Enabled));
        Assert.Equal(30, settings[HrNotificationKind.ProbationDue].DaysBefore);
        Assert.Equal(60, settings[HrNotificationKind.ContractExpiring].DaysBefore);
    }

    // ---- the whole catalogue -----------------------------------------------------------------------

    /// <summary>
    /// Every report in the catalogue, run against a branch with nothing in it. Two things are being
    /// proved: no report throws on empty data, and <b>every empty result says why it is empty</b>
    /// (§12.4.4). <c>ReportTable.Validated()</c> enforces the second at construction, so a report
    /// author who forgets fails here rather than shipping a blank grid to a buyer.
    /// <para>
    /// This is the smoke test spec 0055's plan called for and did not build. It covers the nine
    /// registers spec 0056 adds and the twenty-six that came before them.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_report_runs_on_an_empty_branch_and_explains_itself()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();

        var period = PeriodResolver.Resolve(
            new PeriodQuery(Grain: "month"), new DateOnly(2026, 8, 6), PeriodCalendar.Default);

        var failures = new List<string>();

        await tx.RunAsync(async s =>
        {
            foreach (var report in ReportCatalog.All)
            {
                var ctx = new ReportContext
                {
                    BranchId = branch,
                    Period = period,
                    Filters = new HrReportFilters(),
                    Raw = FilterSet.Empty(),
                    Scope = s,
                    PeriodPairs = [new KeyValuePair<string, string>("g", "month")],
                    // Salary-bearing reports are handed a reader who may see pay, so their queries
                    // actually run here rather than being skipped.
                    CanSeeSalary = true,
                };

                try
                {
                    var table = await report.BuildAsync(ctx, CancellationToken.None);
                    table.Validated();
                }
                catch (Exception e)
                {
                    failures.Add($"{report.Descriptor.Key}: {e.GetType().Name} — {e.Message}");
                }
            }
        });

        Assert.True(failures.Count == 0,
            "Reports that failed on an empty branch:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The same sweep over a branch with a person, a separation, a document and a letter in it — so
    /// the reports that returned "nothing to show" above are exercised on their populated path too.
    /// </summary>
    [Fact]
    public async Task Every_report_runs_on_a_seeded_branch()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();

        var id = await SeedEmployeeAsync(tx, branch, probationDue: new DateOnly(2026, 8, 20));
        await SeedPayAsync(tx, branch, id, 30_000, new DateOnly(2021, 7, 1));
        await tx.RunAsync(s => Files().AddDependantAsync(
            s.Hr, s.Kernel, branch, id, Nominee("Rahima Begum", 10_000), 1, "Farid"));
        await tx.RunAsync(s => Files().AddDocumentAsync(
            s.Hr, s.Kernel, branch, id, Licence("BMDC", "SEED-1", new DateOnly(2026, 9, 1)),
            1, "Farid"));
        var sep = await InitiateAsync(tx, branch, id);
        await ClearAllAsync(tx, branch, sep.Id);
        await tx.RunAsync(s => Separations().ComputeAsync(
            s.Hr, s.Kernel, branch, sep.Id, new DateOnly(2026, 8, 31), 1, "Farid"));
        await tx.RunAsync(s => Letters().IssueAsync(
            s.Hr, s.Kernel, branch, id, LetterKind.Noc, "NOC", "Body.",
            new DateOnly(2026, 8, 6), null, 1, "Farid"));

        var period = PeriodResolver.Resolve(
            new PeriodQuery(Grain: "month"), new DateOnly(2026, 8, 6), PeriodCalendar.Default);

        var failures = new List<string>();

        await tx.RunAsync(async s =>
        {
            foreach (var report in ReportCatalog.All)
            {
                var ctx = new ReportContext
                {
                    BranchId = branch,
                    Period = period,
                    Filters = new HrReportFilters(),
                    Raw = FilterSet.Empty(),
                    Scope = s,
                    PeriodPairs = [new KeyValuePair<string, string>("g", "month")],
                    CanSeeSalary = true,
                };

                try
                {
                    (await report.BuildAsync(ctx, CancellationToken.None)).Validated();
                }
                catch (Exception e)
                {
                    failures.Add($"{report.Descriptor.Key}: {e.GetType().Name} — {e.Message}");
                }
            }
        });

        Assert.True(failures.Count == 0,
            "Reports that failed on a seeded branch:\n" + string.Join("\n", failures));
    }

    // ---- fixtures -------------------------------------------------------------------------------

    private static EmployeeDependant Nominee(string name, int shareBp, string purpose = NomineePurpose.Gratuity)
        => new()
        {
            Name = name,
            Relation = DependantRelation.Spouse,
            IsNominee = true,
            Purpose = purpose,
            ShareBp = shareBp,
            CreatedByName = "Farid",
        };

    private static EmployeeDocument Licence(string body, string number, DateOnly expires) => new()
    {
        Kind = DocumentKind.Licence,
        Title = body + " registration",
        Number = number,
        IssuingBody = body,
        IssuedOn = expires.AddYears(-5),
        ExpiresOn = expires,
        CreatedByName = "Farid",
    };

    private async Task<Separation> InitiateAsync(HrTx tx, long branch, long employeeId)
        => await tx.RunAsync(s => Separations().InitiateAsync(
            s.Hr, s.Kernel, branch, employeeId, SeparationKind.Resignation,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31), "moving on", 1, "Farid"));

    private async Task ClearAllAsync(HrTx tx, long branch, long separationId)
    {
        await using var read = HrContext();
        var items = await read.ClearanceItems.Where(c => c.SeparationId == separationId).ToListAsync();
        foreach (var item in items)
        {
            await tx.RunAsync(s => Separations().SignOffClearanceAsync(
                s.Hr, s.Kernel, branch, item.Id, ClearanceStatus.Cleared, null, 0, 1, "Farid"));
        }
    }

    private async Task<long> SeedEmployeeAsync(
        HrTx tx, long branch, DateOnly? probationDue = null, DateOnly? confirmedOn = null,
        DateOnly? joinedOn = null, string code = "L-001")
        => await tx.RunAsync(async s =>
        {
            var employee = new Employee
            {
                BranchId = branch,
                EmployeeCode = $"L{branch}-{code}",
                FullName = "Lifecycle Subject",
                JoinedOn = joinedOn ?? new DateOnly(2021, 7, 1),
                ProbationDueOn = probationDue,
                ConfirmedOn = confirmedOn,
                Status = confirmedOn is null ? EmploymentStatus.Probation : EmploymentStatus.Confirmed,
                Phone = "01711000000",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();
            return employee.Id;
        });

    private async Task SeedPayAsync(HrTx tx, long branch, long employeeId, long basic, DateOnly from)
        => await tx.RunAsync(async s =>
        {
            var component = new PayComponent
            {
                BranchId = branch,
                Code = $"BASIC{branch}",
                Name = "Basic",
                Kind = PayComponentKind.Earning,
                CalcMethod = PayComponentCalc.Fixed,
                DisplayOrder = 1,
                Active = true,
            };
            s.Hr.PayComponents.Add(component);
            await s.Hr.SaveChangesAsync();

            var structure = new EmployeePayStructure
            {
                BranchId = branch,
                EmployeeId = employeeId,
                EffectiveFrom = from,
                Reason = "joining",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = 1,
            };
            s.Hr.PayStructures.Add(structure);
            await s.Hr.SaveChangesAsync();

            s.Hr.PayStructureComponents.Add(new EmployeePayComponent
            {
                PayStructureId = structure.Id,
                ComponentId = component.Id,
                AmountTaka = basic,
            });
            await s.Hr.SaveChangesAsync();
        });

    private async Task<HrTx> TxAsync()
    {
        await MigrateAsync();
        return new HrTx(Config());
    }

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Hms"] = pg.ConnectionString,
        })
        .Build();

    private HrDbContext HrContext() => new(Options<HrDbContext>("hr"));

    private DbContextOptions<T> Options<T>(string schema) where T : DbContext
        => new DbContextOptionsBuilder<T>()
            .UseNpgsql(pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention()
            .Options;

    private async Task MigrateAsync()
    {
        await using var kernel = new KernelDbContext(Options<KernelDbContext>("kernel"));
        await kernel.Database.MigrateAsync();
        await using var auth = new AuthDbContext(Options<AuthDbContext>("adm"));
        await auth.Database.MigrateAsync();
        await using var hr = new HrDbContext(Options<HrDbContext>("hr"));
        await hr.Database.MigrateAsync();
    }
}
