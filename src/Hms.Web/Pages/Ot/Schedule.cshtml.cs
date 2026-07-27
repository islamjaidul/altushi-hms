using Hms.Kernel.Time;
using Hms.Ot;
using Hms.Ot.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ot;

public sealed record SchedulablePatient(long PatientId, string Label, long? FolioId, long? EncounterId);
public sealed record Operation(long ServiceId, string Name, long Price);
public sealed record Person(long Id, string Name);

/// <summary>
/// US7.1: theatre, time and team in one form, and a clash is refused rather than warned about
/// (§7 U7 — the illegal action does not succeed quietly and get fixed later).
/// </summary>
[Authorize(Policy = Perm.OtSchedule)]
public class ScheduleModel(HmsTx tx, OtService ot, TimeProvider clock) : HmsPageModel
{
    [BindProperty] public long PatientId { get; set; }
    [BindProperty] public long TheatreId { get; set; }
    [BindProperty] public long OperationServiceId { get; set; }
    [BindProperty] public string? OnDate { get; set; }
    [BindProperty] public string? FromTime { get; set; }
    [BindProperty] public int Minutes { get; set; } = 90;
    [BindProperty] public long SurgeonId { get; set; }
    [BindProperty] public long AnaesthetistId { get; set; }
    [BindProperty] public long AssistantId { get; set; }

    public IReadOnlyList<SchedulablePatient> Patients { get; private set; } = [];
    public IReadOnlyList<Theatre> Theatres { get; private set; } = [];
    public IReadOnlyList<Operation> Operations { get; private set; } = [];
    public IReadOnlyList<Person> People { get; private set; } = [];
    public string Today { get; private set; } = "";

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        Today = Ui.DateLong(today);

        await tx.RunAsync(async s =>
        {
            Theatres = await s.Ot.Theatres.AsNoTracking()
                .Where(t => t.Active).OrderBy(t => t.Name).ToListAsync();

            // The operation catalogue: OT- coded services that are not the per-role fees.
            var services = await s.Adm.Services.AsNoTracking()
                .Where(x => x.Active && x.Dept == "OT" && !x.Code.StartsWith("OT-FEE-"))
                .OrderBy(x => x.Name).ToListAsync();
            var rates = await s.Adm.RateVersions.AsNoTracking()
                .Where(v => v.CatalogKind == "service" && v.Scope == "standard"
                            && v.ValidFrom <= today && (v.ValidTo == null || v.ValidTo > today))
                .ToListAsync();
            Operations = services.Select(x => new Operation(x.Id, x.Name,
                rates.FirstOrDefault(r => r.CatalogId == x.Id)?.Price ?? 0)).ToList();

            // Admitted patients bill to their folio; today's visits bill to the counter's
            // encounter. Both are offered, labelled so the operator can tell them apart.
            var admissions = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => a.State == Hms.Ipd.Data.AdmissionState.Admitted)
                .OrderBy(a => a.Id).Take(120).ToListAsync();
            var folios = await s.Ipd.Folios.AsNoTracking()
                .Where(f => admissions.Select(a => a.Id).Contains(f.AdmissionId)).ToListAsync();
            var encounters = await s.Bill.Encounters.AsNoTracking()
                .Where(e => e.OnDate == today && e.State == "open"
                            && e.Type != PharmacySale.EncounterType)
                .OrderByDescending(e => e.Id).Take(80).ToListAsync();
            var patientIds = admissions.Select(a => a.PatientId)
                .Concat(encounters.Select(e => e.PatientId)).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            var list = new List<SchedulablePatient>();
            foreach (var admission in admissions)
            {
                var folio = folios.FirstOrDefault(f => f.AdmissionId == admission.Id);
                if (folio is null) continue;
                var patient = patients.GetValueOrDefault(admission.PatientId);
                list.Add(new SchedulablePatient(admission.PatientId,
                    $"{patient?.FullName} — {admission.AdmissionNo} (indoor)", folio.Id, null));
            }
            foreach (var encounter in encounters.Where(e =>
                         !list.Any(x => x.PatientId == e.PatientId)))
            {
                var patient = patients.GetValueOrDefault(encounter.PatientId);
                list.Add(new SchedulablePatient(encounter.PatientId,
                    $"{patient?.FullName} — {patient?.Uhid} (day case)", null, encounter.Id));
            }
            Patients = list;

            People = (await s.Auth.Users.AsNoTracking()
                    .Where(u => u.Active).OrderBy(u => u.DisplayName).ToListAsync())
                .Select(u => new Person(u.Id, u.DisplayName)).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var chosen = await tx.RunAsync(async s => await s.Ot.Theatres.AsNoTracking()
            .AnyAsync(t => t.Id == TheatreId));
        if (!chosen) { await LoadAsync(); Fail("Choose a theatre."); return Page(); }

        var date = !string.IsNullOrWhiteSpace(OnDate) && FlexibleDate.TryParse(OnDate, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        if (!TimeOnly.TryParse(FromTime ?? "", out var startTime))
        {
            await LoadAsync();
            Fail("Give a start time as HH:mm — for example 09:30.");
            return Page();
        }
        if (Minutes < 15 || Minutes > 720)
        {
            await LoadAsync();
            Fail("How long is the operation? Give something between 15 minutes and 12 hours.");
            return Page();
        }

        var from = Ui.DhakaMidnightUtc(date).Add(startTime.ToTimeSpan());
        var to = from.AddMinutes(Minutes);

        try
        {
            var caseId = await tx.RunAsync(async s =>
            {
                var patient = (await PatientsForPostAsync(s)).FirstOrDefault(p => p.PatientId == PatientId)
                    ?? throw new OtException("Choose the patient from the list.");
                var operation = await s.Adm.Services.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == OperationServiceId)
                    ?? throw new OtException("Choose the operation.");
                var fees = await s.Adm.Services.AsNoTracking()
                    .Where(x => x.Code.StartsWith("OT-FEE-"))
                    .ToDictionaryAsync(x => x.Code, x => x.Id);
                var names = await s.Auth.Users.AsNoTracking()
                    .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

                var team = new List<TeamSlot>
                {
                    new(TeamRole.Surgeon, SurgeonId, names.GetValueOrDefault(SurgeonId, "—"),
                        fees.GetValueOrDefault("OT-FEE-SURG")),
                };
                if (AnaesthetistId != 0)
                    team.Add(new TeamSlot(TeamRole.Anaesthetist, AnaesthetistId,
                        names.GetValueOrDefault(AnaesthetistId, "—"), fees.GetValueOrDefault("OT-FEE-ANAE")));
                if (AssistantId != 0)
                    team.Add(new TeamSlot(TeamRole.Assistant, AssistantId,
                        names.GetValueOrDefault(AssistantId, "—"), fees.GetValueOrDefault("OT-FEE-ASST")));

                var otCase = await ot.ScheduleAsync(s.Ot, s.Kernel, BranchId, PatientId,
                    patient.FolioId, patient.EncounterId, TheatreId, OperationServiceId,
                    operation.Name, from, to, team, ActorId, ActorName);
                return otCase.Id;
            });

            Toast("Operation scheduled", "calendar_add_on");
            return Redirect($"/ot/case/{caseId}");
        }
        catch (OtException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    /// <summary>The same list the screen offered, re-read inside the posting transaction.</summary>
    private async Task<IReadOnlyList<SchedulablePatient>> PatientsForPostAsync(TxScope s)
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var list = new List<SchedulablePatient>();

        var folio = await s.Ipd.Folios.AsNoTracking()
            .Join(s.Ipd.Admissions.AsNoTracking().Where(a =>
                    a.PatientId == PatientId && a.State == Hms.Ipd.Data.AdmissionState.Admitted),
                f => f.AdmissionId, a => a.Id, (f, a) => f)
            .FirstOrDefaultAsync();
        if (folio is not null) list.Add(new SchedulablePatient(PatientId, "", folio.Id, null));

        var encounter = await s.Bill.Encounters.AsNoTracking().FirstOrDefaultAsync(e =>
            e.PatientId == PatientId && e.OnDate == today && e.State == "open"
            && e.Type != PharmacySale.EncounterType);
        if (folio is null && encounter is not null)
            list.Add(new SchedulablePatient(PatientId, "", null, encounter.Id));
        return list;
    }
}
