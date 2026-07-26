using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record ApprovalItem(
    long Id, string Type, string Reason, long? Amount, string RequestedBy,
    DateTimeOffset RequestedAt, string Subject);

public sealed record DecidedItem(
    long Id, string Type, string State, long? Amount, string DecidedBy,
    DateTimeOffset? DecidedAt, string? Note);

/// <summary>
/// 05 §5 screen 8 — one inbox for every ⚿ flow (C7). The decision is a state-guarded UPDATE, so
/// two supervisors clicking at once cannot both decide; the loser is told plainly.
/// </summary>
[Authorize(Policy = Perm.AdminApprovalsDecide)]
public class ApprovalsModel(HmsTx tx, ApprovalEngine approvals) : HmsPageModel
{
    public IReadOnlyList<ApprovalItem> Pending { get; private set; } = [];
    public IReadOnlyList<DecidedItem> Recent { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var rows = await s.Kernel.ApprovalRequests.AsNoTracking()
                .OrderByDescending(a => a.Id).Take(60).ToListAsync();

            var userIds = rows.Select(a => a.RequestedBy)
                .Concat(rows.Where(a => a.DecidedBy is not null).Select(a => a.DecidedBy!.Value))
                .Distinct().ToList();
            var users = await s.Auth.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            // Requests point at their subject row; for a discount that is the patient.
            var patientIds = rows.Where(a => a.SourceTable == "reg.patient").Select(a => a.SourceId).ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName + " · " + p.Uhid);

            Pending = rows.Where(a => a.State == ApprovalState.Pending)
                .Select(a => new ApprovalItem(a.Id, a.Type, a.Reason, a.Amount,
                    users.GetValueOrDefault(a.RequestedBy, "—"), a.RequestedAt,
                    patients.GetValueOrDefault(a.SourceId, $"{a.SourceTable} #{a.SourceId}")))
                .ToList();

            Recent = rows.Where(a => a.State != ApprovalState.Pending).Take(15)
                .Select(a => new DecidedItem(a.Id, a.Type, a.State, a.Amount,
                    a.DecidedBy is { } d ? users.GetValueOrDefault(d, "—") : "—",
                    a.DecidedAt, a.DecisionNote))
                .ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostDecideAsync(long id, bool approve, string? note)
    {
        try
        {
            await tx.RunAsync(s => approvals.DecideAsync(
                s.Kernel, id, approve, ActorId, ActorName, note));
            Toast(approve ? "Approved — the counter can now complete the bill"
                          : "Rejected — returned to the counter",
                  approve ? "task_alt" : "block");
        }
        catch (ApprovalException e)
        {
            Toast(e.Message, "error");
        }
        return Redirect("/admin/approvals");
    }
}
