using Hms.Diagnostics.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Diagnostics;

/// <summary>
/// 05 §5 screen 14. Two rules live here and both are enforced, not merely displayed: an unpaid
/// report is not handed over (the collect action is the only control offered), and every handover
/// is logged with the report version that left the building (edge 22).
/// </summary>
[Authorize(Policy = Perm.DiagnosticsOrderCreate)]
public class DeliveryModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? OrderId { get; set; }

    public IReadOnlyList<LabCard> Ready { get; private set; } = [];
    public IReadOnlyList<LabCard> Delivered { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var all = await LabBoard.LoadAsync(s);
            Ready = all.Where(c => c.Stage == LabBoard.Verified).ToList();
            Delivered = all.Where(c => c.Stage == LabBoard.Delivered).Take(20).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostDeliverAsync(long orderId, string? collector)
    {
        await LoadAsync();
        var card = Ready.FirstOrDefault(c => c.OrderId == orderId);
        if (card is null) { Fail("That report is not ready for delivery — refresh the list."); return Page(); }

        // The money rule (§9A.2 module 4): a held report is not released, whatever the counter says.
        if (card.Held)
        {
            Fail($"Report held — {Ui.Money(card.Due)} is still due on this order. " +
                 "Collect the balance at Due Collection, then hand it over.");
            return Page();
        }

        await tx.RunAsync(async s =>
        {
            var version = await s.Lis.Results.AsNoTracking()
                .Where(r => card.Tests.Select(t => t.OrderTestId).Contains(r.OrderTestId))
                .MaxAsync(r => (int?)r.Version) ?? 1;

            s.Diag.Deliveries.Add(new DeliveryLog
            {
                TestOrderId = orderId,
                ReportVersion = version,
                DeliveredAt = clock.GetUtcNow(),
                CollectorNote = string.IsNullOrWhiteSpace(collector) ? "Patient" : collector.Trim(),
                DeliveredBy = ActorId,
            });
            await s.Diag.SaveChangesAsync();

            await s.Diag.Database.ExecuteSqlAsync(
                $"UPDATE diag.test_order SET state = 'delivered' WHERE id = {orderId}");
            return 0;
        });

        Toast($"{card.OrderNo} delivered to {(string.IsNullOrWhiteSpace(collector) ? "the patient" : collector)}",
              "task_alt");
        return Redirect("/diagnostics/delivery");
    }
}
