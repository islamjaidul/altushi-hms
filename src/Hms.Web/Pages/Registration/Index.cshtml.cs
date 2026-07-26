using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Registration;

public sealed record PatientRow(
    long Id, string Uhid, string FullName, char Sex, string Age, string? Phone,
    string? Area, string? BloodGroup, long Due);

/// <summary>
/// 05 §5 screen 3. One search box covers name, UHID and phone — the operator never has to
/// choose which field they are searching (§7 U5). A card scan lands here with q = the UHID.
/// </summary>
[Authorize(Policy = Perm.RegistrationRead)]
public class IndexModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public IReadOnlyList<PatientRow> Rows { get; private set; } = [];
    public int TotalPatients { get; private set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var term = Q?.Trim();

        (Rows, TotalPatients) = await tx.RunAsync(async s =>
        {
            var query = s.Reg.Patients.AsNoTracking().Where(p => p.Active && p.MergedInto == null);
            var total = await query.CountAsync();

            if (!string.IsNullOrWhiteSpace(term))
            {
                var like = $"%{term}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.FullName, like) ||
                    EF.Functions.ILike(p.Uhid, like) ||
                    (p.Phone != null && EF.Functions.ILike(p.Phone, like)));
            }

            var patients = await query
                .OrderByDescending(p => p.Id)
                .Take(60)
                .ToListAsync();

            // Outstanding balance per patient: dues join their invoice (03 §4).
            var ids = patients.Select(p => p.Id).ToList();
            var dues = await (
                from d in s.Bill.Dues
                join i in s.Bill.Invoices on d.InvoiceId equals i.Id
                where ids.Contains(i.PatientId) && d.Balance > 0
                group d by i.PatientId into g
                select new { PatientId = g.Key, Balance = g.Sum(x => x.Balance) })
                .ToDictionaryAsync(x => x.PatientId, x => x.Balance);

            var rows = patients.Select(p => new PatientRow(
                p.Id, p.Uhid, p.FullName, p.Sex,
                Ui.AgeDisplay(p.Dob, p.AgeYears, p.AgeMonths, today),
                p.Phone, p.Area, p.BloodGroup,
                dues.GetValueOrDefault(p.Id))).ToList();

            return ((IReadOnlyList<PatientRow>)rows, total);
        });
    }
}
