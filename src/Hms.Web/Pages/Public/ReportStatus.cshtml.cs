using Hms.Diagnostics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Public;

public sealed record TestStatusRow(string Test, string Status);

/// <summary>
/// R3 (spec 0019): "is my report ready?" without a login. Lookup is by the LB- order number
/// printed on the receipt; the answer is masked identity + per-test progress only. Unknown
/// and malformed numbers get the SAME message, so probing order ids leaks nothing (§8 N5).
/// </summary>
[AllowAnonymous]
public class ReportStatusModel(HmsTx tx) : PageModel
{
    public string? OrderNo { get; private set; }
    public string? MaskedName { get; private set; }
    public IReadOnlyList<TestStatusRow> Tests { get; private set; } = [];
    public bool NoMatch { get; private set; }

    public async Task OnGetAsync(string? orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) return;
        OrderNo = orderNo.Trim().ToUpperInvariant();

        var digits = new string(OrderNo.Where(char.IsDigit).ToArray());
        if (!long.TryParse(digits, out var orderId) || orderId <= 0)
        {
            NoMatch = true;
            return;
        }

        await tx.RunAsync(async s =>
        {
            var order = await s.Diag.Orders.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == orderId);
            if (order is null || order.State == TestOrderState.Cancelled)
            {
                NoMatch = true;
                return 0;
            }

            var patient = await s.Reg.Patients.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == order.PatientId);
            MaskedName = Ui.MaskName(patient?.FullName ?? "");

            var orderTests = await s.Diag.OrderTests.AsNoTracking()
                .Where(t => t.TestOrderId == orderId).ToListAsync();
            var catalogIds = orderTests.Select(t => t.TestCatalogId).Distinct().ToList();
            var names = await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => catalogIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            // The public wording collapses internal states into three honest phases.
            var orderPhase = order.State switch
            {
                TestOrderState.Delivered => "Delivered",
                TestOrderState.Reported => "Ready for delivery",
                _ => "In progress",
            };
            Tests = orderTests.Select(t => new TestStatusRow(
                names.GetValueOrDefault(t.TestCatalogId, "Test"),
                t.State == "reported" ? "Ready for delivery" : orderPhase)).ToList();
            return 0;
        });
    }
}
