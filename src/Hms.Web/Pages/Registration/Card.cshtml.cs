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
    /// <summary>Spec 0045: the card links to the edit screen, so it needs the patient's id.</summary>
    public long Id { get; private set; }
    public string Uhid { get; private set; } = "";
    public string FullName { get; private set; } = "";
    public string Age { get; private set; } = "";
    public char Sex { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? BloodGroup { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    /// <summary>True when this card has been printed before — shown on the card itself.</summary>
    public bool Reissue { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var patient = await tx.RunAsync(async s =>
        {
            var p = await s.Reg.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (p is null) return null;

            // §5 M1 [M]: "card re-issue with audit". A card is an identity document — every
            // issue of one is attributable, so a duplicate in circulation can be traced.
            var reissue = await s.Kernel.AuditEvents
                .AnyAsync(a => a.Entity == "reg.patient" && a.EntityId == id && a.Action == "patient.card_print");
            s.Kernel.AuditEvents.Add(new Hms.Kernel.Data.AuditEvent
            {
                BranchId = BranchId, At = clock.GetUtcNow(), ActorId = ActorId,
                ActorNameSnapshot = ActorName, Action = "patient.card_print",
                Entity = "reg.patient", EntityId = id,
                After = System.Text.Json.JsonSerializer.Serialize(new { p.Uhid, reissue }),
                CorrelationId = Guid.NewGuid(), Tier = 1,
            });
            await s.Kernel.SaveChangesAsync();
            Reissue = reissue;
            return p;
        });
        if (patient is null) return NotFound();

        Id = patient.Id;
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
