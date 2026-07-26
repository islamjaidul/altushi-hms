using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record AuditRow(
    long Id, DateTimeOffset At, string Actor, string Action, string Entity, long EntityId, string? After);

/// <summary>
/// ADR-0011: the audit trail is append-only in the database itself — the app role holds no
/// UPDATE or DELETE grant on this table, so this screen is a reader by construction, not by policy.
/// </summary>
[Authorize(Policy = Perm.AdminAuditRead)]
public class AuditModel(HmsTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public IReadOnlyList<AuditRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Actions { get; private set; } = [];
    public int Total { get; private set; }

    public async Task OnGetAsync()
    {
        (Rows, Actions, Total) = await tx.RunAsync(async s =>
        {
            var query = s.Kernel.AuditEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(Action))
                query = query.Where(a => a.Action == Action);
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var like = $"%{Q.Trim()}%";
                query = query.Where(a =>
                    EF.Functions.ILike(a.ActorNameSnapshot, like) ||
                    EF.Functions.ILike(a.Entity, like) ||
                    (a.After != null && EF.Functions.ILike(a.After, like)));
            }

            var total = await s.Kernel.AuditEvents.CountAsync();
            var rows = await query.OrderByDescending(a => a.Id).Take(150)
                .Select(a => new AuditRow(a.Id, a.At, a.ActorNameSnapshot, a.Action,
                    a.Entity, a.EntityId, a.After))
                .ToListAsync();

            var actions = await s.Kernel.AuditEvents.AsNoTracking()
                .Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();

            return ((IReadOnlyList<AuditRow>)rows, (IReadOnlyList<string>)actions, total);
        });
    }
}
