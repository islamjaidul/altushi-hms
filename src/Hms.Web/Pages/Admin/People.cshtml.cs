using System.ComponentModel.DataAnnotations;
using Hms.Admin;
using Hms.Admin.Data;
using Hms.Appointments.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record DoctorRow(
    long DoctorId, string Name, string? Room, int MaxSerials, TimeOnly From, TimeOnly To,
    long? ConsultationServiceId, string? ConsultationLabel);
public sealed record ReferrerRow(long Id, string Code, string Name, string Kind, string? Area, string? Phone, short Commission, bool Active);
public sealed record ConsultantRow(long Id, string Name, string Degrees, string? Bmdc, string Departments, bool Active);

/// <summary>A consultation service an administrator can attach to a doctor, at today's rate.</summary>
public sealed record ConsultOption(long Id, string Code, string Name, long Price);

/// <summary>
/// The people masters the MVP needs: doctors whose schedules issue serials (§5 M3 [M]),
/// referrers captured on every diagnostic order (§5 M8 [M]), and the reporting consultants who
/// sign released reports (5A-R1 [Must]). Deactivation rather than deletion throughout — a name
/// on an issued document has to stay readable (§8 N5).
/// </summary>
[Authorize(Policy = Perm.AdminMastersManage)]
public class PeopleModel(HmsTx tx, RateResolver rates, TimeProvider clock) : HmsPageModel
{
    [BindProperty, StringLength(Bounds.Name)] public string? Name { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Room { get; set; }
    [BindProperty, Range(1, 500, ErrorMessage = "Serials per day must be between 1 and 500")]
    public int MaxSerials { get; set; } = 40;
    /// <summary>Spec 0043: what the OPD counter should offer for this doctor. 0 = leave unset.</summary>
    [BindProperty] public long ConsultationServiceId { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Code { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string Kind { get; set; } = "doctor";
    [BindProperty, StringLength(Bounds.Name)] public string? Area { get; set; }
    [BindProperty, StringLength(Bounds.Phone)] public string? Phone { get; set; }
    [BindProperty, Percent] public short Commission { get; set; }
    [BindProperty, StringLength(Bounds.Name)] public string? Degrees { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Bmdc { get; set; }
    [BindProperty, StringLength(Bounds.Address)] public string? Departments { get; set; }

    public IReadOnlyList<DoctorRow> Doctors { get; private set; } = [];
    public IReadOnlyList<ReferrerRow> Referrers { get; private set; } = [];
    public IReadOnlyList<ConsultantRow> Consultants { get; private set; } = [];
    public IReadOnlyList<ConsultOption> ConsultOptions { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        (Doctors, Referrers, Consultants) = await tx.RunAsync(async s =>
        {
            // Spec 0043: the consultation services a doctor can be pointed at, priced at today's
            // rate so the administrator picks by what the patient will actually pay. A service
            // with no rate today is not offered — same rule as the OPD catalogue (§7 U7, edge 11).
            var consultServices = await s.Adm.Services.AsNoTracking()
                .Where(x => x.Active && x.Kind == "consult")
                .OrderBy(x => x.Name).ToListAsync();
            var options = new List<ConsultOption>();
            foreach (var svc in consultServices)
            {
                try
                {
                    var rate = await rates.ResolveAsync(s.Adm, "service", svc.Id, today);
                    options.Add(new ConsultOption(svc.Id, svc.Code, svc.Name, rate.Price));
                }
                catch (RateResolutionException) { }
            }
            ConsultOptions = options;
            var labelById = options.ToDictionary(o => o.Id, o => $"{o.Name} — {Ui.Money(o.Price)}");

            var doctorFees = await s.Appt.Doctors.AsNoTracking()
                .ToDictionaryAsync(d => d.Id, d => d.ConsultationServiceId);

            var docs = (await s.Appt.Schedules.AsNoTracking().ToListAsync())
                .GroupBy(x => x.DoctorId)
                .Select(g =>
                {
                    var serviceId = doctorFees.GetValueOrDefault(g.Key);
                    return new DoctorRow(g.Key, g.First().DoctorName, g.First().Room,
                        g.First().MaxSerials, g.First().SlotFrom, g.First().SlotTo,
                        serviceId,
                        serviceId is { } id ? labelById.GetValueOrDefault(id) : null);
                })
                .OrderBy(d => d.Name).ToList();

            var refs = await s.Adm.Referrers.AsNoTracking().OrderBy(r => r.Name)
                .Select(r => new ReferrerRow(r.Id, r.Code, r.Name, r.Kind, r.Area, r.Phone,
                    r.CommissionPercent, r.Active)).ToListAsync();

            var cons = (await s.Adm.ReportingConsultants.AsNoTracking().OrderBy(c => c.Name).ToListAsync())
                .Select(c => new ConsultantRow(c.Id, c.Name, c.Degrees, c.BmdcNo,
                    c.Departments.Length == 0 ? "all departments" : string.Join(", ", c.Departments),
                    c.Active)).ToList();

            return ((IReadOnlyList<DoctorRow>)docs, (IReadOnlyList<ReferrerRow>)refs,
                    (IReadOnlyList<ConsultantRow>)cons);
        });
    }

    public async Task<IActionResult> OnPostDoctorAsync()
    {
        if (string.IsNullOrWhiteSpace(Name)) { await LoadAsync(); Fail("Doctor name is required."); return Page(); }
        await tx.RunAsync(async s =>
        {
            // WP2.5 (AUD-ARCH-05): the id comes from appt.doctor's identity column, not MAX+1 —
            // two administrators adding doctors at the same moment get two distinct identities
            // by construction. The doctor row must exist before the schedule row: the schedule
            // carries a foreign key to it.
            var doctor = new Doctor
            {
                Name = Name!.Trim(),
                // 0 is the "— not set —" option, not a service id (spec 0043).
                ConsultationServiceId = ConsultationServiceId > 0 ? ConsultationServiceId : null,
            };
            s.Appt.Doctors.Add(doctor);
            await s.Appt.SaveChangesAsync();

            s.Appt.Schedules.Add(new DoctorSchedule
            {
                DoctorId = doctor.Id, DoctorName = doctor.Name, Room = Room?.Trim(),
                MaxSerials = MaxSerials <= 0 ? 40 : MaxSerials,
                Weekday = 0, SlotFrom = new TimeOnly(9, 0), SlotTo = new TimeOnly(14, 0),
            });
            await s.Appt.SaveChangesAsync();
            return 0;
        });
        Toast($"{Name} added — serials can be issued now", "person_add");
        return Redirect("/admin/people");
    }

    /// <summary>
    /// Spec 0043: point an existing doctor at the consultation the OPD counter should offer.
    /// Separate from the add form because the four seeded doctors — and any created before this
    /// existed — would otherwise be unreachable without SQL, which AC3 forbids.
    /// </summary>
    public async Task<IActionResult> OnPostDoctorFeeAsync(long doctorId)
    {
        var name = await tx.RunAsync(async s =>
        {
            var doctor = await s.Appt.Doctors.SingleOrDefaultAsync(d => d.Id == doctorId);
            if (doctor is null) return null;
            doctor.ConsultationServiceId = ConsultationServiceId > 0 ? ConsultationServiceId : null;
            await s.Appt.SaveChangesAsync();
            return doctor.Name;
        });

        if (name is null) { await LoadAsync(); Fail("That doctor no longer exists."); return Page(); }
        Toast(ConsultationServiceId > 0
            ? $"{name}'s consultation fee set — the OPD counter will offer it"
            : $"{name}'s consultation fee cleared", "payments");
        return Redirect("/admin/people");
    }

    public async Task<IActionResult> OnPostReferrerAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Code))
        { await LoadAsync(); Fail("Referrer code and name are both required."); return Page(); }

        var code = Code!.Trim().ToUpperInvariant();
        try
        {
            await tx.RunAsync(async s =>
            {
                if (await s.Adm.Referrers.AnyAsync(r => r.Code == code))
                    throw new InvalidOperationException($"Referrer code {code} is already in use.");
                s.Adm.Referrers.Add(new Referrer
                {
                    Code = code, Name = Name!.Trim(), Kind = Kind,
                    Area = Area?.Trim(), Phone = Phone?.Trim(),
                    CommissionPercent = Math.Clamp(Commission, (short)0, (short)100),
                });
                await s.Adm.SaveChangesAsync();
                return 0;
            });
        }
        catch (InvalidOperationException e) { await LoadAsync(); Fail(e.Message); return Page(); }

        Toast($"Referrer {code} added", "handshake");
        return Redirect("/admin/people");
    }

    public async Task<IActionResult> OnPostConsultantAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Degrees))
        { await LoadAsync(); Fail("Consultant name and qualifications are both required."); return Page(); }

        await tx.RunAsync(async s =>
        {
            s.Adm.ReportingConsultants.Add(new ReportingConsultant
            {
                Name = Name!.Trim(), Degrees = Degrees!.Trim(), BmdcNo = Bmdc?.Trim(),
                Departments = string.IsNullOrWhiteSpace(Departments)
                    ? []
                    : Departments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
            await s.Adm.SaveChangesAsync();
            return 0;
        });
        Toast($"{Name} can now sign reports", "verified");
        return Redirect("/admin/people");
    }

    /// <summary>Never a delete — the name has to stay readable on documents already issued.</summary>
    public async Task<IActionResult> OnPostToggleReferrerAsync(long id)
    {
        await tx.RunAsync(async s =>
        {
            var r = await s.Adm.Referrers.SingleAsync(x => x.Id == id);
            r.Active = !r.Active;
            await s.Adm.SaveChangesAsync();
            return 0;
        });
        Toast("Referrer updated", "task_alt");
        return Redirect("/admin/people");
    }
}
