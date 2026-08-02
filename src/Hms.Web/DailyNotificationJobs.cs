using System.Text.Json;
using Hms.Billing;
using Hms.Kernel.Data;
using Hms.Kernel.Jobs;
using Hms.Kernel.Time;
using Hms.Notifications;
using Hms.Notifications.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

// Spec 0039 WP6 (AUD-XCUT-02): the two daily consumers the audit found dead — due reminders
// (§5 M20 [M] trigger) and the M22 end-of-day digest (§5 M22 [M], US22.1). The scheduler turns
// a business-day rollover into kernel.job rows; the handlers below execute them through the
// same SmsSender/template gate every other message uses.
//
// TODO(PM, spec 0039 WP6): neither event has a template in SmsTemplates.Defaults yet — the
// PRD names the triggers but not the wording, and inventing message content is a product
// decision (hard rule 2). Until the PM supplies the two template bodies (and, for the digest,
// who receives it — Sms:DigestRecipient), these jobs run, compute their figures, and queue
// nothing: SmsSender drops events that have no enabled template, by design.

// The rollover watcher itself is Hms.Kernel.Jobs.DailyJobScheduler (kernel-only dependencies,
// so its compare-and-set is integration-tested); the handlers for the jobs it enqueues live
// here because they orchestrate across modules — composition-root work by ADR-0003.

/// <summary>Shared branch loop: these jobs speak for every branch, one branch scope at a time,
/// so the WP5 isolation filters do their normal work instead of being bypassed.</summary>
internal static class BranchLoop
{
    public static async Task ForEachBranchAsync(
        HmsTx tx, Func<Branch, Task> body, CancellationToken ct)
    {
        var branches = await tx.RunAsync(
            async s => await s.Kernel.Branches.AsNoTracking().Where(b => b.Active).ToListAsync(ct), ct);
        var restore = BranchScope.Current;
        try
        {
            foreach (var branch in branches)
            {
                ct.ThrowIfCancellationRequested();
                BranchScope.Current = branch.Id;
                await body(branch);
            }
        }
        finally
        {
            BranchScope.Current = restore;
        }
    }
}

/// <summary>
/// §5 M20 [M] "due reminder": one SMS per patient per business day, covering the patient's
/// whole outstanding balance at this branch. Queued through the template gate — no template,
/// no message (see the file-head TODO).
/// </summary>
public sealed class DueRemindersHandler(
    HmsTx tx, SmsQueue sms, OrgIdentity org, BusinessDayCalendar calendar) : IJobHandler
{
    public string Type => DailyJobScheduler.DueRemindersJobType;

    public async Task ExecuteAsync(IServiceProvider services, string payloadJson, CancellationToken ct)
    {
        var day = DateOnly.Parse(JsonSerializer.Deserialize<DailyJobPayload>(payloadJson)!.Day);
        var dayStartUtc = Ui.DhakaMidnightUtc(day).Add(calendar.Boundary.ToTimeSpan());

        await BranchLoop.ForEachBranchAsync(tx, branch => tx.RunAsync(async s =>
        {
            // bill.due has no branch column; joining through the branch-filtered invoice set
            // scopes it (and keys the balance to a patient).
            var dues = await s.Bill.Dues.AsNoTracking()
                .Where(d => d.Balance > 0).ToListAsync(ct);
            if (dues.Count == 0) return;

            var invoiceIds = dues.Select(d => d.InvoiceId).ToList();
            var patientByInvoice = await s.Bill.Invoices.AsNoTracking()
                .Where(i => invoiceIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.PatientId, ct);

            var owedByPatient = dues
                .Where(d => patientByInvoice.ContainsKey(d.InvoiceId))
                .GroupBy(d => patientByInvoice[d.InvoiceId])
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Balance));
            if (owedByPatient.Count == 0) return;

            var patientIds = owedByPatient.Keys.ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id) && p.Active && p.MergedInto == null)
                .ToListAsync(ct);

            // One reminder per patient per business day, across restarts and re-runs.
            var remindedToday = (await s.Notif.Sms.AsNoTracking()
                    .Where(m => m.Event == SmsEvent.DueReminder && m.QueuedAt >= dayStartUtc
                                && m.Recipient != null)
                    .Select(m => m.Recipient!)
                    .ToListAsync(ct))
                .ToHashSet();

            foreach (var patient in patients)
            {
                if (string.IsNullOrWhiteSpace(patient.Phone) || remindedToday.Contains(patient.Phone))
                    continue;
                await SmsSender.SendAsync(s, sms, branch.Id, SmsEvent.DueReminder, patient.Phone,
                    new Dictionary<string, string?>
                    {
                        ["hospital"] = org.Name,
                        ["patient"] = patient.FullName,
                        ["amount"] = owedByPatient[patient.Id].ToString(),
                    }, ct);
            }
        }, ct), ct);
    }
}

/// <summary>
/// §5 M22 [M] / US22.1: the end-of-day digest — yesterday's money story per branch, computed
/// with the same <see cref="InvoiceValue"/> definitions the MD dashboard and the printed
/// day-close answer to (a reversed invoice earns nothing). Queued through the template gate to
/// the configured recipient (see the file-head TODO).
/// </summary>
public sealed class EodDigestHandler(
    HmsTx tx, SmsQueue sms, OrgIdentity org, BusinessDayCalendar calendar,
    IConfiguration config) : IJobHandler
{
    public string Type => DailyJobScheduler.EodDigestJobType;

    public async Task ExecuteAsync(IServiceProvider services, string payloadJson, CancellationToken ct)
    {
        var day = DateOnly.Parse(JsonSerializer.Deserialize<DailyJobPayload>(payloadJson)!.Day);
        var from = Ui.DhakaMidnightUtc(day).Add(calendar.Boundary.ToTimeSpan());
        var to = Ui.DhakaMidnightUtc(day.AddDays(1)).Add(calendar.Boundary.ToTimeSpan());

        // TODO(PM, spec 0039 WP6): who receives the digest is a product/deployment decision.
        // Until Sms:DigestRecipient is set (the MD's number), the digest computes and queues
        // nothing — same posture as a patient with no phone (edge 24).
        var recipient = config["Sms:DigestRecipient"];

        await BranchLoop.ForEachBranchAsync(tx, branch => tx.RunAsync(async s =>
        {
            var invoices = await s.Bill.Invoices.AsNoTracking()
                .Where(i => i.CreatedAt >= from && i.CreatedAt < to)
                .Select(i => new { i.Id, i.PatientId, i.State, i.Net, i.Discount })
                .ToListAsync(ct);
            var refunded = await InvoiceValue.RefundedByInvoiceAsync(
                s.Bill, invoices.Select(i => i.Id).ToList());
            var totals = InvoiceValue.Totalise(
                invoices.Select(i => new InvoiceMoneyRow(i.Id, i.PatientId, i.State, i.Net, i.Discount)),
                refunded);

            var collected = await s.Bill.Receipts.AsNoTracking()
                .Where(r => r.At >= from && r.At < to).SumAsync(r => (long?)r.Amount, ct) ?? 0;

            // bill.due has no branch column; membership in the branch-filtered invoice set scopes it.
            var dues = await s.Bill.Dues.AsNoTracking()
                .Where(d => d.Balance > 0).ToListAsync(ct);
            var dueInvoiceIds = dues.Select(d => d.InvoiceId).ToList();
            var branchDueIds = (await s.Bill.Invoices.AsNoTracking()
                .Where(i => dueInvoiceIds.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct)).ToHashSet();
            var outstanding = dues.Where(d => branchDueIds.Contains(d.InvoiceId)).Sum(d => d.Balance);

            await SmsSender.SendAsync(s, sms, branch.Id, SmsEvent.EodDigest, recipient,
                new Dictionary<string, string?>
                {
                    ["hospital"] = org.Name,
                    ["day"] = day.ToString("yyyy-MM-dd"),
                    ["income"] = totals.Income.ToString(),
                    ["collected"] = collected.ToString(),
                    ["due"] = outstanding.ToString(),
                    ["discount"] = totals.Discount.ToString(),
                }, ct);
        }, ct), ct);
    }
}
