using Hms.Diagnostics.Data;
using Hms.Lis.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

public sealed record LabTest(long OrderTestId, long CatalogId, string Name, bool HasResult, bool Verified);

public sealed record LabSample(long Id, string Barcode, string SampleType, string State);

public sealed record LabCard(
    long OrderId, string OrderNo, string PatientName, string Uhid, string? Phone,
    char Sex, short? AgeYears,
    IReadOnlyList<LabTest> Tests, IReadOnlyList<LabSample> Samples,
    string Stage, long Due, DateTimeOffset PromisedAt, DateTimeOffset CreatedAt,
    IReadOnlyList<string> Departments)
{
    public bool Held => Due > 0;
    public string TestList => string.Join(" · ", Tests.Select(t => t.Name));

    /// <summary>US9.4: turnaround is measured against the promise the patient was given.</summary>
    public TimeSpan Elapsed(DateTimeOffset now) => now - CreatedAt;
    public bool Breached(DateTimeOffset now) => Stage != LabBoard.Delivered && now > PromisedAt;
    public string TatText(DateTimeOffset now)
    {
        var span = Elapsed(now);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} m" : $"{span.Minutes} m";
    }
}

/// <summary>
/// The lab pipeline as the bench actually experiences it (02 §2.8). The stage is <em>derived</em>
/// from the sample chain and the result rows rather than stored twice — there is no second copy
/// of the truth to drift.
/// </summary>
public static class LabBoard
{
    public const string PendingCollection = SampleState.PendingCollection;
    public const string Collected = SampleState.Collected;
    public const string Received = SampleState.Received;
    public const string Resulted = "resulted";
    public const string Verified = "verified";
    public const string Delivered = "delivered";

    public static readonly (string Key, string Label)[] Stages =
    [
        (PendingCollection, "Awaiting collection"),
        (Collected, "Collected"),
        (Received, "Received at lab"),
        (Resulted, "Result entered"),
        (Verified, "Verified"),
        (Delivered, "Delivered"),
    ];

    public static async Task<IReadOnlyList<LabCard>> LoadAsync(
        TxScope s, int take = 120, CancellationToken ct = default)
    {
        // Only orders that reached payment are lab work (§9A.2 seam). `Invoiced` means awaiting
        // payment — such an order has no tube, so showing it here would tell the bench to work a
        // sample nobody drew. It appears the moment the balance is settled.
        string[] released = [TestOrderState.InProgress, TestOrderState.Reported, TestOrderState.Delivered];
        var orders = await s.Diag.Orders.AsNoTracking()
            .Where(o => released.Contains(o.State))
            .OrderByDescending(o => o.Id).Take(take).ToListAsync(ct);
        if (orders.Count == 0) return [];

        var orderIds = orders.Select(o => o.Id).ToList();
        // WP4 (AUD-M9-01): a refunded test must leave the worklist — radiology already filtered
        // this and the lab did not, so a refunded test could be collected, resulted and
        // verified. Two modules reading the same rows now apply the same rule.
        var orderTests = await s.Diag.OrderTests.AsNoTracking()
            .Where(ot => orderIds.Contains(ot.TestOrderId) && !ot.Refunded).ToListAsync(ct);
        var orderTestIds = orderTests.Select(ot => ot.Id).ToList();

        var catalogIds = orderTests.Select(ot => ot.TestCatalogId).Distinct().ToList();
        var catalog = await s.Adm.TestCatalog.AsNoTracking()
            .Where(t => catalogIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        var names = catalog.ToDictionary(kv => kv.Key, kv => kv.Value.Name);

        var results = await s.Lis.Results.AsNoTracking()
            .Where(r => orderTestIds.Contains(r.OrderTestId)).ToListAsync(ct);

        var sampleTests = await s.Lis.SampleTests.AsNoTracking()
            .Where(st => orderTestIds.Contains(st.OrderTestId)).ToListAsync(ct);
        var sampleIds = sampleTests.Select(st => st.SampleId).Distinct().ToList();
        var samples = await s.Lis.Samples.AsNoTracking()
            .Where(x => sampleIds.Contains(x.Id)).ToListAsync(ct);

        var delivered = await s.Diag.Deliveries.AsNoTracking()
            .Where(d => orderIds.Contains(d.TestOrderId))
            .Select(d => d.TestOrderId).ToListAsync(ct);

        var patientIds = orders.Select(o => o.PatientId).Distinct().ToList();
        var patients = await s.Reg.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var invoiceIds = orders.Where(o => o.InvoiceId is not null).Select(o => o.InvoiceId!.Value).ToList();
        var dues = await s.Bill.Dues.AsNoTracking()
            .Where(d => invoiceIds.Contains(d.InvoiceId))
            .ToDictionaryAsync(d => d.InvoiceId, d => d.Balance, ct);

        var cards = new List<LabCard>(orders.Count);
        foreach (var order in orders)
        {
            var mine = orderTests.Where(ot => ot.TestOrderId == order.Id).ToList();
            var myIds = mine.Select(ot => ot.Id).ToHashSet();

            var tests = mine.Select(ot =>
            {
                var latest = results.Where(r => r.OrderTestId == ot.Id)
                    .OrderByDescending(r => r.Version).FirstOrDefault();
                return new LabTest(ot.Id, ot.TestCatalogId,
                    names.GetValueOrDefault(ot.TestCatalogId, "Test"),
                    latest is not null, latest?.VerifiedAt is not null);
            }).ToList();

            var mySampleIds = sampleTests.Where(st => myIds.Contains(st.OrderTestId))
                .Select(st => st.SampleId).Distinct().ToHashSet();
            var mySamples = samples.Where(x => mySampleIds.Contains(x.Id))
                .Select(x => new LabSample(x.Id, x.Barcode, x.SampleType, x.State)).ToList();

            var patient = patients.GetValueOrDefault(order.PatientId);
            var due = order.InvoiceId is { } inv ? dues.GetValueOrDefault(inv) : 0;

            cards.Add(new LabCard(
                order.Id, "LB-" + order.Id.ToString("D5"),
                patient?.FullName ?? "(unknown)", patient?.Uhid ?? "—", patient?.Phone,
                patient?.Sex ?? 'O', AgeYearsOf(patient),
                tests, mySamples,
                StageOf(order.Id, tests, mySamples, delivered),
                due, order.PromisedAt, order.CreatedAt,
                mine.Select(ot => catalog.TryGetValue(ot.TestCatalogId, out var c) ? c.Dept : "Other")
                    .Distinct().OrderBy(d => d).ToList()));
        }
        return cards;
    }

    /// <summary>DOB wins when present (02 §2.2); reference bands need a number, not a date.</summary>
    private static short? AgeYearsOf(Hms.Registration.Data.Patient? p)
    {
        if (p is null) return null;
        if (p.Dob is { } dob)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var years = today.Year - dob.Year;
            if (today < dob.AddYears(years)) years--;
            return (short)Math.Max(0, years);
        }
        return p.AgeYears;
    }

    private static string StageOf(
        long orderId, IReadOnlyList<LabTest> tests, IReadOnlyList<LabSample> samples,
        IReadOnlyList<long> delivered)
    {
        if (delivered.Contains(orderId)) return Delivered;
        if (tests.Count > 0 && tests.All(t => t.Verified)) return Verified;
        if (tests.Any(t => t.HasResult)) return Resulted;

        // Imaging and cardiology take no tube, so they are ready for reporting immediately.
        if (samples.Count == 0) return Received;

        // The order is only as far along as its least-advanced tube.
        var order = new[] { SampleState.PendingCollection, SampleState.Collected, SampleState.Received };
        var least = samples
            .Select(x => Array.IndexOf(order, x.State))
            .Where(i => i >= 0)
            .DefaultIfEmpty(0)
            .Min();
        return order[least];
    }
}
