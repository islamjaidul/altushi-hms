using Hms.Admin.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Admin;

public sealed record ResolvedRate(long Price, long RateVersionId);

public sealed class RateResolutionException(string message) : Exception(message);

/// <summary>
/// 03 §5: most specific scope wins (package > corporate > standard) where
/// valid_from ≤ business_day < valid_to. The resolved version id is stored on the charge line —
/// history reproduces itself forever (C6, edge 13).
/// </summary>
public sealed class RateResolver
{
    public async Task<ResolvedRate> ResolveAsync(
        AdmDbContext adm, string catalogKind, long catalogId, DateOnly businessDay,
        string? corporateScope = null, string? packageScope = null, CancellationToken ct = default)
    {
        var scopes = new List<string> { "standard" };
        if (corporateScope is not null) scopes.Add(corporateScope);
        if (packageScope is not null) scopes.Add(packageScope);

        var candidates = await adm.RateVersions
            .Where(r => r.CatalogKind == catalogKind && r.CatalogId == catalogId
                        && scopes.Contains(r.Scope)
                        && r.ValidFrom <= businessDay
                        && (r.ValidTo == null || businessDay < r.ValidTo))
            .ToListAsync(ct);

        var winner = candidates
            .OrderByDescending(r => r.Scope.StartsWith("package:") ? 2 : r.Scope.StartsWith("corporate:") ? 1 : 0)
            .FirstOrDefault()
            ?? throw new RateResolutionException(
                $"No rate effective on {businessDay:yyyy-MM-dd} for {catalogKind}/{catalogId} — " +
                "check the rate plan (provisional items need prices before go-live, edge 11).");

        return new ResolvedRate(winner.Price, winner.Id);
    }

    /// <summary>
    /// Prices a run of business days for one catalog item, <b>naming</b> the days it could not
    /// price instead of dropping them.
    ///
    /// Spec 0032 M2-D1. A catch around a single <see cref="ResolveAsync"/> means one thing when
    /// it filters a *picker* — an unpriced item is simply not sellable (edge 11) — and something
    /// else entirely when it sits inside a <b>sum</b>. There, a swallowed exception makes the day
    /// contribute zero and the total silently understates itself, which on the help desk means a
    /// family is quoted a number that is too low with nothing on screen saying so. A total that
    /// cannot be complete must be able to say which days are missing, so the caller can present a
    /// lower bound honestly rather than a wrong figure confidently.
    /// </summary>
    public async Task<DayTotal> TotalOverDaysAsync(
        AdmDbContext adm, string catalogKind, long catalogId, IEnumerable<DateOnly> days,
        string? corporateScope = null, string? packageScope = null, CancellationToken ct = default)
    {
        long total = 0;
        var unpriced = new List<DateOnly>();
        foreach (var day in days)
        {
            try
            {
                total += (await ResolveAsync(
                    adm, catalogKind, catalogId, day, corporateScope, packageScope, ct)).Price;
            }
            catch (RateResolutionException)
            {
                unpriced.Add(day);
            }
        }
        return new DayTotal(total, unpriced);
    }
}

/// <summary>
/// The sum of a set of business days, plus the days that had no effective rate. When
/// <see cref="Unpriced"/> is non-empty, <see cref="Total"/> is a <b>lower bound</b> and every
/// caller that shows it to a person must say so.
/// </summary>
public sealed record DayTotal(long Total, IReadOnlyList<DateOnly> Unpriced)
{
    public bool IsComplete => Unpriced.Count == 0;
}
