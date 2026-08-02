using System.ComponentModel.DataAnnotations;
using Hms.Admin;
using Hms.Billing;
using Hms.Ipd;
using Hms.Ipd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record BedChoice(long Id, string Code, string Ward, string Class, long Tariff);
public sealed record DoctorChoice(long Id, string Name);
public sealed record PackageChoice(long Id, string Name, short SvcPct, long Price);

/// <summary>
/// §5 M6 admission: patient (typeahead), consultant, source, bed from the free list, optional
/// package (5A-9). Admission fee and the package line post to the folio at birth; the advance
/// is taken at the folio screen by whoever holds the counter drawer.
/// </summary>
[Authorize(Policy = Perm.IpdManage)]
public class AdmitModel(
    HmsTx tx, IpdService ipd, BillingService billing, RateResolver rates, TimeProvider clock)
    : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? BedId { get; set; }
    [BindProperty] public long PatientId { get; set; }
    [BindProperty] public long? DoctorId { get; set; }
    [BindProperty] public string Source { get; set; } = "direct";
    /// <summary>AUD-VAL-13b: a 100 KB paste once stored verbatim.</summary>
    [BindProperty, StringLength(Bounds.Clinical)] public string? ProvisionalDx { get; set; }
    [BindProperty] public long? PackageId { get; set; }
    /// <summary>AUD-VAL-13a: −50% once reached the folio's service-charge math.</summary>
    [BindProperty, Percent] public short ServiceChargePct { get; set; }
    [BindProperty] public bool ReserveOnly { get; set; }

    public IReadOnlyList<BedChoice> FreeBeds { get; private set; } = [];
    public IReadOnlyList<DoctorChoice> Doctors { get; private set; } = [];
    public IReadOnlyList<PackageChoice> Packages { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        await tx.RunAsync(async s =>
        {
            var beds = await s.Ipd.Beds.AsNoTracking()
                .Where(b => b.State == BedState.Free).OrderBy(b => b.Code).ToListAsync();
            var wards = await s.Ipd.Wards.AsNoTracking().ToDictionaryAsync(w => w.Id);
            var choices = new List<BedChoice>();
            foreach (var b in beds)
            {
                long price = 0;
                try { price = (await rates.ResolveAsync(s.Adm, "service", b.TariffServiceId, today)).Price; }
                catch (RateResolutionException) { }
                var ward = wards.GetValueOrDefault(b.WardId);
                choices.Add(new BedChoice(b.Id, b.Code, ward?.Name ?? "—", ward?.Class ?? "—", price));
            }
            FreeBeds = choices;

            Doctors = (await s.Appt.Schedules.AsNoTracking().ToListAsync())
                .GroupBy(x => x.DoctorId)
                .Select(g => new DoctorChoice(g.Key, g.First().DoctorName))
                .OrderBy(d => d.Name).ToList();

            var packages = await s.Ipd.Packages.AsNoTracking().Where(p => p.Active).ToListAsync();
            var packageChoices = new List<PackageChoice>();
            foreach (var p in packages)
            {
                long price = 0;
                try { price = (await rates.ResolveAsync(s.Adm, "service", p.ServiceCatalogId, today)).Price; }
                catch (RateResolutionException) { }
                packageChoices.Add(new PackageChoice(p.Id, p.Name, p.DefaultServiceChargePct, price));
            }
            Packages = packageChoices;
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (PatientId == 0) { await LoadAsync(); Fail("Select a patient first."); return Page(); }
        if (BedId is null or 0) { await LoadAsync(); Fail("Pick a bed from the free list."); return Page(); }

        try
        {
            var admissionId = await tx.RunAsync(async s =>
            {
                // AUD-VAL-12: the typeahead constrains the browser, not the request — an id
                // nothing points at must not become an admission. (The bed is checked inside
                // AdmitAsync, which locks it and refuses an unknown or occupied one.)
                if (!await s.Reg.Patients.AnyAsync(p => p.Id == PatientId))
                    throw new IpdException("That patient is not on the register — search and pick them again.");

                await IpdBilling.EnsureNotBlockedAsync(s, PatientId);      // R4

                short svcPct = ServiceChargePct;
                if (PackageId is long pkgId && svcPct == 0)
                    svcPct = (await s.Ipd.Packages.AsNoTracking().SingleAsync(p => p.Id == pkgId))
                        .DefaultServiceChargePct;

                var admission = await ipd.AdmitAsync(
                    s.Ipd, s.Kernel, BranchId, PatientId, DoctorId, Source,
                    string.IsNullOrWhiteSpace(ProvisionalDx) ? null : ProvisionalDx.Trim(),
                    BedId.Value, PackageId, svcPct, ReserveOnly, ActorId, ActorName);

                if (!ReserveOnly)
                {
                    var feeService = await s.Adm.Services.AsNoTracking()
                        .Where(x => x.Code == "IPD-ADM-FEE" && x.Active)
                        .Select(x => (long?)x.Id).FirstOrDefaultAsync();
                    await IpdBilling.PostAdmissionChargesAsync(
                        s, billing, rates, clock, BranchId, admission, feeService, ActorId);
                }
                return admission.Id;
            });

            Toast(ReserveOnly ? "Bed reserved" : "Admitted — take the advance at the counter panel",
                "person_add");
            return Redirect(ReserveOnly ? "/ipd/admissions" : $"/ipd/folio/{admissionId}");
        }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        catch (BillingException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        catch (RateResolutionException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }
}
