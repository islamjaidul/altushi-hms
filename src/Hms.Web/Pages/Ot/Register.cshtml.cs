using Hms.Kernel.Time;
using Hms.Ot.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ot;

public sealed record RegisterRow(
    string CaseNo, DateTimeOffset At, string Patient, string Uhid, string Operation,
    string Theatre, string Surgeon, string Anaesthetist, string State, long Billed);

/// <summary>
/// M7 [M]: the operation register — the chronological, statutory-style log a hospital is expected
/// to be able to produce on request. Read-only: the register reports the record, it is not a
/// second copy of it.
/// </summary>
[Authorize(Policy = Perm.OtRead)]
public class RegisterModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public IReadOnlyList<RegisterRow> Rows { get; private set; } = [];
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        ToDate = !string.IsNullOrWhiteSpace(To) && FlexibleDate.TryParse(To, out var t) ? t : today;
        FromDate = !string.IsNullOrWhiteSpace(From) && FlexibleDate.TryParse(From, out var f)
            ? f : ToDate.AddDays(-30);

        var start = Ui.DhakaMidnightUtc(FromDate);
        var end = Ui.DhakaMidnightUtc(ToDate).AddDays(1);

        await tx.RunAsync(async s =>
        {
            var cases = await s.Ot.Cases.AsNoTracking()
                .Where(c => c.ScheduledFrom >= start && c.ScheduledFrom < end)
                .OrderBy(c => c.ScheduledFrom).Take(500).ToListAsync();
            if (cases.Count == 0) return 0;

            var caseIds = cases.Select(c => c.Id).ToList();
            var team = await s.Ot.Team.AsNoTracking()
                .Where(t => caseIds.Contains(t.CaseId)).ToListAsync();
            var consumables = await s.Ot.Consumables.AsNoTracking()
                .Where(x => caseIds.Contains(x.CaseId)).ToListAsync();
            var theatres = await s.Ot.Theatres.AsNoTracking()
                .ToDictionaryAsync(t => t.Id, t => t.Name);
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => cases.Select(c => c.PatientId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            Rows = cases.Select(c =>
            {
                var patient = patients.GetValueOrDefault(c.PatientId);
                var mine = team.Where(t => t.CaseId == c.Id).ToList();
                return new RegisterRow(
                    c.CaseNo, c.StartedAt ?? c.ScheduledFrom, patient?.FullName ?? "—",
                    patient?.Uhid ?? "—", c.OperationName,
                    theatres.GetValueOrDefault(c.TheatreId, "—"),
                    mine.FirstOrDefault(t => t.Role == TeamRole.Surgeon)?.PersonName ?? "—",
                    mine.FirstOrDefault(t => t.Role == TeamRole.Anaesthetist)?.PersonName ?? "—",
                    c.State,
                    mine.Sum(t => t.AmountPosted)
                    + consumables.Where(x => x.CaseId == c.Id).Sum(x => x.UnitPrice * x.Qty));
            }).ToList();
            return 0;
        });
    }
}
