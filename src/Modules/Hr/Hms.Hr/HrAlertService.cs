using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// One thing that falls due. Carries everything a register row, a dashboard tile and a message all
/// need, so the three cannot disagree about a date.
/// </summary>
public sealed record HrAlert(
    string Kind,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? Phone,
    DateOnly OnDate,
    int DaysAway,
    string Subject,
    string Detail)
{
    /// <summary>Overdue reads red, due-soon amber. The same words everywhere they appear.</summary>
    public string Tone => DaysAway < 0 ? "bad" : DaysAway <= 7 ? "warn" : "good";

    public string When => DaysAway switch
    {
        < 0 => $"{-DaysAway} day{(DaysAway == -1 ? "" : "s")} overdue",
        0 => "today",
        1 => "tomorrow",
        _ => $"in {DaysAway} days",
    };

    /// <summary>What an SMS would say. Never contains a salary figure (D6).</summary>
    public string Message(string organisation) =>
        $"{organisation}: {Subject}. {Detail}.";
}

/// <summary>
/// Whether this host can actually deliver a message, and by what channel. The ERP SKU ships M20 and
/// its SMS gateway; the standalone HRM SKU references neither (ADR-0025 — what the customer bought is
/// what they receive). Registered by each host, read by the switches screen so it can say what it
/// really does rather than offer a switch that controls nothing.
/// </summary>
public sealed record HrNotificationChannel(bool Available, string Name)
{
    public static readonly HrNotificationChannel None = new(false, "");
}

/// <summary>
/// What is falling due across the module (module PRD §7.14, G57): probations, contracts, documents,
/// professional licences, birthdays and work anniversaries.
/// <para>
/// <b>Alerts are derived, never stored.</b> A stored alert outlives its cause — the licence is
/// renewed, the row is not, and the register goes on warning about a document that is now valid.
/// Everything here is a query over the facts, so a renewal silences its own alert.
/// </para>
/// <para>
/// Nothing here sends anything. It computes; a host decides how (or whether) to deliver. That is
/// what lets both SKUs show the same registers when only one of them ships a gateway.
/// </para>
/// </summary>
public sealed class HrAlertService(AuditWriter audit, TimeProvider clock)
{
    /// <summary>The employer's switches, with a stated default for any kind never configured.</summary>
    public static async Task<IReadOnlyDictionary<string, NotificationSetting>> SettingsAsync(
        HrDbContext hr, long branchId, CancellationToken ct = default)
    {
        var stored = await hr.NotificationSettings.AsNoTracking()
            .Where(s => s.BranchId == branchId)
            .ToDictionaryAsync(s => s.Kind, ct);

        var all = new Dictionary<string, NotificationSetting>();
        foreach (var kind in HrNotificationKind.All)
        {
            all[kind] = stored.TryGetValue(kind, out var row) ? row : new NotificationSetting
            {
                BranchId = branchId,
                Kind = kind,
                Enabled = false,
                DaysBefore = HrNotificationKind.DefaultDaysBefore(kind),
            };
        }
        return all;
    }

    public async Task SaveSettingAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, string kind, bool enabled, int daysBefore,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!HrNotificationKind.All.Contains(kind))
            throw new HrException("That is not an alert this product raises.");
        if (daysBefore is < 0 or > 365)
            throw new HrException("A warning runs from 0 to 365 days ahead.");

        var row = await hr.NotificationSettings
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.Kind == kind, ct);

        var before = row is null ? null : new { row.Enabled, row.DaysBefore };

        if (row is null)
        {
            row = new NotificationSetting { BranchId = branchId, Kind = kind };
            hr.NotificationSettings.Add(row);
        }

        row.Enabled = enabled;
        row.DaysBefore = daysBefore;
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.notification.configure",
            "hr.notification_setting", row.Id, before: before, after: new { kind, enabled, daysBefore },
            tier: 2);
    }

    /// <summary>
    /// Everything due on or before each kind's horizon, plus everything already overdue. Overdue is
    /// deliberately unbounded: a licence that expired three months ago is more urgent than one
    /// expiring next week, and dropping it off the list is how it stays expired.
    /// </summary>
    public static async Task<IReadOnlyList<HrAlert>> DueAsync(
        HrDbContext hr, long branchId, DateOnly today,
        IReadOnlyDictionary<string, NotificationSetting> settings,
        CancellationToken ct = default)
    {
        var alerts = new List<HrAlert>();
        int Horizon(string kind) => settings.TryGetValue(kind, out var s)
            ? s.DaysBefore : HrNotificationKind.DefaultDaysBefore(kind);

        // ---- probation ---------------------------------------------------------------------------
        var probationBy = today.AddDays(Horizon(HrNotificationKind.ProbationDue));
        alerts.AddRange(await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.SeparatedOn == null
                        && e.ProbationDueOn != null && e.ProbationDueOn <= probationBy)
            .OrderBy(e => e.ProbationDueOn)
            .Select(e => new HrAlert(
                HrNotificationKind.ProbationDue, e.Id, e.EmployeeCode, e.FullName, e.Phone,
                e.ProbationDueOn!.Value, e.ProbationDueOn!.Value.DayNumber - today.DayNumber,
                "Probation falls due", ""))
            .ToListAsync(ct));

        // ---- contracts ----------------------------------------------------------------------------
        var contractBy = today.AddDays(Horizon(HrNotificationKind.ContractExpiring));
        alerts.AddRange(await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.SeparatedOn == null
                        && e.ContractEndsOn != null && e.ContractEndsOn <= contractBy)
            .OrderBy(e => e.ContractEndsOn)
            .Select(e => new HrAlert(
                HrNotificationKind.ContractExpiring, e.Id, e.EmployeeCode, e.FullName, e.Phone,
                e.ContractEndsOn!.Value, e.ContractEndsOn!.Value.DayNumber - today.DayNumber,
                "Contract ends", ""))
            .ToListAsync(ct));

        // ---- documents and licences ----------------------------------------------------------------
        var documentBy = today.AddDays(Horizon(HrNotificationKind.DocumentExpiring));
        var licenceBy = today.AddDays(Horizon(HrNotificationKind.LicenceExpiring));
        var latest = documentBy > licenceBy ? documentBy : licenceBy;

        var expiring = await (
            from d in hr.Documents.AsNoTracking()
            join e in hr.Employees.AsNoTracking() on d.EmployeeId equals e.Id
            where d.BranchId == branchId && d.SupersededAt == null && e.SeparatedOn == null
                  && d.ExpiresOn != null && d.ExpiresOn <= latest
            orderby d.ExpiresOn
            select new { d.Kind, d.Title, d.Number, d.IssuingBody, Expires = d.ExpiresOn!.Value,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName, e.Phone })
            .ToListAsync(ct);

        foreach (var d in expiring)
        {
            var isLicence = d.Kind == DocumentKind.Licence;
            var horizon = isLicence ? licenceBy : documentBy;
            if (d.Expires > horizon) continue;

            alerts.Add(new HrAlert(
                isLicence ? HrNotificationKind.LicenceExpiring : HrNotificationKind.DocumentExpiring,
                d.EmployeeId, d.EmployeeCode, d.FullName, d.Phone, d.Expires,
                d.Expires.DayNumber - today.DayNumber,
                isLicence ? "Professional licence expires" : $"{DocumentKind.Label(d.Kind)} expires",
                string.Join(" · ", new[] { d.Title, d.Number, d.IssuingBody }
                    .Where(x => !string.IsNullOrWhiteSpace(x))!)));
        }

        // ---- birthdays and work anniversaries --------------------------------------------------------
        alerts.AddRange(await AnniversariesAsync(hr, branchId, today,
            Horizon(HrNotificationKind.Birthday), Horizon(HrNotificationKind.WorkAnniversary), ct));

        return [.. alerts.OrderBy(a => a.DaysAway).ThenBy(a => a.EmployeeName)];
    }

    /// <summary>
    /// Birthdays and anniversaries within their horizons. The month filter runs in SQL and the
    /// day-of-year arithmetic in memory, because "the same date next year" is not a comparison
    /// Postgres can index and the candidate set is at most two months of a directory.
    /// </summary>
    private static async Task<List<HrAlert>> AnniversariesAsync(
        HrDbContext hr, long branchId, DateOnly today, int birthdayDays, int anniversaryDays,
        CancellationToken ct)
    {
        var span = Math.Max(birthdayDays, anniversaryDays);
        var months = MonthsTouched(today, span);

        var people = await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.SeparatedOn == null
                        && (months.Contains(e.JoinedOn.Month)
                            || (e.DateOfBirth != null && months.Contains(e.DateOfBirth.Value.Month))))
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName, e.Phone, e.DateOfBirth, e.JoinedOn })
            .ToListAsync(ct);

        var found = new List<HrAlert>();
        foreach (var p in people)
        {
            if (p.DateOfBirth is { } dob && NextOccurrence(dob, today) is { } birthday
                && birthday.DayNumber - today.DayNumber <= birthdayDays)
            {
                found.Add(new HrAlert(
                    HrNotificationKind.Birthday, p.Id, p.EmployeeCode, p.FullName, p.Phone,
                    birthday, birthday.DayNumber - today.DayNumber, "Birthday",
                    $"Turns {birthday.Year - dob.Year}"));
            }

            if (NextOccurrence(p.JoinedOn, today) is { } anniversary
                && anniversary.DayNumber - today.DayNumber <= anniversaryDays)
            {
                var years = anniversary.Year - p.JoinedOn.Year;
                // A joining date is not an anniversary; the first one is a year later.
                if (years >= 1)
                {
                    found.Add(new HrAlert(
                        HrNotificationKind.WorkAnniversary, p.Id, p.EmployeeCode, p.FullName, p.Phone,
                        anniversary, anniversary.DayNumber - today.DayNumber, "Work anniversary",
                        $"{years} year{(years == 1 ? "" : "s")} of service"));
                }
            }
        }

        return found;
    }

    /// <summary>The calendar months a horizon starting today can reach.</summary>
    private static int[] MonthsTouched(DateOnly today, int days)
    {
        var months = new HashSet<int> { today.Month };
        for (var d = 0; d <= Math.Min(days, 366); d += 28) months.Add(today.AddDays(d).Month);
        months.Add(today.AddDays(Math.Min(days, 366)).Month);
        return [.. months];
    }

    /// <summary>
    /// The next time this day-and-month comes round, today included. 29 February lands on 1 March in
    /// a common year — the same convention every payroll system in the world uses, and stated here
    /// because the alternative is silently skipping a person's birthday three years in four.
    /// </summary>
    public static DateOnly? NextOccurrence(DateOnly original, DateOnly today)
    {
        var candidate = Shift(original, today.Year);
        return candidate >= today ? candidate : Shift(original, today.Year + 1);
    }

    private static DateOnly Shift(DateOnly original, int year)
    {
        var day = Math.Min(original.Day, DateTime.DaysInMonth(year, original.Month));
        var shifted = new DateOnly(year, original.Month, day);
        return original is { Month: 2, Day: 29 } && day == 28 ? shifted.AddDays(1) : shifted;
    }
}
