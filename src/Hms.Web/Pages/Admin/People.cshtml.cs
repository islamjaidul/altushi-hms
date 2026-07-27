using Hms.Admin.Data;
using Hms.Appointments.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record DoctorRow(long DoctorId, string Name, string? Room, int MaxSerials, TimeOnly From, TimeOnly To);
public sealed record ReferrerRow(long Id, string Code, string Name, string Kind, string? Area, string? Phone, short Commission, bool Active);
public sealed record ConsultantRow(long Id, string Name, string Degrees, string? Bmdc, string Departments, bool Active);

/// <summary>
/// The people masters the MVP needs: doctors whose schedules issue serials (§5 M3 [M]),
/// referrers captured on every diagnostic order (§5 M8 [M]), and the reporting consultants who
/// sign released reports (5A-R1 [Must]). Deactivation rather than deletion throughout — a name
/// on an issued document has to stay readable (§8 N5).
/// </summary>
[Authorize(Policy = Perm.AdminMastersManage)]
public class PeopleModel(HmsTx tx) : HmsPageModel
{
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Room { get; set; }
    [BindProperty] public int MaxSerials { get; set; } = 40;
    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string Kind { get; set; } = "doctor";
    [BindProperty] public string? Area { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public short Commission { get; set; }
    [BindProperty] public string? Degrees { get; set; }
    [BindProperty] public string? Bmdc { get; set; }
    [BindProperty] public string? Departments { get; set; }

    public IReadOnlyList<DoctorRow> Doctors { get; private set; } = [];
    public IReadOnlyList<ReferrerRow> Referrers { get; private set; } = [];
    public IReadOnlyList<ConsultantRow> Consultants { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        (Doctors, Referrers, Consultants) = await tx.RunAsync(async s =>
        {
            var docs = (await s.Appt.Schedules.AsNoTracking().ToListAsync())
                .GroupBy(x => x.DoctorId)
                .Select(g => new DoctorRow(g.Key, g.First().DoctorName, g.First().Room,
                    g.First().MaxSerials, g.First().SlotFrom, g.First().SlotTo))
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
            var nextId = (await s.Appt.Schedules.MaxAsync(x => (long?)x.DoctorId) ?? 0) + 1;
            s.Appt.Schedules.Add(new DoctorSchedule
            {
                DoctorId = nextId, DoctorName = Name!.Trim(), Room = Room?.Trim(),
                MaxSerials = MaxSerials <= 0 ? 40 : MaxSerials,
                Weekday = 0, SlotFrom = new TimeOnly(9, 0), SlotTo = new TimeOnly(14, 0),
            });
            await s.Appt.SaveChangesAsync();
            return 0;
        });
        Toast($"{Name} added — serials can be issued now", "person_add");
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
