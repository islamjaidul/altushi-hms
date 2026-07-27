using Hms.Ot.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ot;

public sealed record BoardCase(
    long Id, string CaseNo, string Patient, string Operation, string Surgeon, string State,
    DateTimeOffset From, DateTimeOffset To);

public sealed record BoardTheatre(string Name, IReadOnlyList<BoardCase> Cases);

/// <summary>M7 [S]: the day's theatres at a glance — what is in each and what is next.</summary>
[Authorize(Policy = Perm.OtRead)]
public class BoardModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public IReadOnlyList<BoardTheatre> Theatres { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public async Task OnGetAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var dayStart = Ui.DhakaMidnightUtc(Today);
        var dayEnd = dayStart.AddDays(1);

        await tx.RunAsync(async s =>
        {
            var theatres = await s.Ot.Theatres.AsNoTracking()
                .Where(t => t.Active).OrderBy(t => t.Name).ToListAsync();
            var cases = await s.Ot.Cases.AsNoTracking()
                .Where(c => c.ScheduledFrom >= dayStart && c.ScheduledFrom < dayEnd)
                .OrderBy(c => c.ScheduledFrom).ToListAsync();
            if (cases.Count == 0)
            {
                Theatres = theatres.Select(t => new BoardTheatre(t.Name, [])).ToList();
                return 0;
            }

            var caseIds = cases.Select(c => c.Id).ToList();
            var surgeons = (await s.Ot.Team.AsNoTracking()
                    .Where(t => caseIds.Contains(t.CaseId) && t.Role == TeamRole.Surgeon)
                    .ToListAsync())
                .ToDictionary(t => t.CaseId, t => t.PersonName);
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => cases.Select(c => c.PatientId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName);

            Theatres = theatres.Select(t => new BoardTheatre(t.Name,
                cases.Where(c => c.TheatreId == t.Id).Select(c => new BoardCase(
                    c.Id, c.CaseNo, patients.GetValueOrDefault(c.PatientId, "—"), c.OperationName,
                    surgeons.GetValueOrDefault(c.Id, "—"), c.State, c.ScheduledFrom, c.ScheduledTo))
                    .ToList())).ToList();
            return 0;
        });
    }
}
