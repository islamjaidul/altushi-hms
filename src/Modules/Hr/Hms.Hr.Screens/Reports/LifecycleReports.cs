using Hms.Hr;
using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// The registers spec 0056 adds (module PRD §12.2's "Onboarding &amp; probation register" and
/// "Compliance &amp; expiry register", plus separation, nominee and letter registers).
/// <para>
/// §12.2 lists some of these under People as <i>screens</i>. They are reports, because ADR-0029
/// obliges every new read to be a class in the catalogue, and because the decision each one leads to
/// — confirm, extend, renew — belongs on the employee's record where the context is, not in a list.
/// Every row therefore drills through to the record.
/// </para>
/// <para>
/// None of these shows pay except the settlement register, which shows nothing else.
/// </para>
/// </summary>
internal static class Lifecycle
{
    /// <summary>
    /// How far ahead a "due" register looks when the URL says nothing. Read from the employer's own
    /// notification horizon so the register, the command centre and the message all agree — three
    /// surfaces disagreeing about "soon" is exactly the confusion §12.3 exists to prevent.
    /// </summary>
    internal static async Task<int> HorizonAsync(ReportContext ctx, string kind, CancellationToken ct)
    {
        var settings = await HrAlertService.SettingsAsync(ctx.Scope.Hr, ctx.BranchId, ct);
        return settings[kind].DaysBefore;
    }

    internal static string DueWords(DateOnly on, DateOnly today)
    {
        var days = on.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => $"{-days} day{(days == -1 ? "" : "s")} overdue",
            0 => "today",
            1 => "tomorrow",
            _ => $"in {days} days",
        };
    }

    internal static string DueTone(DateOnly on, DateOnly today) =>
        on < today ? "bad" : on.DayNumber - today.DayNumber <= 7 ? "warn" : "good";
}

/// <summary>§12.2 People — the onboarding &amp; probation register (G13).</summary>
public sealed class ProbationDueReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "probation-due",
        Name = "Probation register",
        Question = "Whose probation is coming up for a decision, and who is already overdue",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var today = ctx.Period.Range.To;
        var horizon = await Lifecycle.HorizonAsync(ctx, HrNotificationKind.ProbationDue, ct);
        var cutoff = today.AddDays(horizon);
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var people = await People.Scope(ctx)
            .Where(e => e.SeparatedOn == null && e.ProbationDueOn != null && e.ProbationDueOn <= cutoff)
            .OrderBy(e => e.ProbationDueOn)
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var assignments = await People.AssignmentsOn(ctx, today)
            .Select(a => new { a.EmployeeId, a.OrgUnitId, a.DesignationId })
            .ToListAsync(ct);
        var byEmployee = assignments.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        if (ctx.Filters.OrgUnitIds.Count > 0 || ctx.Filters.DesignationId is not null
            || ctx.Filters.GradeId is not null)
        {
            people = [.. people.Where(e => byEmployee.ContainsKey(e.Id))];
        }

        var matched = people.Count;
        if (matched > ctx.MaxRows) people = [.. people.Take(ctx.MaxRows)];

        if (people.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody's probation falls due within {horizon} days of {PeriodWords.Day(today)}. "
                + "Change the warning period on Setup → Alerts to look further ahead, or check that "
                + "new hires are being given a probation due date.");
        }

        // The extension count comes from the append-only event stream rather than a column, so it
        // cannot disagree with the history the timeline shows.
        var ids = people.Select(e => e.Id).ToList();
        var extensions = await ctx.Scope.Hr.EmploymentEvents.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId) && e.Kind == EmploymentEventKind.ProbationExtended)
            .GroupBy(e => e.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Count, ct);

        var rows = people.Select(e =>
        {
            byEmployee.TryGetValue(e.Id, out var a);
            var due = e.ProbationDueOn!.Value;
            return new ReportRow(
            [
                new ReportCell(e.EmployeeCode, Href: ctx.Employee(e.Id)),
                new ReportCell(e.FullName, Href: ctx.Employee(e.Id)),
                new ReportCell(lookups.Unit(a?.OrgUnitId)),
                new ReportCell(lookups.Designation(a?.DesignationId)),
                new ReportCell(EmploymentType.Label(e.EmploymentType)),
                new ReportCell(Ui.DateLong(e.JoinedOn)),
                new ReportCell(Ui.DateLong(due)),
                new ReportCell(Lifecycle.DueWords(due, today), PillCss: Lifecycle.DueTone(due, today)),
                new ReportCell(
                    extensions.TryGetValue(e.Id, out var n) ? n.ToString() : "—",
                    Number: extensions.GetValueOrDefault(e.Id)),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Unit"), new("Designation"), new("Kind"),
                new("Joined"), new("Due"), new("When"),
                new("Extensions", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§12.2 People — contracts running out (G12).</summary>
public sealed class ContractExpiryReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "contract-expiry",
        Name = "Contract expiry register",
        Question = "Whose contract or engagement ends soon",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var today = ctx.Period.Range.To;
        var horizon = await Lifecycle.HorizonAsync(ctx, HrNotificationKind.ContractExpiring, ct);
        var cutoff = today.AddDays(horizon);
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var people = await People.Scope(ctx)
            .Where(e => e.SeparatedOn == null && e.ContractEndsOn != null && e.ContractEndsOn <= cutoff)
            .OrderBy(e => e.ContractEndsOn)
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var assignments = await People.AssignmentsOn(ctx, today)
            .Select(a => new { a.EmployeeId, a.OrgUnitId, a.DesignationId })
            .ToListAsync(ct);
        var byEmployee = assignments.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var matched = people.Count;
        if (matched > ctx.MaxRows) people = [.. people.Take(ctx.MaxRows)];

        if (people.Count == 0)
        {
            return ReportTable.Empty(
                $"No engagement ends within {horizon} days of {PeriodWords.Day(today)}. "
                + "Only employments carrying an end date appear here — set one on the employee's "
                + "record when the kind of employment is a contract or an internship.");
        }

        var rows = people.Select(e =>
        {
            byEmployee.TryGetValue(e.Id, out var a);
            var ends = e.ContractEndsOn!.Value;
            return new ReportRow(
            [
                new ReportCell(e.EmployeeCode, Href: ctx.Employee(e.Id)),
                new ReportCell(e.FullName, Href: ctx.Employee(e.Id)),
                new ReportCell(lookups.Unit(a?.OrgUnitId)),
                new ReportCell(EmploymentType.Label(e.EmploymentType)),
                new ReportCell(Ui.DateLong(e.JoinedOn)),
                new ReportCell(Ui.DateLong(ends)),
                new ReportCell(Lifecycle.DueWords(ends, today), PillCss: Lifecycle.DueTone(ends, today)),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Unit"), new("Kind"),
                new("Joined"), new("Ends"), new("When"),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>
/// §12.2 People — the compliance &amp; expiry register (G16, G21). The single question a private
/// hospital is actually asked in an inspection, and the module could not answer it at all.
/// </summary>
public sealed class DocumentExpiryReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "document-expiry",
        Name = "Compliance & expiry register",
        Question = "Which documents and professional licences have expired or are about to",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var today = ctx.Period.Range.To;
        var settings = await HrAlertService.SettingsAsync(ctx.Scope.Hr, ctx.BranchId, ct);
        var documentDays = settings[HrNotificationKind.DocumentExpiring].DaysBefore;
        var licenceDays = settings[HrNotificationKind.LicenceExpiring].DaysBefore;
        var cutoff = today.AddDays(Math.Max(documentDays, licenceDays));

        var employeeIds = await People.Scope(ctx)
            .Where(e => e.SeparatedOn == null)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var rows = await (
            from d in ctx.Scope.Hr.Documents.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on d.EmployeeId equals e.Id
            where d.BranchId == ctx.BranchId && d.SupersededAt == null
                  && employeeIds.Contains(d.EmployeeId)
                  && d.ExpiresOn != null && d.ExpiresOn <= cutoff
            orderby d.ExpiresOn
            select new
            {
                d.Kind, d.Title, d.Number, d.IssuingBody, Expires = d.ExpiresOn!.Value,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        // Each kind has its own horizon; the wider one bounded the query, so the narrower one is
        // applied here rather than issuing two queries.
        var filtered = rows
            .Where(r => r.Expires <= today.AddDays(
                r.Kind == DocumentKind.Licence ? licenceDays : documentDays))
            .ToList();

        var matched = filtered.Count;
        if (matched > ctx.MaxRows) filtered = [.. filtered.Take(ctx.MaxRows)];

        if (filtered.Count == 0)
        {
            return ReportTable.Empty(
                $"Nothing expires within {Math.Max(documentDays, licenceDays)} days of "
                + $"{PeriodWords.Day(today)}. Documents appear here only once they carry an expiry "
                + "date — add one on the employee's personal file.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Kind"), new("Document"),
                new("Number"), new("Issuing body"), new("Expires"), new("When"),
            ],
            Rows =
            [
                .. filtered.Select(r => new ReportRow(
                [
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(DocumentKind.Label(r.Kind)),
                    new ReportCell(r.Title),
                    new ReportCell(r.Number ?? "—"),
                    new ReportCell(r.IssuingBody ?? "—"),
                    new ReportCell(Ui.DateLong(r.Expires)),
                    new ReportCell(Lifecycle.DueWords(r.Expires, today),
                        PillCss: Lifecycle.DueTone(r.Expires, today)),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>§7.1 `[hospital]` — every professional registration on file, valid or not (G16).</summary>
public sealed class LicenceRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "licence-register",
        Name = "Professional licence register",
        Question = "Every registration and licence on file, and whether it is current",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var today = ctx.Period.Range.To;

        var employeeIds = await People.Scope(ctx).Select(e => e.Id).ToListAsync(ct);

        var rows = await (
            from d in ctx.Scope.Hr.Documents.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on d.EmployeeId equals e.Id
            where d.BranchId == ctx.BranchId && d.SupersededAt == null
                  && d.Kind == DocumentKind.Licence && employeeIds.Contains(d.EmployeeId)
            orderby e.FullName
            select new
            {
                d.Title, d.Number, d.IssuingBody, d.IssuedOn, d.ExpiresOn,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName, e.SeparatedOn,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                "No professional licence is on file for anyone in this selection. Record one on an "
                + "employee's personal file — kind \"Professional licence\", with the council that "
                + "issued it.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Licence"), new("Number"),
                new("Issuing body"), new("Issued"), new("Expires"), new("Status"),
            ],
            Rows =
            [
                .. rows.Select(r =>
                {
                    var (tone, words) = EmployeeFileService.ExpiryState(r.ExpiresOn, today, 45);
                    return new ReportRow(
                    [
                        new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                        new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                        new ReportCell(r.Title),
                        new ReportCell(r.Number ?? "—"),
                        new ReportCell(r.IssuingBody ?? "—"),
                        new ReportCell(r.IssuedOn is { } i ? Ui.DateLong(i) : "—"),
                        new ReportCell(r.ExpiresOn is { } x ? Ui.DateLong(x) : "—"),
                        new ReportCell(words, PillCss: tone),
                    ]);
                }),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>
/// §7.1 (G14) — who each employee has nominated, and whether the instruction is complete. An
/// incomplete nomination is a problem discovered on the worst possible day; this makes it a row.
/// </summary>
public sealed class NomineeRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "nominee-register",
        Name = "Nominee register",
        Question = "Who is nominated for provident fund and gratuity, and whose nomination is incomplete",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var employees = await People.Scope(ctx)
            .Where(e => e.SeparatedOn == null)
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        if (employees.Count == 0)
        {
            return ReportTable.Empty(
                "No employee matches this selection. Remove a filter, or widen the period.");
        }

        var ids = employees.Select(e => e.Id).ToList();
        var nominations = await ctx.Scope.Hr.Dependants.AsNoTracking()
            .Where(d => ids.Contains(d.EmployeeId) && d.IsNominee && d.SupersededAt == null)
            .Select(d => new { d.EmployeeId, d.Name, d.Relation, d.Purpose, d.ShareBp })
            .ToListAsync(ct);

        var byEmployee = nominations.GroupBy(n => n.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matched = employees.Count;
        var shown = matched > ctx.MaxRows ? employees.Take(ctx.MaxRows).ToList() : employees;

        var rows = shown.Select(e =>
        {
            var mine = byEmployee.GetValueOrDefault(e.Id, []);
            var pf = mine.Where(n => n.Purpose is NomineePurpose.ProvidentFund or NomineePurpose.Both)
                .Sum(n => n.ShareBp);
            var gratuity = mine.Where(n => n.Purpose is NomineePurpose.Gratuity or NomineePurpose.Both)
                .Sum(n => n.ShareBp);

            var complete = pf == EmployeeFileService.WholeShareBp
                           && gratuity == EmployeeFileService.WholeShareBp;

            return new ReportRow(
            [
                new ReportCell(e.EmployeeCode, Href: ctx.Employee(e.Id)),
                new ReportCell(e.FullName, Href: ctx.Employee(e.Id)),
                new ReportCell(mine.Count.ToString(), Number: mine.Count),
                new ReportCell(mine.Count == 0 ? "—" : string.Join(", ",
                    mine.Select(n => $"{n.Name} ({n.Relation})"))),
                new ReportCell($"{pf / 100m:0.##}%", Number: pf),
                new ReportCell($"{gratuity / 100m:0.##}%", Number: gratuity),
                new ReportCell(
                    complete ? "Complete" : mine.Count == 0 ? "Nobody nominated" : "Does not total 100%",
                    PillCss: complete ? "good" : "bad"),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Nominees", ReportAlign.Right), new("Who"),
                new("PF share", ReportAlign.Right), new("Gratuity share", ReportAlign.Right),
                new("Status"),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§12.2 People — separation &amp; clearance, as a queue rather than a screen (G18).</summary>
public sealed class SeparationClearanceReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "separation-clearance",
        Name = "Separation & clearance",
        Question = "Who is leaving, where their clearance has got to, and what is holding it up",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var separations = await (
            from s in ctx.Scope.Hr.Separations.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on s.EmployeeId equals e.Id
            where s.BranchId == ctx.BranchId && s.State != SeparationState.Cancelled
                  && s.NoticeOn <= range.To && s.LastWorkingDay >= range.From
            orderby s.LastWorkingDay
            select new
            {
                s.Id, s.Kind, s.State, s.NoticeOn, s.LastWorkingDay, s.RelievingOn,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = separations.Count;
        if (matched > ctx.MaxRows) separations = [.. separations.Take(ctx.MaxRows)];

        if (separations.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody is leaving in {PeriodWords.Describe(ctx.Period)}. A separation appears here "
                + "from the day notice is recorded until it is closed.");
        }

        var ids = separations.Select(s => s.Id).ToList();
        var items = await ctx.Scope.Hr.ClearanceItems.AsNoTracking()
            .Where(i => ids.Contains(i.SeparationId))
            .Select(i => new { i.SeparationId, i.Department, i.Status })
            .ToListAsync(ct);
        var bySeparation = items.GroupBy(i => i.SeparationId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = separations.Select(s =>
        {
            var mine = bySeparation.GetValueOrDefault(s.Id, []);
            var outstanding = mine
                .Where(i => i.Status is ClearanceStatus.Pending or ClearanceStatus.Blocked)
                .Select(i => ClearanceDepartment.Label(i.Department))
                .ToList();

            return new ReportRow(
            [
                new ReportCell(s.EmployeeCode, Href: ctx.Employee(s.EmployeeId)),
                new ReportCell(s.FullName, Href: ctx.Employee(s.EmployeeId)),
                new ReportCell(SeparationKind.Label(s.Kind)),
                new ReportCell(Ui.DateLong(s.NoticeOn)),
                new ReportCell(Ui.DateLong(s.LastWorkingDay)),
                new ReportCell(s.RelievingOn is { } r ? Ui.DateLong(r) : "—"),
                new ReportCell(SeparationState.Label(s.State),
                    PillCss: s.State == SeparationState.DocumentsIssued ? "good" : "warn"),
                new ReportCell(
                    outstanding.Count == 0 ? "All signed off" : string.Join(", ", outstanding),
                    Number: outstanding.Count,
                    PillCss: outstanding.Count == 0 ? "good" : "bad"),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Kind"), new("Notice"), new("Last day"),
                new("Relieved"), new("Stage"), new("Outstanding"),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>
/// §12.2 Payroll — the final-settlement register (G19). <b>Salary-bearing</b>: it is entirely money,
/// so a reader without <c>hr.reports.salary</c> sees it listed as unavailable with the reason and no
/// query runs at all.
/// </summary>
public sealed class SettlementRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "settlement-register",
        Name = "Final settlement register",
        Question = "What each departing employee was owed, and what it was made of",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var settlements = await (
            from s in ctx.Scope.Hr.Separations.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on s.EmployeeId equals e.Id
            where s.BranchId == ctx.BranchId && s.ComputedAt != null
                  && s.State != SeparationState.Cancelled
                  && s.LastWorkingDay >= range.From && s.LastWorkingDay <= range.To
            orderby s.LastWorkingDay
            select new
            {
                s.Id, s.Kind, s.State, s.LastWorkingDay, s.NetPayableTaka,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = settlements.Count;
        if (matched > ctx.MaxRows) settlements = [.. settlements.Take(ctx.MaxRows)];

        if (settlements.Count == 0)
        {
            return ReportTable.Empty(
                $"No settlement was computed for a last working day in "
                + $"{PeriodWords.Describe(ctx.Period)}. A separation appears here once its statement "
                + "has been computed, which requires every department to have signed off.");
        }

        var ids = settlements.Select(s => s.Id).ToList();
        var lines = await ctx.Scope.Hr.SettlementLines.AsNoTracking()
            .Where(l => ids.Contains(l.SeparationId))
            .Select(l => new { l.SeparationId, l.Source, l.Kind, l.AmountTaka })
            .ToListAsync(ct);
        var bySeparation = lines.GroupBy(l => l.SeparationId).ToDictionary(g => g.Key, g => g.ToList());

        long Sum(long separationId, string source) =>
            bySeparation.GetValueOrDefault(separationId, []).Where(l => l.Source == source)
                .Sum(l => l.AmountTaka);

        var rows = settlements.Select(s => new ReportRow(
        [
            new ReportCell(s.EmployeeCode, Href: ctx.Employee(s.EmployeeId)),
            new ReportCell(s.FullName, Href: ctx.Employee(s.EmployeeId)),
            new ReportCell(SeparationKind.Label(s.Kind)),
            new ReportCell(Ui.DateLong(s.LastWorkingDay)),
            Money(Sum(s.Id, SettlementSource.UnpaidSalary)),
            Money(Sum(s.Id, SettlementSource.LeaveEncashment)),
            Money(Sum(s.Id, SettlementSource.Gratuity)),
            Money(Sum(s.Id, SettlementSource.NoticePay)),
            Money(Sum(s.Id, SettlementSource.LoanRecovery) + Sum(s.Id, SettlementSource.ClearanceDues)),
            Money(s.NetPayableTaka),
            new ReportCell(SeparationState.Label(s.State),
                PillCss: s.State is SeparationState.Paid or SeparationState.DocumentsIssued
                    ? "good" : "warn"),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Kind"), new("Last day"),
                new("Unpaid salary", ReportAlign.Right, Money: true),
                new("Encashment", ReportAlign.Right, Money: true),
                new("Gratuity", ReportAlign.Right, Money: true),
                new("Notice", ReportAlign.Right, Money: true),
                new("Recoveries", ReportAlign.Right, Money: true),
                new("Net payable", ReportAlign.Right, Money: true),
                new("Stage"),
            ],
            Rows = rows,
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell($"{settlements.Count} settlement{(settlements.Count == 1 ? "" : "s")}"),
                Money(settlements.Sum(s => Sum(s.Id, SettlementSource.UnpaidSalary))),
                Money(settlements.Sum(s => Sum(s.Id, SettlementSource.LeaveEncashment))),
                Money(settlements.Sum(s => Sum(s.Id, SettlementSource.Gratuity))),
                Money(settlements.Sum(s => Sum(s.Id, SettlementSource.NoticePay))),
                Money(settlements.Sum(s =>
                    Sum(s.Id, SettlementSource.LoanRecovery) + Sum(s.Id, SettlementSource.ClearanceDues))),
                Money(settlements.Sum(s => s.NetPayableTaka)),
                new ReportCell(""),
            ],
        };

        static ReportCell Money(long taka) =>
            new(taka == 0 ? "—" : Ui.Money(taka), Number: taka);
    }
}

/// <summary>§7.14 Governance — every letter the employer has issued (G20).</summary>
public sealed class LettersIssuedReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "letters-issued",
        Name = "Letters issued",
        Question = "Which letters and certificates were issued, to whom, and by whom",
        Category = ReportCategory.Governance,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
        PermissionOverride = "hr.audit.view",
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var letters = await (
            from l in ctx.Scope.Hr.IssuedLetters.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
            where l.BranchId == ctx.BranchId
                  && l.IssuedOn >= range.From && l.IssuedOn <= range.To
                  && (ctx.Filters.EmployeeId == null || l.EmployeeId == ctx.Filters.EmployeeId)
            orderby l.IssuedOn descending, l.Id descending
            select new
            {
                l.Id, l.LetterNo, l.Kind, l.Title, l.IssuedOn, l.CreatedByName,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = letters.Count;
        if (matched > ctx.MaxRows) letters = [.. letters.Take(ctx.MaxRows)];

        if (letters.Count == 0)
        {
            return ReportTable.Empty(
                $"No letter was issued in {PeriodWords.Describe(ctx.Period)}. Letters are issued "
                + "from an employee's record, and every one issued is listed here permanently.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Number"), new("Letter"), new("Code"), new("Employee"),
                new("Issued"), new("By"),
            ],
            Rows =
            [
                .. letters.Select(l => new ReportRow(
                [
                    new ReportCell(l.LetterNo),
                    new ReportCell(LetterKind.Label(l.Kind)),
                    new ReportCell(l.EmployeeCode, Href: ctx.Employee(l.EmployeeId)),
                    new ReportCell(l.FullName, Href: ctx.Employee(l.EmployeeId)),
                    new ReportCell(Ui.DateLong(l.IssuedOn)),
                    new ReportCell(l.CreatedByName),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>
/// §7.14 (G57) — birthdays and work anniversaries in the period. Small, and the one register in the
/// module an HR officer will open for a pleasant reason.
/// </summary>
public sealed class AnniversariesReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "anniversaries",
        Name = "Birthdays & anniversaries",
        Question = "Whose birthday or work anniversary falls in this period",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var people = await People.Scope(ctx)
            .Where(e => e.SeparatedOn == null)
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName, e.DateOfBirth, e.JoinedOn })
            .ToListAsync(ct);

        var found = new List<(DateOnly On, string What, string Detail, long Id, string Code, string Name)>();

        foreach (var p in people)
        {
            if (p.DateOfBirth is { } dob
                && HrAlertService.NextOccurrence(dob, range.From) is { } birthday
                && birthday <= range.To)
            {
                found.Add((birthday, "Birthday", $"Turns {birthday.Year - dob.Year}",
                    p.Id, p.EmployeeCode, p.FullName));
            }

            if (HrAlertService.NextOccurrence(p.JoinedOn, range.From) is { } anniversary
                && anniversary <= range.To)
            {
                var years = anniversary.Year - p.JoinedOn.Year;
                if (years >= 1)
                {
                    found.Add((anniversary, "Work anniversary",
                        $"{years} year{(years == 1 ? "" : "s")} of service",
                        p.Id, p.EmployeeCode, p.FullName));
                }
            }
        }

        if (found.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody has a birthday or work anniversary in {PeriodWords.Describe(ctx.Period)}. "
                + "A birthday needs a date of birth on the employee's record.");
        }

        var ordered = found.OrderBy(f => f.On).ThenBy(f => f.Name).ToList();
        var matched = ordered.Count;
        if (matched > ctx.MaxRows) ordered = [.. ordered.Take(ctx.MaxRows)];

        return new ReportTable
        {
            Columns = [new("Date"), new("Occasion"), new("Code"), new("Name"), new("Detail")],
            Rows =
            [
                .. ordered.Select(f => new ReportRow(
                [
                    new ReportCell(Ui.DateLong(f.On)),
                    new ReportCell(f.What),
                    new ReportCell(f.Code, Href: ctx.Employee(f.Id)),
                    new ReportCell(f.Name, Href: ctx.Employee(f.Id)),
                    new ReportCell(f.Detail),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}
