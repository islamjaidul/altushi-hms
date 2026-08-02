using Hms.Diagnostics.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Diagnostics;

/// <summary>
/// Spec 0039 WP6 (AUD-PHI-01): the ONE way the anonymous report-status screen resolves what a
/// visitor typed. The key is <see cref="TestOrder.PublicToken"/> and nothing else — the audit
/// walked the old order-number lookup straight through the primary key and read back 9 of 9
/// live orders. An id-shaped input ("LB-00042"), a malformed input and an unknown token all
/// return the same null, so there is no oracle to probe (§8 N5).
/// </summary>
public static class PublicReportLookup
{
    public static Task<TestOrder?> FindAsync(
        DiagDbContext diag, string? code, CancellationToken ct = default)
    {
        var token = PublicToken.Normalise(code);
        if (token is null) return Task.FromResult<TestOrder?>(null);

        // IgnoreQueryFilters, out loud (WP5): the kiosk visitor has no signed-in branch, and the
        // token — 16 random bytes unique across the table — is the entire authority here.
        return diag.Orders.AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                o => o.PublicToken == token && o.State != TestOrderState.Cancelled, ct);
    }
}
