using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Diagnostics;

public sealed record SlipTest(string Name, long Amount);
public sealed record SampleLabel(string Barcode, string SampleType, IReadOnlyList<string> Tests);

/// <summary>
/// The order slip and its barcode labels — the most tangible "this is real software" moment in
/// the demo (§9A.2 module 4). A reprint reuses the same barcode (edge 27): identity belongs to
/// the sample, never to the piece of paper.
/// </summary>
[Authorize(Policy = Perm.DiagnosticsOrderCreate)]
public class SlipModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public long OrderId { get; private set; }
    public string PublicToken { get; private set; } = "";   // AUD-PHI-01: the public lookup code
    public string State { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset PromisedAt { get; private set; }
    public string PatientName { get; private set; } = "";
    public string Uhid { get; private set; } = "";
    public string Age { get; private set; } = "";
    public char Sex { get; private set; }
    public string? InvoiceNo { get; private set; }
    public long? InvoiceId { get; private set; }
    public long Due { get; private set; }
    public IReadOnlyList<SlipTest> Tests { get; private set; } = [];
    public IReadOnlyList<SampleLabel> Labels { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var found = await tx.RunAsync(async s =>
        {
            var order = await s.Diag.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id);
            if (order is null) return false;

            OrderId = order.Id;
            PublicToken = order.PublicToken;
            State = order.State;
            CreatedAt = order.CreatedAt;
            PromisedAt = order.PromisedAt;

            var patient = await s.Reg.Patients.AsNoTracking().SingleAsync(p => p.Id == order.PatientId);
            PatientName = patient.FullName;
            Uhid = patient.Uhid;
            Sex = patient.Sex;
            Age = Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today);

            var orderTests = await s.Diag.OrderTests.AsNoTracking()
                .Where(ot => ot.TestOrderId == id).ToListAsync();
            var catalogIds = orderTests.Select(ot => ot.TestCatalogId).ToList();
            var names = await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => catalogIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            var chargeIds = orderTests.Select(ot => ot.ChargeLineId).ToList();
            var charges = await s.Bill.ChargeLines.AsNoTracking()
                .Where(c => chargeIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Amount);

            Tests = orderTests
                .Select(ot => new SlipTest(names.GetValueOrDefault(ot.TestCatalogId, "Test"),
                    charges.GetValueOrDefault(ot.ChargeLineId)))
                .ToList();

            if (order.InvoiceId is { } invId)
            {
                var invoice = await s.Bill.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == invId);
                InvoiceNo = invoice?.InvoiceNo;
                InvoiceId = invId;
                Due = await s.Bill.Dues.AsNoTracking().Where(d => d.InvoiceId == invId)
                    .Select(d => (long?)d.Balance).FirstOrDefaultAsync() ?? 0;
            }

            var orderTestIds = orderTests.Select(ot => ot.Id).ToList();
            var sampleTests = await s.Lis.SampleTests.AsNoTracking()
                .Where(st => orderTestIds.Contains(st.OrderTestId)).ToListAsync();
            var sampleIds = sampleTests.Select(st => st.SampleId).Distinct().ToList();
            var samples = await s.Lis.Samples.AsNoTracking()
                .Where(x => sampleIds.Contains(x.Id)).ToListAsync();

            var testNameByOrderTest = orderTests.ToDictionary(
                ot => ot.Id, ot => names.GetValueOrDefault(ot.TestCatalogId, "Test"));

            Labels = samples.Select(sample => new SampleLabel(
                sample.Barcode, sample.SampleType,
                sampleTests.Where(st => st.SampleId == sample.Id)
                    .Select(st => testNameByOrderTest.GetValueOrDefault(st.OrderTestId, "Test"))
                    .ToList())).ToList();

            return true;
        });

        return found ? Page() : NotFound();
    }
}
