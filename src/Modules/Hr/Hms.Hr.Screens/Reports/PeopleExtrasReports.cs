using Hms.Hr;
using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// The registers spec 0059 adds (§13). The `[S]`/`[C]` set — training, appraisal, discipline, assets,
/// notices, expenses — plus the leave-year registers §7.6 asks for.
/// <para>
/// <b>The disciplinary register is not here.</b> §11 makes that record permission-restricted and
/// "hidden entirely without it", and a report the centre lists as unavailable-with-a-reason still
/// tells a reader that something exists. It is reached from the employee's record by a holder of the
/// claim, and from nowhere else.
/// </para>
/// </summary>
public sealed class TrainingComplianceReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "training-compliance",
        Name = "Training & certification",
        Question = "Who is trained, what expires when, and who is out of compliance",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var today = ctx.Period.Range.To;

        var rows = await (
            from t in ctx.Scope.Hr.TrainingRecords.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on t.EmployeeId equals e.Id
            where t.BranchId == ctx.BranchId && e.SeparatedOn == null
                  && (ctx.Filters.EmployeeId == null || t.EmployeeId == ctx.Filters.EmployeeId)
            orderby t.ExpiresOn == null, t.ExpiresOn
            select new
            {
                t.Title, t.Provider, t.CompletedOn, t.ExpiresOn, t.CertificateNo,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                "No training is recorded. A certification with an expiry date appears here and warns "
                + "before it lapses — the same expiry engine the document register uses.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Employee"), new("Training"), new("Provider"),
                new("Completed"), new("Expires"), new("Status"),
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
                        new ReportCell(r.Provider ?? "—"),
                        new ReportCell(Ui.DateLong(r.CompletedOn)),
                        new ReportCell(r.ExpiresOn is { } x ? Ui.DateLong(x) : "—"),
                        new ReportCell(words, PillCss: tone),
                    ]);
                }),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 — what the employer owns and who is holding it (G17).</summary>
public sealed class AssetRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "asset-register",
        Name = "Asset register",
        Question = "What the employer owns, who is holding it, and what has not come back",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var assets = await ctx.Scope.Hr.Assets.AsNoTracking()
            .Where(a => a.BranchId == ctx.BranchId && a.Active)
            .OrderBy(a => a.Code)
            .ToListAsync(ct);

        if (assets.Count == 0)
        {
            return ReportTable.Empty(
                "No asset is registered. What an employee holds — ID card, uniform, laptop, keys — "
                + "surfaces at clearance when it is recorded here.");
        }

        var issues = await (
            from i in ctx.Scope.Hr.AssetIssues.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on i.EmployeeId equals e.Id
            where i.BranchId == ctx.BranchId && i.ReturnedOn == null
            select new { i.AssetId, EmployeeId = e.Id, e.EmployeeCode, e.FullName, i.IssuedOn })
            .ToListAsync(ct);
        var heldBy = issues.ToDictionary(i => i.AssetId);

        var matched = assets.Count;
        if (matched > ctx.MaxRows) assets = [.. assets.Take(ctx.MaxRows)];

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Asset"), new("Category"), new("Serial"),
                new("Value", ReportAlign.Right, Money: true), new("Held by"), new("Since"),
            ],
            Rows =
            [
                .. assets.Select(a =>
                {
                    heldBy.TryGetValue(a.Id, out var issue);
                    return new ReportRow(
                    [
                        new ReportCell(a.Code),
                        new ReportCell(a.Name),
                        new ReportCell(a.Category ?? "—"),
                        new ReportCell(a.SerialNo ?? "—"),
                        new ReportCell(a.ValueTaka == 0 ? "—" : Ui.Money(a.ValueTaka),
                            Number: a.ValueTaka),
                        issue is null
                            ? new ReportCell("in store", PillCss: "good")
                            : new ReportCell(issue.FullName, Href: ctx.Employee(issue.EmployeeId),
                                PillCss: "warn"),
                        new ReportCell(issue is null ? "—" : Ui.DateLong(issue.IssuedOn)),
                    ]);
                }),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 — the appraisal cycle (G60).</summary>
public sealed class AppraisalReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "appraisals",
        Name = "Appraisals",
        Question = "Where each appraisal has got to, and what it concluded",
        Category = ReportCategory.Management,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var rows = await (
            from a in ctx.Scope.Hr.Appraisals.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on a.EmployeeId equals e.Id
            where a.BranchId == ctx.BranchId
                  && a.PeriodFrom <= range.To && a.PeriodTo >= range.From
                  && (ctx.Filters.EmployeeId == null || a.EmployeeId == ctx.Filters.EmployeeId)
            orderby e.EmployeeCode
            select new
            {
                a.Cycle, a.PeriodFrom, a.PeriodTo, a.State, a.SelfRating, a.ManagerRating,
                a.ManagerName, a.Outcome, EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                $"No appraisal covers {PeriodWords.Describe(ctx.Period)}. The rating scale is the "
                + "employer's own — this product ships no competency framework and no opinion about "
                + "whether performance runs 1–5 or A–E.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Employee"), new("Cycle"), new("Period"),
                new("Self", ReportAlign.Right), new("Manager", ReportAlign.Right),
                new("By"), new("Outcome"), new("State"),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.Cycle),
                    new ReportCell($"{r.PeriodFrom:MMM yyyy} – {r.PeriodTo:MMM yyyy}"),
                    new ReportCell(r.SelfRating?.ToString() ?? "—", Number: r.SelfRating),
                    new ReportCell(r.ManagerRating?.ToString() ?? "—", Number: r.ManagerRating),
                    new ReportCell(r.ManagerName ?? "—"),
                    new ReportCell(r.Outcome ?? "—"),
                    new ReportCell(AppraisalState.Label(r.State),
                        PillCss: r.State == AppraisalState.Closed ? "good" : "warn"),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 — expense claims (G56).</summary>
public sealed class ExpenseClaimReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "expense-claims",
        Name = "Expense claims",
        Question = "What was claimed, what was approved, and what is still outstanding",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var rows = await (
            from c in ctx.Scope.Hr.ExpenseClaims.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on c.EmployeeId equals e.Id
            where c.BranchId == ctx.BranchId
                  && c.IncurredOn >= range.From && c.IncurredOn <= range.To
                  && (ctx.Filters.EmployeeId == null || c.EmployeeId == ctx.Filters.EmployeeId)
            orderby c.IncurredOn descending
            select new
            {
                c.ClaimNo, c.IncurredOn, c.Category, c.Description, c.AmountTaka,
                c.ApprovedAmountTaka, c.State, c.ReimbursedVia,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                $"No expense claim falls in {PeriodWords.Describe(ctx.Period)}.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Number"), new("Date"), new("Code"), new("Employee"),
                new("Category"), new("What"),
                new("Claimed", ReportAlign.Right, Money: true),
                new("Approved", ReportAlign.Right, Money: true),
                new("State"),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.ClaimNo),
                    new ReportCell(Ui.DateLong(r.IncurredOn)),
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.Category),
                    new ReportCell(r.Description),
                    new ReportCell(Ui.Money(r.AmountTaka), Number: r.AmountTaka),
                    new ReportCell(r.ApprovedAmountTaka is { } a ? Ui.Money(a) : "—",
                        Number: r.ApprovedAmountTaka),
                    new ReportCell(RequestState.Label(r.State), PillCss: RequestState.Tone(r.State)),
                ])),
            ],
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell($"{rows.Count} claim{(rows.Count == 1 ? "" : "s")}"),
                new ReportCell(""), new ReportCell(""),
                new ReportCell(Ui.Money(rows.Sum(r => r.AmountTaka)),
                    Number: rows.Sum(r => r.AmountTaka)),
                new ReportCell(Ui.Money(rows.Sum(r => r.ApprovedAmountTaka ?? 0)),
                    Number: rows.Sum(r => r.ApprovedAmountTaka ?? 0)),
                new ReportCell(""),
            ],
        };
    }
}

/// <summary>
/// §13 — the leave liability Finance asks for every year-end (§7.6 `[S]`): what the carried balances
/// would cost if they were all encashed.
/// <para>
/// Salary-bearing, obviously — it is a money figure per person. The day rate is the same one the
/// settlement uses, so the two agree.
/// </para>
/// </summary>
public sealed class LeaveLiabilityReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "leave-liability",
        Name = "Leave liability",
        Question = "What the carried leave balances would cost if they were encashed",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.LeaveYear,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var year = ctx.Period.Range.To.Year;

        var balances = await ctx.Scope.Hr.LeaveBalances.AsNoTracking()
            .Where(b => b.LeaveYear == year)
            .Select(b => new
            {
                b.EmployeeId, b.LeaveTypeId,
                Available = b.OpeningBp + b.AccruedBp + b.AdjustmentBp - b.AvailedBp - b.EncashedBp,
            })
            .ToListAsync(ct);

        if (balances.Count == 0)
        {
            return ReportTable.Empty(
                $"No leave balance exists for {year}. Balances open when a leave policy is configured "
                + "and the year is closed onto them.");
        }

        var byEmployee = balances.GroupBy(b => b.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Available));

        var ids = byEmployee.Keys.ToList();
        var people = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.SeparatedOn == null
                        && (ctx.Filters.EmployeeId == null || e.Id == ctx.Filters.EmployeeId))
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        if (people.Count == 0)
            return ReportTable.Empty("No serving employee has a leave balance for this year.");

        // The gross each person is on, so the liability is priced the way the settlement prices it.
        var earningIds = await ctx.Scope.Hr.PayComponents.AsNoTracking()
            .Where(c => c.BranchId == ctx.BranchId && c.Kind == PayComponentKind.Earning)
            .Select(c => c.Id).ToListAsync(ct);

        var structures = await ctx.Scope.Hr.PayStructures.AsNoTracking()
            .Where(p => ids.Contains(p.EmployeeId) && p.EffectiveTo == null)
            .Select(p => new { p.Id, p.EmployeeId })
            .ToListAsync(ct);
        var structureIds = structures.Select(x => x.Id).ToList();

        var amounts = await ctx.Scope.Hr.PayStructureComponents.AsNoTracking()
            .Where(c => structureIds.Contains(c.PayStructureId) && earningIds.Contains(c.ComponentId))
            .Select(c => new { c.PayStructureId, c.AmountTaka })
            .ToListAsync(ct);

        var grossByStructure = amounts.GroupBy(a => a.PayStructureId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AmountTaka));
        var grossOf = structures.Where(s => grossByStructure.ContainsKey(s.Id))
            .ToDictionary(s => s.EmployeeId, s => grossByStructure[s.Id]);

        var matched = people.Count;
        if (matched > ctx.MaxRows) people = [.. people.Take(ctx.MaxRows)];

        var rows = people.Select(p =>
        {
            var days = byEmployee.GetValueOrDefault(p.Id);
            var gross = grossOf.GetValueOrDefault(p.Id);
            var dayRate = gross / 30;             // the same divisor the settlement's Fixed30 uses
            var liability = dayRate * days / 10_000;

            return new ReportRow(
            [
                new ReportCell(p.EmployeeCode, Href: ctx.Employee(p.Id)),
                new ReportCell(p.FullName, Href: ctx.Employee(p.Id)),
                new ReportCell((days / 10000m).ToString("0.##"), Number: days),
                new ReportCell(gross == 0 ? "—" : Ui.Money(gross), Number: gross),
                new ReportCell(liability == 0 ? "—" : Ui.Money(liability), Number: liability),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Employee"),
                new("Days carried", ReportAlign.Right),
                new("Monthly gross", ReportAlign.Right, Money: true),
                new("Liability", ReportAlign.Right, Money: true),
            ],
            Rows = rows,
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""),
                new ReportCell($"{rows.Count} employee{(rows.Count == 1 ? "" : "s")}"),
                new ReportCell((rows.Sum(r => r.Cells[2].Number ?? 0) / 10000m).ToString("0.##")),
                new ReportCell(""),
                new ReportCell(Ui.Money(rows.Sum(r => r.Cells[4].Number ?? 0)),
                    Number: rows.Sum(r => r.Cells[4].Number ?? 0)),
            ],
        };
    }
}

/// <summary>§13 Governance — profile changes employees proposed and HR decided (G54).</summary>
public sealed class ProfileChangeReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "profile-changes",
        Name = "Profile change requests",
        Question = "What employees asked to correct on their own record, and what HR decided",
        Category = ReportCategory.Governance,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
        PermissionOverride = "hr.audit.view",
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var rows = await (
            from r in ctx.Scope.Hr.ProfileChangeRequests.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
            where r.BranchId == ctx.BranchId
            orderby r.RaisedAt descending
            select new
            {
                r.RaisedAt, r.State, r.DecidedByName, r.DecisionNote,
                r.Phone, r.Email, r.PresentAddress, r.EmergencyContact, r.BankAccountNo,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                "Nobody has proposed a change to their own record. Until this existed HR edited "
                + "everything, including a phone number only the employee knows.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Raised"), new("Code"), new("Employee"), new("Asked to change"),
                new("State"), new("Decided by"),
            ],
            Rows =
            [
                .. rows.Select(r =>
                {
                    var fields = new List<string>();
                    if (r.Phone is not null) fields.Add("phone");
                    if (r.Email is not null) fields.Add("email");
                    if (r.PresentAddress is not null) fields.Add("address");
                    if (r.EmergencyContact is not null) fields.Add("emergency contact");
                    if (r.BankAccountNo is not null) fields.Add("bank account");

                    return new ReportRow(
                    [
                        new ReportCell(Ui.DateLong(DateOnly.FromDateTime(Ui.Local(r.RaisedAt).DateTime))),
                        new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                        new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                        new ReportCell(string.Join(", ", fields)),
                        new ReportCell(RequestState.Label(r.State), PillCss: RequestState.Tone(r.State)),
                        new ReportCell(r.DecidedByName ?? "—"),
                    ]);
                }),
            ],
            MatchedRows = matched,
        };
    }
}
