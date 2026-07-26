using System.Text.RegularExpressions;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Kernel.Numbering;

/// <summary>
/// ADR-0004: money-document numbers come from per-scope counter rows incremented under row lock
/// inside the issuing transaction. The row lock is held to commit; a rollback rolls the counter
/// back too — gap-free by construction. Callers MUST be inside the transaction that also writes
/// the document (G19: one business action, one transaction).
/// </summary>
public sealed partial class NumberSeriesService
{
    [GeneratedRegex(@"\{n(?::D(?<pad>\d+))?\}")]
    private static partial Regex NumberToken();

    /// <summary>Returns the next value for the scope, creating the series row on first use.</summary>
    public async Task<long> IssueValueAsync(
        KernelDbContext db, long branchId, string docType, string fiscalYear,
        string displayFormat, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Number issuance requires the caller's ambient transaction (ADR-0004 / G19).");

        // Idempotent seed; unique index (branch_id, doc_type, fiscal_year) backs ON CONFLICT.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO kernel.number_series (branch_id, doc_type, fiscal_year, next_value, display_format)
            VALUES ({branchId}, {docType}, {fiscalYear}, 1, {displayFormat})
            ON CONFLICT (branch_id, doc_type, fiscal_year) DO NOTHING
            """, ct);

        // Row-locking increment; the lock rides the ambient transaction until commit/rollback.
        var issued = await db.Database.SqlQuery<long>($"""
            UPDATE kernel.number_series SET next_value = next_value + 1
            WHERE branch_id = {branchId} AND doc_type = {docType} AND fiscal_year = {fiscalYear}
            RETURNING next_value - 1 AS "Value"
            """).ToListAsync(ct);

        return issued.Single();
    }

    /// <summary>Issues and formats per the series display format, e.g. "INV-{fy}-{n:D6}" → INV-2026-27-000042.</summary>
    public async Task<(long Value, string Display)> IssueAsync(
        KernelDbContext db, long branchId, string docType, string fiscalYear,
        string displayFormat, CancellationToken ct = default)
    {
        var value = await IssueValueAsync(db, branchId, docType, fiscalYear, displayFormat, ct);
        return (value, Format(displayFormat, fiscalYear, value));
    }

    public static string Format(string displayFormat, string fiscalYear, long value)
    {
        var s = displayFormat.Replace("{fy}", fiscalYear);
        return NumberToken().Replace(s, m =>
            m.Groups["pad"].Success ? value.ToString("D" + m.Groups["pad"].Value) : value.ToString());
    }
}
