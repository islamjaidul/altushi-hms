using Hms.Diagnostics;
using Hms.Diagnostics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Public;

/// <summary>
/// R3 (spec 0019): "is my report ready?" without a login. Spec 0039 WP6 (AUD-PHI-01) rebuilt the
/// lookup: the key is the random report code printed on the test slip — never the LB- order
/// number, which is the primary key zero-padded and was walked anonymously to read back every
/// live order. The answer is a masked name and ONE overall status line; the per-test list is
/// withheld because which investigations a person underwent is likely diagnostic information
/// under §8 N5 (PM question pending — not disclosing is the safe default). Unknown, malformed
/// and not-ready inputs all read the same, so probing leaks nothing.
/// </summary>
[AllowAnonymous]
public class ReportStatusModel(HmsTx tx) : PageModel
{
    public string? Code { get; private set; }
    public string? MaskedName { get; private set; }
    public string? Status { get; private set; }
    public bool NoMatch { get; private set; }

    public async Task OnGetAsync(string? orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) return;
        Code = orderNo.Trim();

        await tx.RunAsync(async s =>
        {
            var order = await PublicReportLookup.FindAsync(s.Diag, Code);
            if (order is null)
            {
                NoMatch = true;
                return 0;
            }

            // IgnoreQueryFilters, out loud (WP5): the kiosk has no signed-in branch; the token
            // already resolved the order, and only the masked form of the name is rendered.
            var patient = await s.Reg.Patients.AsNoTracking().IgnoreQueryFilters()
                .SingleOrDefaultAsync(p => p.Id == order.PatientId);
            MaskedName = Ui.MaskName(patient?.FullName ?? "");

            // One honest line for the whole order — no test names on a public screen.
            Status = order.State switch
            {
                TestOrderState.Delivered => "Delivered",
                TestOrderState.Reported => "Ready for collection",
                _ => "In progress",
            };
            return 0;
        });
    }
}
