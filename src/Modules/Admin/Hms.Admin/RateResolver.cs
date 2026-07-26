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
}
