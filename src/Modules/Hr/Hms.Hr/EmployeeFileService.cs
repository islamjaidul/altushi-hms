using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// The file HR keeps on a person: family, nominees, and typed documents with expiry
/// (module PRD §7.1, G14 / G16 / G21).
/// <para>
/// Two rules run through everything here. <b>Nothing is edited in place and nothing is deleted</b> —
/// a nomination is a legal instruction and a licence number is evidence, so a change supersedes and
/// the previous row stays readable (hard rule 4's shape, applied to people rather than money). And
/// <b>an expired document never blocks anything</b>: §7.1 is explicit that professional registration
/// "never blocks; always warns", so no method here refuses work on the grounds that a date has
/// passed.
/// </para>
/// </summary>
public sealed class EmployeeFileService(AuditWriter audit, TimeProvider clock)
{
    /// <summary>Basis points of a whole share. Nominee shares for one purpose must total this.</summary>
    public const int WholeShareBp = 10000;

    // ---- dependants and nominees --------------------------------------------------------------

    /// <summary>Everyone on the file who has not been superseded.</summary>
    public static Task<List<EmployeeDependant>> DependantsAsync(
        HrDbContext hr, long employeeId, CancellationToken ct = default)
        => hr.Dependants.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId && d.SupersededAt == null)
            .OrderByDescending(d => d.IsNominee).ThenBy(d => d.Name)
            .ToListAsync(ct);

    /// <summary>
    /// Adds a family member, and possibly a nomination. The share check runs over the whole live set
    /// for the purpose rather than the row being added, because a nomination is only meaningful as
    /// part of a complete instruction: 60% to a spouse means nothing until the other 40% is named.
    /// </summary>
    public async Task<EmployeeDependant> AddDependantAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        EmployeeDependant draft, long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new HrException("The family member's name is required.");
        if (!DependantRelation.All.Contains(draft.Relation))
            throw new HrException("Choose the relationship.");

        if (draft.IsNominee)
        {
            if (draft.Purpose is null || !NomineePurpose.All.Contains(draft.Purpose))
                throw new HrException("A nominee must be nominated for provident fund, gratuity or both.");
            if (draft.ShareBp is <= 0 or > WholeShareBp)
                throw new HrException("A nominee's share is between 0.01% and 100%.");
        }
        else
        {
            draft.Purpose = null;
            draft.ShareBp = 0;
        }

        draft.BranchId = branchId;
        draft.EmployeeId = employeeId;
        draft.CreatedAt = clock.GetUtcNow();
        draft.CreatedBy = actorId;
        draft.CreatedByName = actorName;
        hr.Dependants.Add(draft);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.dependant.add", "hr.employee_dependant",
            draft.Id, after: new { employeeId, draft.Name, draft.Relation, draft.IsNominee, draft.Purpose, draft.ShareBp });

        return draft;
    }

    /// <summary>
    /// Replaces a row with a corrected one. The old row keeps its id and its history; the new one
    /// points back at it. "Who was the nominee in 2024" therefore stays answerable, which is the
    /// entire reason a nomination is not a mutable field.
    /// </summary>
    public async Task<EmployeeDependant> SupersedeDependantAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long dependantId,
        EmployeeDependant replacement, long actorId, string actorName, CancellationToken ct = default)
    {
        var previous = await hr.Dependants.FirstOrDefaultAsync(d => d.Id == dependantId, ct)
                       ?? throw new HrException("That family member is no longer on the file.");
        if (previous.SupersededAt is not null)
            throw new HrException($"{previous.Name} was already replaced — amend the current entry instead.");

        replacement.SupersedesId = previous.Id;
        var added = await AddDependantAsync(
            hr, kernel, branchId, previous.EmployeeId, replacement, actorId, actorName, ct);

        previous.SupersededAt = clock.GetUtcNow();
        previous.SupersededBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.dependant.supersede",
            "hr.employee_dependant", previous.Id,
            before: new { previous.Name, previous.ShareBp, previous.Purpose },
            after: new { replacedBy = added.Id, added.Name, added.ShareBp, added.Purpose });

        return added;
    }

    /// <summary>Withdraws a nomination or removes a family member — by superseding with nothing.</summary>
    public async Task RetireDependantAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long dependantId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var row = await hr.Dependants.FirstOrDefaultAsync(d => d.Id == dependantId, ct)
                  ?? throw new HrException("That family member is no longer on the file.");
        if (row.SupersededAt is not null) return;

        row.SupersededAt = clock.GetUtcNow();
        row.SupersededBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.dependant.retire",
            "hr.employee_dependant", row.Id, before: new { row.Name, row.IsNominee, row.ShareBp });
    }

    /// <summary>
    /// What each purpose's live nominations add up to. A purpose that totals anything other than
    /// 100% is an instruction the employer cannot execute, and every surface says so rather than
    /// waiting for the day it matters.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, int>> NomineeSharesAsync(
        HrDbContext hr, long employeeId, CancellationToken ct = default)
    {
        var live = await hr.Dependants.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId && d.IsNominee && d.SupersededAt == null)
            .Select(d => new { d.Purpose, d.ShareBp })
            .ToListAsync(ct);

        var totals = new Dictionary<string, int>
        {
            [NomineePurpose.ProvidentFund] = 0,
            [NomineePurpose.Gratuity] = 0,
        };

        foreach (var n in live)
        {
            // "Both" counts against each purpose: one instruction covering two benefits, not half
            // an instruction covering each.
            if (n.Purpose is NomineePurpose.ProvidentFund or NomineePurpose.Both)
                totals[NomineePurpose.ProvidentFund] += n.ShareBp;
            if (n.Purpose is NomineePurpose.Gratuity or NomineePurpose.Both)
                totals[NomineePurpose.Gratuity] += n.ShareBp;
        }

        return totals;
    }

    /// <summary>The sentence a screen shows when a purpose does not add up; null when it does.</summary>
    public static string? ShareComplaint(string purpose, int totalBp)
    {
        var name = purpose == NomineePurpose.ProvidentFund ? "Provident fund" : "Gratuity";
        if (totalBp == 0) return $"{name}: nobody is nominated.";
        if (totalBp == WholeShareBp) return null;
        var pct = totalBp / 100m;
        return totalBp < WholeShareBp
            ? $"{name}: nominations total {pct:0.##}% — {(WholeShareBp - totalBp) / 100m:0.##}% is unallocated."
            : $"{name}: nominations total {pct:0.##}%, which is {(totalBp - WholeShareBp) / 100m:0.##}% too much.";
    }

    // ---- documents ------------------------------------------------------------------------------

    public static Task<List<EmployeeDocument>> DocumentsAsync(
        HrDbContext hr, long employeeId, CancellationToken ct = default)
        => hr.Documents.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId && d.SupersededAt == null)
            .OrderBy(d => d.ExpiresOn == null).ThenBy(d => d.ExpiresOn).ThenBy(d => d.Kind)
            .ToListAsync(ct);

    public async Task<EmployeeDocument> AddDocumentAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        EmployeeDocument draft, long actorId, string actorName, CancellationToken ct = default)
    {
        if (!DocumentKind.All.Contains(draft.Kind))
            throw new HrException("Choose what kind of document this is.");
        if (string.IsNullOrWhiteSpace(draft.Title))
            throw new HrException("Give the document a name — it is what the register lists.");
        if (draft.Kind == DocumentKind.Licence && string.IsNullOrWhiteSpace(draft.IssuingBody))
            throw new HrException("A professional licence needs the council or board that issued it.");
        if (draft.IssuedOn is { } from && draft.ExpiresOn is { } to && to < from)
            throw new HrException("A document cannot expire before it was issued.");

        draft.BranchId = branchId;
        draft.EmployeeId = employeeId;
        draft.CreatedAt = clock.GetUtcNow();
        draft.CreatedBy = actorId;
        draft.CreatedByName = actorName;
        hr.Documents.Add(draft);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.document.add", "hr.employee_document",
            draft.Id, after: new { employeeId, draft.Kind, draft.Title, draft.Number, draft.ExpiresOn });

        return draft;
    }

    /// <summary>
    /// Records a renewal. The expired row is kept and pointed at, so "when did this registration
    /// last lapse, and for how long" is answerable — the question a hospital gets asked in an
    /// inspection, and the reason a renewal is not an edit.
    /// </summary>
    public async Task<EmployeeDocument> RenewDocumentAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long documentId,
        EmployeeDocument renewal, long actorId, string actorName, CancellationToken ct = default)
    {
        var previous = await hr.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                       ?? throw new HrException("That document is no longer on the file.");
        if (previous.SupersededAt is not null)
            throw new HrException($"{previous.Title} has already been renewed — renew the current one.");

        renewal.Kind = previous.Kind;
        renewal.SupersedesId = previous.Id;
        var added = await AddDocumentAsync(
            hr, kernel, branchId, previous.EmployeeId, renewal, actorId, actorName, ct);

        previous.SupersededAt = clock.GetUtcNow();
        previous.SupersededBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.document.renew", "hr.employee_document",
            previous.Id, before: new { previous.Number, previous.ExpiresOn },
            after: new { renewedAs = added.Id, added.Number, added.ExpiresOn });

        return added;
    }

    public async Task RetireDocumentAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long documentId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var row = await hr.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                  ?? throw new HrException("That document is no longer on the file.");
        if (row.SupersededAt is not null) return;

        row.SupersededAt = clock.GetUtcNow();
        row.SupersededBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.document.retire",
            "hr.employee_document", row.Id, before: new { row.Kind, row.Title, row.ExpiresOn });
    }

    /// <summary>
    /// How a document's expiry reads today. Pure, so every surface — record, register, command
    /// centre, notification — says the same words about the same date.
    /// </summary>
    public static (string Tone, string Words) ExpiryState(DateOnly? expiresOn, DateOnly today, int leadDays)
    {
        if (expiresOn is not { } on) return ("muted", "No expiry");

        var days = on.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => ("bad", $"Expired {-days} day{(days == -1 ? "" : "s")} ago"),
            0 => ("bad", "Expires today"),
            _ when days <= leadDays => ("warn", $"Expires in {days} day{(days == 1 ? "" : "s")}"),
            _ => ("good", $"Valid to {on:dd MMM yyyy}"),
        };
    }
}
