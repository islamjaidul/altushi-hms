using System.Text;
using Hms.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record BatchRow(long Id, string Kind, string SourceFile, DateTimeOffset At, string By);

/// <summary>
/// §9A.1 F1 — "the single strongest lock-in mechanism": the hospital loads its real price list
/// during construction, months before opening. ADR-0010's posture is the point — valid rows
/// commit as one audited batch and bad rows come back per row, so a 900-line spreadsheet with
/// four typos loads 896 tests instead of rejecting the file (edge 12).
/// </summary>
[Authorize(Policy = Perm.AdminMastersManage)]
public class ImportModel(HmsTx tx, CatalogImportService importer) : HmsPageModel
{
    /// <summary>Upload cap: the service refuses >10k rows, we refuse an absurd file first (G13).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    [BindProperty] public string? Csv { get; set; }
    [BindProperty] public IFormFile? Upload { get; set; }

    public ImportResult? Result { get; private set; }
    public string? SourceName { get; private set; }
    public IReadOnlyList<BatchRow> Recent { get; private set; } = [];

    public const string TemplateHeader = "code,name,dept,sample_types,tat_minutes,price,valid_from";
    public const string TemplateSample =
        "CBC,Complete Blood Count,Hematology,EDTA,240,400,2026-01-01\n" +
        "LIPID,Lipid Profile,Biochemistry,Serum,1440,1200,2026-01-01\n" +
        "XRAY-CH,X-ray Chest P/A,Imaging,,120,500,2026-01-01";

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Recent = await tx.RunAsync(async s =>
        {
            var batches = await s.Kernel.ImportBatches.AsNoTracking()
                .OrderByDescending(b => b.Id).Take(10).ToListAsync();
            var userIds = batches.Select(b => b.CommittedBy).Distinct().ToList();
            var users = await s.Auth.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
            return (IReadOnlyList<BatchRow>)batches.Select(b => new BatchRow(
                b.Id, b.Kind, b.SourceFile, b.CommittedAt,
                users.GetValueOrDefault(b.CommittedBy, "—"))).ToList();
        });
    }

    /// <summary>Downloadable starter file — the leave-behind the hospital fills in (§9A.4 #4).</summary>
    public IActionResult OnGetTemplate() =>
        CsvFile($"{TemplateHeader}\n{TemplateSample}\n", "test-catalog-template.csv");

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();

        var content = Csv;
        SourceName = "pasted.csv";

        if (Upload is { Length: > 0 })
        {
            if (Upload.Length > MaxBytes)
            {
                Fail($"That file is larger than {MaxBytes / 1024 / 1024} MB. Split it and import in parts.");
                return Page();
            }
            using var reader = new StreamReader(Upload.OpenReadStream(), Encoding.UTF8);
            content = await reader.ReadToEndAsync();
            SourceName = Path.GetFileName(Upload.FileName);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            Fail("Choose a CSV file or paste the rows before importing.");
            return Page();
        }
        if (!content.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            Fail("That does not look like a catalog file — the first line must be the header row: " +
                 TemplateHeader);
            return Page();
        }

        try
        {
            Result = await tx.RunAsync(s => importer.ImportTestCatalogAsync(
                s.Adm, s.Kernel, content!, SourceName!, BranchId, ActorId, ActorName));
        }
        catch (InvalidOperationException e)
        {
            Fail(e.Message);
            return Page();
        }

        await LoadAsync();

        if (Result.Committed)
            Toast($"Imported — {Result.Inserted} added, {Result.Updated} updated" +
                  (Result.Errors.Count > 0 ? $", {Result.Errors.Count} row(s) need attention" : ""),
                  "inventory_2");
        else
            Fail("No row could be read. Fix the rows listed below and import again.");

        return Page();
    }

    /// <summary>The round-trip file (edge 12): a starter the hospital fills in.</summary>
    private static FileContentResult CsvFile(string content, string name) =>
        new(Encoding.UTF8.GetBytes(content), "text/csv") { FileDownloadName = name };
}
