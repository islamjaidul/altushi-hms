using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// The file HR keeps on one person: family and nominees, and typed documents with their expiry
/// (module PRD §7.1 — G14, G16, G21).
/// <para>
/// A page of its own rather than two more tables on the record. The record answers "who is this and
/// what do they cost"; this answers "is their paperwork in order", which is a different job done by
/// a different person on a different day.
/// </para>
/// <para>
/// Reading needs <c>hr.read</c>; writing needs <c>hr.employee.manage</c>. Nothing here is ever
/// deleted — a nomination is superseded and a document is renewed, so the previous instruction stays
/// readable (hard rule 4's shape).
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.Read)]
public class EmployeeFileModel(IHrTx tx, EmployeeFileService file) : HmsPageModel
{
    public Employee? Person { get; private set; }
    public IReadOnlyList<EmployeeDependant> Dependants { get; private set; } = [];
    public IReadOnlyList<EmployeeDependant> SupersededDependants { get; private set; } = [];
    public IReadOnlyList<EmployeeDocument> Documents { get; private set; } = [];
    public IReadOnlyList<EmployeeDocument> SupersededDocuments { get; private set; } = [];
    public IReadOnlyDictionary<string, int> Shares { get; private set; } =
        new Dictionary<string, int>();
    public IReadOnlyList<string> ShareComplaints { get; private set; } = [];
    public DateOnly Today { get; private set; }
    public int ExpiryLeadDays { get; private set; } = 45;

    public bool CanManage => Can("hr.employee.manage");

    // ---- dependant form
    [BindProperty] public string? DependantName { get; set; }
    [BindProperty] public string? Relation { get; set; }
    [BindProperty] public string? DependantDob { get; set; }
    [BindProperty] public string? DependantPhone { get; set; }
    [BindProperty] public string? DependantNid { get; set; }
    [BindProperty] public bool IsNominee { get; set; }
    [BindProperty] public string? Purpose { get; set; }
    [BindProperty] public int SharePercent { get; set; }

    // ---- document form
    [BindProperty] public string? DocumentKindValue { get; set; }
    [BindProperty] public string? DocumentTitle { get; set; }
    [BindProperty] public string? DocumentNumber { get; set; }
    [BindProperty] public string? IssuingBody { get; set; }
    [BindProperty] public string? IssuedOn { get; set; }
    [BindProperty] public string? ExpiresOn { get; set; }
    [BindProperty] public string? DocumentNote { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        await LoadAsync(id);
        return Person is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAddDependantAsync(long id)
    {
        if (!CanManage) return Forbid();

        var dob = FlexibleDate.TryParse(DependantDob ?? "", out var d) && d != default
            ? (DateOnly?)d : null;

        // The form takes whole percent, because "share basis points" is not a thing an HR officer
        // says. The domain keeps basis points, so the conversion happens once, here.
        if (IsNominee && SharePercent is < 1 or > 100)
            return await BackWith(id, "A nominee's share is a whole percentage from 1 to 100.");

        try
        {
            await tx.RunAsync(s => file.AddDependantAsync(
                s.Hr, s.Kernel, BranchId, id, new EmployeeDependant
                {
                    Name = DependantName ?? "",
                    Relation = Relation ?? "",
                    DateOfBirth = dob,
                    Phone = DependantPhone,
                    NationalId = DependantNid,
                    IsNominee = IsNominee,
                    Purpose = IsNominee ? Purpose : null,
                    ShareBp = IsNominee ? SharePercent * 100 : 0,
                    CreatedByName = ActorName,
                }, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast($"{DependantName} added to the file", "family_restroom");
        return Redirect($"/hr/employees/{id}/file");
    }

    public async Task<IActionResult> OnPostRetireDependantAsync(long id, long dependantId)
    {
        if (!CanManage) return Forbid();

        try
        {
            await tx.RunAsync(s => file.RetireDependantAsync(
                s.Hr, s.Kernel, BranchId, dependantId, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Removed from the current file — the entry is kept in the record", "history");
        return Redirect($"/hr/employees/{id}/file");
    }

    public async Task<IActionResult> OnPostAddDocumentAsync(long id)
    {
        if (!CanManage) return Forbid();

        var issued = FlexibleDate.TryParse(IssuedOn ?? "", out var i) && i != default
            ? (DateOnly?)i : null;
        var expires = FlexibleDate.TryParse(ExpiresOn ?? "", out var x) && x != default
            ? (DateOnly?)x : null;

        try
        {
            await tx.RunAsync(s => file.AddDocumentAsync(
                s.Hr, s.Kernel, BranchId, id, new EmployeeDocument
                {
                    Kind = DocumentKindValue ?? "",
                    Title = DocumentTitle ?? "",
                    Number = DocumentNumber,
                    IssuingBody = IssuingBody,
                    IssuedOn = issued,
                    ExpiresOn = expires,
                    Note = DocumentNote,
                    CreatedByName = ActorName,
                }, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast($"{DocumentTitle} added to the file", "description");
        return Redirect($"/hr/employees/{id}/file");
    }

    public async Task<IActionResult> OnPostRenewDocumentAsync(long id, long documentId)
    {
        if (!CanManage) return Forbid();

        var issued = FlexibleDate.TryParse(IssuedOn ?? "", out var i) && i != default
            ? (DateOnly?)i : null;
        var expires = FlexibleDate.TryParse(ExpiresOn ?? "", out var x) && x != default
            ? (DateOnly?)x : null;

        if (expires is null)
            return await BackWith(id, "A renewal needs the new expiry date — that is the point of it.");

        try
        {
            await tx.RunAsync(s => file.RenewDocumentAsync(
                s.Hr, s.Kernel, BranchId, documentId, new EmployeeDocument
                {
                    Kind = "",                                  // taken from the document being renewed
                    Title = DocumentTitle ?? "",
                    Number = DocumentNumber,
                    IssuingBody = IssuingBody,
                    IssuedOn = issued,
                    ExpiresOn = expires,
                    Note = DocumentNote,
                    CreatedByName = ActorName,
                }, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast($"Renewed to {expires:dd MMM yyyy} — the previous one is kept", "autorenew");
        return Redirect($"/hr/employees/{id}/file");
    }

    public async Task<IActionResult> OnPostRetireDocumentAsync(long id, long documentId)
    {
        if (!CanManage) return Forbid();

        try
        {
            await tx.RunAsync(s => file.RetireDocumentAsync(
                s.Hr, s.Kernel, BranchId, documentId, ActorId, ActorName));
        }
        catch (HrException e)
        {
            return await BackWith(id, e.Message);
        }

        Toast("Removed from the current file — the entry is kept in the record", "history");
        return Redirect($"/hr/employees/{id}/file");
    }

    private async Task<IActionResult> BackWith(long id, string message)
    {
        await LoadAsync(id);
        Fail(message);
        return Page();
    }

    private async Task LoadAsync(long id)
    {
        Today = Dhaka.Today(TimeProvider.System);

        await tx.RunAsync(async s =>
        {
            Person = await s.Hr.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.BranchId == BranchId);
            if (Person is null) return;

            var settings = await HrAlertService.SettingsAsync(s.Hr, BranchId);
            ExpiryLeadDays = Math.Max(
                settings[HrNotificationKind.DocumentExpiring].DaysBefore,
                settings[HrNotificationKind.LicenceExpiring].DaysBefore);

            Dependants = await EmployeeFileService.DependantsAsync(s.Hr, id);
            SupersededDependants = await s.Hr.Dependants.AsNoTracking()
                .Where(d => d.EmployeeId == id && d.SupersededAt != null)
                .OrderByDescending(d => d.SupersededAt)
                .ToListAsync();

            Documents = await EmployeeFileService.DocumentsAsync(s.Hr, id);
            SupersededDocuments = await s.Hr.Documents.AsNoTracking()
                .Where(d => d.EmployeeId == id && d.SupersededAt != null)
                .OrderByDescending(d => d.SupersededAt)
                .ToListAsync();

            Shares = await EmployeeFileService.NomineeSharesAsync(s.Hr, id);
            ShareComplaints =
            [
                .. Shares
                    .Select(kv => EmployeeFileService.ShareComplaint(kv.Key, kv.Value))
                    .Where(c => c is not null)
                    .Select(c => c!),
            ];
        });
    }

    /// <summary>How one document's expiry reads — the same words the register and the alert use.</summary>
    public (string Tone, string Words) Expiry(EmployeeDocument d) =>
        EmployeeFileService.ExpiryState(d.ExpiresOn, Today, ExpiryLeadDays);
}
