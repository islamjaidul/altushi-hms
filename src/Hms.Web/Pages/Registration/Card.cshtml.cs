using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Registration;

/// <summary>
/// The patient ID card (§9A.2 module 1). Printing is a re-render of the same layout, so what
/// the operator saw is exactly what leaves the printer (§7 U10) — and works with no printer
/// attached, because the preview is the fallback (edge 2).
/// </summary>
[Authorize(Policy = Perm.RegistrationRead)]
public class CardModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public string Uhid { get; private set; } = "";
    public string FullName { get; private set; } = "";
    public string Age { get; private set; } = "";
    public char Sex { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? BloodGroup { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var patient = await tx.RunAsync(s =>
            s.Reg.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id));
        if (patient is null) return NotFound();

        Uhid = patient.Uhid;
        FullName = patient.FullName;
        Sex = patient.Sex;
        Age = Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today);
        Phone = patient.Phone;
        Address = patient.Address ?? patient.Area;
        BloodGroup = patient.BloodGroup;
        RegisteredAt = patient.CreatedAt;
        return Page();
    }
}
