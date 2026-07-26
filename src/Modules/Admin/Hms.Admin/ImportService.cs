using System.Security.Cryptography;
using System.Text;
using Hms.Admin.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Admin;

public sealed record ImportRowError(int Row, string Field, string Message);

public sealed record ImportResult(long? BatchId, int Inserted, int Updated, IReadOnlyList<ImportRowError> Errors)
{
    public bool Committed => BatchId is not null;
}

/// <summary>
/// ADR-0010: staged import — validate everything, commit nothing on any structural doubt is
/// wrong-headed for construction data; instead: valid rows commit as ONE audited batch,
/// bad rows come back per-row for the round-trip file (edge 12). Spreadsheets are hostile
/// input (G13): sizes capped by caller, values parsed defensively, nothing echoed raw.
/// </summary>
public sealed class CatalogImportService(AuditWriter audit, TimeProvider clock)
{
    public const int MaxRows = 10_000;

    /// <summary>
    /// CSV columns: code,name,dept,sample_types(;-separated),tat_minutes,price,valid_from.
    /// Upsert key: code. Price becomes a rate version starting valid_from (open-ended),
    /// closing any open standard version the day before — effective-dating preserved (edge 13).
    /// </summary>
    public async Task<ImportResult> ImportTestCatalogAsync(
        AdmDbContext adm, KernelDbContext kernel, string csvContent, string sourceFileName,
        long branchId, long actorId, string actorName, CancellationToken ct = default)
    {
        var errors = new List<ImportRowError>();
        var rows = new List<(int n, string code, string name, string dept, string[] types, int tat, long price, DateOnly from)>();

        var lines = csvContent.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > MaxRows) throw new InvalidOperationException($"Import exceeds {MaxRows} rows (G13).");

        for (var i = 1; i < lines.Length; i++)          // row 0 is the header
        {
            var n = i + 1;
            var f = lines[i].Split(',');
            if (f.Length < 7) { errors.Add(new(n, "*", "expected 7 columns")); continue; }
            var code = f[0].Trim(); var name = f[1].Trim(); var dept = f[2].Trim();
            if (code.Length == 0) { errors.Add(new(n, "code", "required")); continue; }
            if (name.Length == 0) { errors.Add(new(n, "name", "required")); continue; }
            if (!int.TryParse(f[4], out var tat) || tat < 0)
            { errors.Add(new(n, "tat_minutes", $"not a non-negative integer: '{f[4].Trim()}'")); continue; }
            if (!long.TryParse(f[5], out var price) || price < 0)
            { errors.Add(new(n, "price", $"not whole taka: '{f[5].Trim()}'")); continue; }
            if (!DateOnly.TryParse(f[6].Trim(), out var from))
            { errors.Add(new(n, "valid_from", $"not a date: '{f[6].Trim()}'")); continue; }
            rows.Add((n, code, name, dept, f[3].Split(';', StringSplitOptions.RemoveEmptyEntries), tat, price, from));
        }

        if (rows.Count == 0) return new ImportResult(null, 0, 0, errors);

        var batch = new ImportBatch
        {
            Kind = "test_catalog",
            SourceFile = sourceFileName,
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csvContent))),
            Mapping = "{}",
            CommittedBy = actorId,
            CommittedAt = clock.GetUtcNow(),
        };
        kernel.ImportBatches.Add(batch);
        await kernel.SaveChangesAsync(ct);

        int inserted = 0, updated = 0;
        foreach (var r in rows)
        {
            var existing = await adm.TestCatalog.SingleOrDefaultAsync(t => t.Code == r.code, ct);
            long catalogId;
            if (existing is null)
            {
                var item = new TestCatalogItem
                {
                    Code = r.code, Name = r.name, Dept = r.dept,
                    SampleTypes = r.types, TatMinutes = r.tat, Provisional = false,
                };
                adm.TestCatalog.Add(item);
                await adm.SaveChangesAsync(ct);
                catalogId = item.Id;
                inserted++;
            }
            else
            {
                existing.Name = r.name; existing.Dept = r.dept;
                existing.SampleTypes = r.types; existing.TatMinutes = r.tat;
                catalogId = existing.Id;
                updated++;
            }

            // effective-dated price: close any open standard version, insert the new one
            var open = await adm.RateVersions.SingleOrDefaultAsync(v =>
                v.CatalogKind == "test" && v.CatalogId == catalogId &&
                v.Scope == "standard" && v.BranchId == branchId && v.ValidTo == null, ct);
            if (open is not null)
            {
                if (open.ValidFrom >= r.from)
                    { errors.Add(new(r.n, "valid_from", "must be after the current version's start")); continue; }
                if (open.Price == r.price) continue;     // idempotent re-import: nothing to change
                open.ValidTo = r.from;
            }
            adm.RateVersions.Add(new RateVersion
            {
                BranchId = branchId, CatalogKind = "test", CatalogId = catalogId,
                Price = r.price, ValidFrom = r.from, AuthorId = actorId, ApprovalId = null,
            });
        }
        await adm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "import.commit", "kernel.import_batch",
            batch.Id, after: new { batch.Kind, inserted, updated, errorCount = errors.Count }, tier: 2);
        await kernel.SaveChangesAsync(ct);

        return new ImportResult(batch.Id, inserted, updated, errors);
    }
}
