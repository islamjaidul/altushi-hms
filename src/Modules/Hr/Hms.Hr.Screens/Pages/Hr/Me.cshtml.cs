using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record MyLeaveRow(string No, string Type, DateOnly From, DateOnly To, int DaysBp, string State);
public sealed record MyBalanceRow(string Type, int AvailableBp);

// ---- spec 0059: the rest of the employee's own relationship with HR (§7.10, G52).
public sealed record MyPayslipRow(long LineId, DateOnly Period, string RunNo, long NetTaka, bool Held);
public sealed record MyLoanRow(string LoanNo, string Kind, long PrincipalTaka, long OutstandingTaka,
    long InstallmentTaka, string State);
public sealed record MyDocumentRow(string Kind, string Title, string? Number, DateOnly? ExpiresOn,
    string Tone, string Words);
public sealed record MyLetterRow(long Id, string Kind, string LetterNo, DateOnly IssuedOn);
public sealed record MyRequestRow(string Kind, DateOnly OnDate, string What, string State);
public sealed record MyNoticeRow(long Id, string Title, string Body, DateOnly PublishFrom,
    bool RequiresAcknowledgement, bool Acknowledged);

/// <summary>
/// Self-service (§5 M16 [S], and the <c>U (own leave)</c> cells §12 gives Nurse, Technologist and
/// Pharmacist). This is the one HR screen an ordinary employee reaches, so it shows their own record
/// and nothing else.
/// <para>
/// The identity link is <c>AppUser.EmployeeRef</c> — the column reserved for exactly this since the
/// MVP and unwritten until now. Every query below is scoped by it: there is no employee id in the
/// URL, so there is nothing to tamper with.
/// </para>
/// </summary>
// Spec 0055: "My space" is present for anyone with a linked employment (§11 scoping rule 2), so the
// door is hr.self rather than leave-apply — an employee who may not apply for leave still has a
// payslip, a record and requests of their own. Seeded roles gain hr.self on the next start, because
// HrSeed reconciles grants on every boot rather than only on first run.
[Authorize(Policy = HrPerm.Self)]
public class MeModel(IHrTx tx, LeaveService leave) : HmsPageModel
{
    [BindProperty] public long LeaveTypeId { get; set; }
    [BindProperty] public string FromDate { get; set; } = "";
    [BindProperty] public string ToDate { get; set; } = "";
    [BindProperty] public bool HalfDay { get; set; }
    [BindProperty] public string Reason { get; set; } = "";

    public Employee? Me { get; private set; }
    public IReadOnlyList<MyLeaveRow> Applications { get; private set; } = [];
    public IReadOnlyList<MyBalanceRow> Balances { get; private set; } = [];
    public List<SelectListItem> LeaveTypes { get; private set; } = [];

    // ---- spec 0059 (§7.10, G52). Four phases built a great deal and gave all of it to HR; this
    // is the employee's own door onto their own record. Every query is scoped by the identity
    // link, never by a URL parameter — there is nothing here to tamper with.
    public MonthGrid? Attendance { get; private set; }
    public IReadOnlyList<MyPayslipRow> Payslips { get; private set; } = [];
    public IReadOnlyList<MyLoanRow> Loans { get; private set; } = [];
    public IReadOnlyList<MyDocumentRow> Documents { get; private set; } = [];
    public IReadOnlyList<MyLetterRow> Letters { get; private set; } = [];
    public IReadOnlyList<MyRequestRow> Requests { get; private set; } = [];
    public IReadOnlyList<MyNoticeRow> Notices { get; private set; } = [];
    public long PfBalanceTaka { get; private set; }
    public long TaxWithheldTaka { get; private set; }
    public int BankedMinutes { get; private set; }
    public ProfileChangeRequest? OpenProfileChange { get; private set; }
    public DateOnly Today { get; private set; }

    // ---- the profile change request (G54)
    [BindProperty] public string? NewPhone { get; set; }
    [BindProperty] public string? NewEmail { get; set; }
    [BindProperty] public string? NewAddress { get; set; }
    [BindProperty] public string? NewEmergencyContact { get; set; }
    [BindProperty] public string? NewBankAccountNo { get; set; }

    /// <summary>
    /// Proposes a correction to the employee's own record (§7.10, G54). Nothing changes until HR
    /// approves it — until now HR edited everything, including a phone number the employee is the
    /// only person who actually knows.
    /// </summary>
    public async Task<IActionResult> OnPostProposeChangeAsync()
    {
        await LoadAsync();
        if (Me is null) return NotFound();

        if (OpenProfileChange is not null)
        {
            Fail("You already have a change waiting for HR. It has to be decided before another.");
            return Page();
        }

        var nothing = string.IsNullOrWhiteSpace(NewPhone) && string.IsNullOrWhiteSpace(NewEmail)
            && string.IsNullOrWhiteSpace(NewAddress)
            && string.IsNullOrWhiteSpace(NewEmergencyContact)
            && string.IsNullOrWhiteSpace(NewBankAccountNo);
        if (nothing)
        {
            Fail("Fill in whatever has changed — an empty request asks HR to guess.");
            return Page();
        }

        var employeeId = Me.Id;
        await tx.RunAsync(async s =>
        {
            s.Hr.ProfileChangeRequests.Add(new ProfileChangeRequest
            {
                BranchId = BranchId,
                EmployeeId = employeeId,
                Phone = Blank(NewPhone),
                Email = Blank(NewEmail),
                PresentAddress = Blank(NewAddress),
                EmergencyContact = Blank(NewEmergencyContact),
                BankAccountNo = Blank(NewBankAccountNo),
                RaisedAt = DateTimeOffset.UtcNow,
                RaisedBy = ActorId,
                RaisedByName = ActorName,
            });
            await s.Hr.SaveChangesAsync();
        });

        Toast("Sent to HR — nothing changes on your record until they approve it", "how_to_reg");
        return Redirect("/hr/me");
    }

    public async Task<IActionResult> OnPostAcknowledgeAsync(long noticeId)
    {
        await LoadAsync();
        if (Me is null) return NotFound();

        var employeeId = Me.Id;
        await tx.RunAsync(async s =>
        {
            var already = await s.Hr.NoticeAcknowledgements
                .AnyAsync(a => a.NoticeId == noticeId && a.EmployeeId == employeeId);
            if (already) return;

            s.Hr.NoticeAcknowledgements.Add(new NoticeAcknowledgement
            {
                BranchId = BranchId,
                NoticeId = noticeId,
                EmployeeId = employeeId,
                AcknowledgedAt = DateTimeOffset.UtcNow,
            });
            await s.Hr.SaveChangesAsync();
        });

        Toast("Acknowledged", "task_alt");
        return Redirect("/hr/me");
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>
    /// Whether this employee may apply for leave at all. Spec 0055 moved the page's door to
    /// <c>hr.self</c>, so applying is now gated on its own grant rather than on reaching the screen —
    /// which is the right shape anyway: everyone has a payslip and a record, not everyone has the
    /// right to file leave.
    /// </summary>
    public bool CanApply => Can("hr.leave.apply");

    public async Task<IActionResult> OnPostApplyAsync()
    {
        await LoadAsync();

        // At the handler, not the button (§12, security-guardrails): hiding the form is a courtesy,
        // refusing the POST is the control.
        if (!CanApply) return Forbid();

        if (Me is null)
        {
            Fail("Your login is not linked to an employee record yet — ask HR to link it.");
            return Page();
        }
        if (!FlexibleDate.TryParse(FromDate, out var from) || !FlexibleDate.TryParse(ToDate, out var to))
        {
            Fail("Enter both dates as dd/mm/yyyy.");
            return Page();
        }

        var days = to.DayNumber - from.DayNumber + 1;
        var daysBp = HalfDay && days == 1 ? 5000 : days * 10000;

        try
        {
            await tx.RunAsync(async s => await leave.ApplyAsync(
                s.Hr, s.Kernel, BranchId, Me.Id, LeaveTypeId, from, to, daysBp, Reason,
                ActorId, ActorName));
            Toast("Applied — your department head will see it", "event_available");
            return Redirect("/hr/me");
        }
        catch (HrException e)
        {
            Fail(e.Message);
            return Page();
        }
    }

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        var userRef = ActorId.ToString();
        Me = await s.Hr.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.BranchId == BranchId && e.UserRef == userRef);

        LeaveTypes = await s.Hr.LeaveTypes.AsNoTracking()
            .Where(t => t.BranchId == BranchId && t.Active).OrderBy(t => t.Name)
            .Select(t => new SelectListItem(t.Name, t.Id.ToString()))
            .ToListAsync();

        if (Me is null) return;

        var types = await s.Hr.LeaveTypes.AsNoTracking()
            .Where(t => t.BranchId == BranchId).ToDictionaryAsync(t => t.Id, t => t.Name);

        Applications = await s.Hr.LeaveApplications.AsNoTracking()
            .Where(l => l.EmployeeId == Me.Id)
            .OrderByDescending(l => l.AppliedAt).Take(50)
            .Select(l => new MyLeaveRow(
                l.ApplicationNo, types.GetValueOrDefault(l.LeaveTypeId, "—"),
                l.FromDate, l.ToDate, l.DaysBp, l.State))
            .ToListAsync();

        var year = Dhaka.DateOf(DateTimeOffset.UtcNow).Year;
        var balances = await s.Hr.LeaveBalances.AsNoTracking()
            .Where(b => b.EmployeeId == Me.Id && b.LeaveYear == year)
            .ToListAsync();

        Balances = balances
            .Select(b => new MyBalanceRow(types.GetValueOrDefault(b.LeaveTypeId, "—"), b.AvailableBp))
            .ToList();

        // ---- spec 0059: everything else that is this person's own (§7.10, G52). -----------------
        Today = Dhaka.DateOf(DateTimeOffset.UtcNow);
        var me = Me.Id;

        // My attendance this month, on the one month grid (§7.10; the grid three specs deferred).
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var days = await s.Hr.AttendanceDays.AsNoTracking()
            .Where(d => d.EmployeeId == me && d.OnDate >= monthStart && d.OnDate <= monthEnd)
            .OrderBy(d => d.OnDate)
            .Select(d => new { d.OnDate, d.Status, d.LateMinutes, d.OvertimeMinutes })
            .ToListAsync();

        var policy = await s.Hr.PayrollPolicies.AsNoTracking()
            .Where(p => p.BranchId == BranchId && p.EffectiveFrom <= monthEnd
                        && (p.EffectiveTo == null || p.EffectiveTo >= monthEnd))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync();
        var weekStart = policy is not null && policy.WeekStartsOn is >= 0 and <= 6
            ? (DayOfWeek)policy.WeekStartsOn : DayOfWeek.Saturday;

        Attendance = days.Count == 0
            ? MonthGrid.Empty(monthStart, weekStart,
                "No attendance is derived for this month yet.")
            : new MonthGrid(monthStart, weekStart,
                [new GridRow(Me.FullName, Me.EmployeeCode,
                    [.. days.Select(d => new GridCell(
                        d.OnDate, AttendanceLetter(d.Status), AttendanceTone(d.Status),
                        AttendanceWords(d.Status, d.LateMinutes, d.OvertimeMinutes)))])],
                MonthGrid.DaysOf(monthStart));

        // My payslips — only from runs that are actually final. A draft run's figures are not a
        // payslip, and showing one would be showing a number that is going to change.
        Payslips =
        [
            .. await (
                from l in s.Hr.PayrollLines.AsNoTracking()
                join r in s.Hr.PayrollRuns.AsNoTracking() on l.RunId equals r.Id
                where l.EmployeeId == me
                      && (r.State == PayrollRunState.Locked || r.State == PayrollRunState.Posted
                          || r.State == PayrollRunState.Disbursed)
                orderby r.Period descending
                select new MyPayslipRow(l.Id, r.Period, r.RunNo, l.NetPayTaka, l.Held))
                .Take(24).ToListAsync(),
        ];

        Loans =
        [
            .. (await s.Hr.Loans.AsNoTracking()
                    .Where(l => l.EmployeeId == me)
                    .OrderByDescending(l => l.StartPeriod)
                    .ToListAsync())
                .Select(l => new MyLoanRow(l.LoanNo, l.Kind, l.PrincipalTaka,
                    l.PrincipalTaka - l.RecoveredTaka, l.InstallmentTaka, l.State)),
        ];

        var pf = await s.Hr.LedgerEntries.AsNoTracking()
            .Where(e => e.EmployeeId == me && e.Kind == LedgerKind.ProvidentFund)
            .Select(e => new { e.EmployeeShareTaka, e.EmployerShareTaka })
            .ToListAsync();
        PfBalanceTaka = pf.Sum(e => e.EmployeeShareTaka + e.EmployerShareTaka);

        var fiscalStart = new DateOnly(Today.Month >= 7 ? Today.Year : Today.Year - 1, 7, 1);
        TaxWithheldTaka = await s.Hr.LedgerEntries.AsNoTracking()
            .Where(e => e.EmployeeId == me && e.Kind == LedgerKind.Tax && e.OnDate >= fiscalStart)
            .SumAsync(e => e.EmployeeShareTaka);

        BankedMinutes = await TimeRequestService.BankBalanceAsync(s.Hr, me);

        Documents =
        [
            .. (await EmployeeFileService.DocumentsAsync(s.Hr, me))
                .Select(d =>
                {
                    var (tone, words) = EmployeeFileService.ExpiryState(d.ExpiresOn, Today, 45);
                    return new MyDocumentRow(
                        DocumentKind.Label(d.Kind), d.Title, d.Number, d.ExpiresOn, tone, words);
                }),
        ];

        Letters = await s.Hr.IssuedLetters.AsNoTracking()
            .Where(l => l.EmployeeId == me)
            .OrderByDescending(l => l.IssuedOn)
            .Select(l => new MyLetterRow(l.Id, LetterKind.Label(l.Kind), l.LetterNo, l.IssuedOn))
            .ToListAsync();

        // One inbox for everything I have raised (§7.10's "my requests").
        var requests = new List<MyRequestRow>();
        requests.AddRange(await s.Hr.RegularizationRequests.AsNoTracking()
            .Where(r => r.EmployeeId == me)
            .Select(r => new MyRequestRow("Correction", r.OnDate, "mark as " + r.RequestedStatus, r.State))
            .ToListAsync());
        requests.AddRange(await s.Hr.OvertimeRequests.AsNoTracking()
            .Where(r => r.EmployeeId == me)
            .Select(r => new MyRequestRow("Overtime", r.OnDate, r.Minutes + " minutes", r.State))
            .ToListAsync());
        requests.AddRange(await s.Hr.CompOffRequests.AsNoTracking()
            .Where(r => r.EmployeeId == me)
            .Select(r => new MyRequestRow("Comp-off", r.OnDate, r.Minutes + " minutes off", r.State))
            .ToListAsync());
        requests.AddRange(await s.Hr.ShortLeaveRequests.AsNoTracking()
            .Where(r => r.EmployeeId == me)
            .Select(r => new MyRequestRow("Short leave", r.OnDate, r.Reason, r.State))
            .ToListAsync());
        requests.AddRange(await s.Hr.ExpenseClaims.AsNoTracking()
            .Where(r => r.EmployeeId == me)
            .Select(r => new MyRequestRow("Expense", r.IncurredOn, r.Description, r.State))
            .ToListAsync());
        Requests = [.. requests.OrderByDescending(r => r.OnDate).Take(50)];

        OpenProfileChange = await s.Hr.ProfileChangeRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == me
                                      && (r.State == RequestState.Raised
                                          || r.State == RequestState.Recommended));

        var acked = await s.Hr.NoticeAcknowledgements.AsNoTracking()
            .Where(a => a.EmployeeId == me)
            .Select(a => a.NoticeId)
            .ToListAsync();

        Notices =
        [
            .. (await s.Hr.Notices.AsNoTracking()
                    .Where(n => n.BranchId == BranchId && n.Active
                                && n.PublishFrom <= Today
                                && (n.PublishTo == null || n.PublishTo >= Today))
                    .OrderByDescending(n => n.PublishFrom)
                    .Take(20)
                    .ToListAsync())
                .Select(n => new MyNoticeRow(n.Id, n.Title, n.Body, n.PublishFrom,
                    n.RequiresAcknowledgement, acked.Contains(n.Id))),
        ];
    });

    // The letters a Bangladeshi muster roll already uses, so a printed grid reads like the one on
    // the wall it replaces.
    private static string AttendanceLetter(string status) => status switch
    {
        AttendanceStatus.Present => "P",
        AttendanceStatus.Absent => "A",
        AttendanceStatus.OnLeave => "L",
        AttendanceStatus.WeeklyOff => "W",
        AttendanceStatus.Holiday => "H",
        AttendanceStatus.Errand => "E",
        AttendanceStatus.Incomplete => "?",
        _ => "·",
    };

    private static string AttendanceTone(string status) => status switch
    {
        AttendanceStatus.Present => "good",
        AttendanceStatus.Absent or AttendanceStatus.Incomplete => "bad",
        AttendanceStatus.OnLeave or AttendanceStatus.Errand => "warn",
        _ => "muted",
    };

    private static string AttendanceWords(string status, int lateMinutes, int overtimeMinutes)
    {
        var parts = new List<string> { status.Replace('_', ' ') };
        if (lateMinutes > 0) parts.Add($"{lateMinutes} min late");
        if (overtimeMinutes > 0) parts.Add($"{overtimeMinutes} min overtime");
        return string.Join(" · ", parts);
    }
}
