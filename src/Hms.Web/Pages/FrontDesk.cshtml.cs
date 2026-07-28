using Hms.Admin;
using Hms.Appointments.Data;
using Hms.Ipd;
using Hms.Ipd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages;

public sealed record EnquiryAdmission(
    string AdmissionNo, string State, string? Bed, string? Ward, string? Doctor,
    DateTimeOffset AdmittedAt, long Posted, long Accrued, long Advance, long SettledDue);
public sealed record VisitRow(string What, string Detail, DateTimeOffset At);
public sealed record DoctorToday(string Name, string? Room, int Booked, int Done, int Waiting);
public sealed record ClassRow(string Class, int Free, int Total);

/// <summary>
/// §5 M2: the hospital's answer window. Read-only composition over registration, IPD, billing
/// and appointments — US2.2's live bill estimate adds accrued-but-unposted bed days by
/// COMPUTING them (rate-resolved) without posting anything: the enquiry desk can never
/// change a folio, only describe it.
/// </summary>
[Authorize(Policy = Perm.IpdRead)]
public class FrontDeskModel(
    HmsTx tx, FolioService folios, RateResolver rates, TimeProvider clock) : HmsPageModel
{
    public long? PatientId { get; set; }
    public string? PatientName { get; private set; }
    public string? PatientUhid { get; private set; }
    public string? PatientPhone { get; private set; }
    public EnquiryAdmission? Admission { get; private set; }
    public long? AdmissionId { get; private set; }
    public IReadOnlyList<VisitRow> History { get; private set; } = [];
    public IReadOnlyList<DoctorToday> Doctors { get; private set; } = [];
    public IReadOnlyList<ClassRow> BedSummary { get; private set; } = [];

    /// <summary>
    /// Bed-days the patient has occupied for which no rate is effective (spec 0032 M2-D1).
    /// Reachable whenever a ward's tariff is provisional at go-live (edge 11), a rate plan has a
    /// gap, or a ward is priced from a date after a sitting patient was admitted. Non-empty means
    /// <see cref="EstimateTotal"/> is a floor, not a quote.
    /// </summary>
    public IReadOnlyList<DateOnly> UnpricedBedDays { get; private set; } = [];

    public bool EstimateIncomplete => UnpricedBedDays.Count > 0;

    public long EstimateTotal => Admission is null ? 0
        : Admission.Posted + Admission.Accrued - Admission.Advance + Admission.SettledDue;

    public async Task OnGetAsync(long? patientId)
    {
        PatientId = patientId;
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        await tx.RunAsync(async s =>
        {
            // --- today's doctors (US2.1's "when is the doctor available") ---
            var schedules = await s.Appt.Schedules.AsNoTracking().ToListAsync();
            var todays = await s.Appt.Appointments.AsNoTracking()
                .Where(a => a.OnDate == today).ToListAsync();
            Doctors = schedules.GroupBy(x => x.DoctorId).Select(g =>
            {
                var mine = todays.Where(a => a.DoctorId == g.Key).ToList();
                return new DoctorToday(g.First().DoctorName, g.First().Room,
                    mine.Count(a => a.State != AppointmentState.Cancelled),
                    mine.Count(a => a.State == AppointmentState.Done),
                    mine.Count(a => a.State is AppointmentState.Booked or AppointmentState.Arrived
                        or AppointmentState.InChamber));
            }).OrderBy(d => d.Name).ToList();

            // --- bed availability by class (US2.1's "is a cabin free?") ---
            var wards = await s.Ipd.Wards.AsNoTracking().Where(w => w.Active).ToListAsync();
            var beds = await s.Ipd.Beds.AsNoTracking().ToListAsync();
            BedSummary = wards.GroupBy(w => w.Class).Select(g =>
            {
                var classBeds = beds.Where(b => g.Select(w => w.Id).Contains(b.WardId)).ToList();
                return new ClassRow(g.Key,
                    classBeds.Count(b => b.State == BedState.Free), classBeds.Count);
            }).ToList();

            if (patientId is not long pid || pid == 0) return 0;

            var patient = await s.Reg.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid);
            if (patient is null) return 0;
            PatientName = patient.FullName;
            PatientUhid = patient.Uhid;
            PatientPhone = patient.Phone;

            // --- open admission + US2.2 estimate ---
            var admission = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => a.PatientId == pid
                            && a.State != AdmissionState.Discharged
                            && a.State != AdmissionState.Death
                            && a.State != AdmissionState.Absconded
                            && a.State != AdmissionState.Reserved)
                .OrderByDescending(a => a.Id).FirstOrDefaultAsync();
            if (admission is not null)
            {
                AdmissionId = admission.Id;
                var stay = await s.Ipd.BedStays.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.AdmissionId == admission.Id && x.ToAt == null);
                var bed = stay is null ? null
                    : await s.Ipd.Beds.AsNoTracking().SingleAsync(b => b.Id == stay.BedId);
                var ward = bed is null ? null
                    : await s.Ipd.Wards.AsNoTracking().SingleAsync(w => w.Id == bed.WardId);
                var doctor = admission.DoctorId is long did
                    ? (await s.Appt.Schedules.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.DoctorId == did))?.DoctorName
                    : null;

                var folio = await s.Ipd.Folios.AsNoTracking()
                    .SingleOrDefaultAsync(f => f.AdmissionId == admission.Id);
                long posted = 0, accrued = 0, advance = 0, settledDue = 0;
                if (folio is not null)
                {
                    posted = await s.Bill.ChargeLines.AsNoTracking()
                        .Where(c => c.FolioId == folio.Id && c.InvoiceId == null)
                        .SumAsync(c => (long?)c.Amount) ?? 0;
                    advance = await s.Bill.Receipts.AsNoTracking()
                        .Where(r => r.FolioId == folio.Id).SumAsync(r => (long?)r.Amount) ?? 0;
                    if (folio.SettlementInvoiceId is long inv)
                        settledDue = await s.Bill.Dues.AsNoTracking()
                            .Where(d => d.InvoiceId == inv).Select(d => d.Balance).FirstOrDefaultAsync();

                    // Accrued-but-unposted bed days: COMPUTED at today's rates, never posted.
                    // A day with no effective rate is NOT dropped silently (spec 0032 M2-D1) —
                    // it is counted, so the screen can say the quote is a floor rather than
                    // present a short number as if it were the answer.
                    if (folio.State == FolioState.Open)
                    {
                        var unposted = await folios.ComputeUnpostedBedDaysAsync(s.Ipd, admission.Id);
                        var priced = await rates.TotalOverDaysAsync(
                            s.Adm, "service", unposted.TariffServiceId, unposted.Dates);
                        accrued = priced.Total;
                        UnpricedBedDays = priced.Unpriced;
                    }
                }
                Admission = new EnquiryAdmission(admission.AdmissionNo, admission.State,
                    bed?.Code, ward?.Name, doctor, admission.AdmittedAt,
                    posted, accrued, advance, settledDue);
            }

            // --- visit history summary ([S] previous patient searching) ---
            var encounters = await s.Bill.Encounters.AsNoTracking()
                .Where(e => e.PatientId == pid).OrderByDescending(e => e.Id).Take(5).ToListAsync();
            var pastAdmissions = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => a.PatientId == pid && (a.State == AdmissionState.Discharged
                    || a.State == AdmissionState.Death || a.State == AdmissionState.Absconded))
                .OrderByDescending(a => a.Id).Take(5).ToListAsync();
            History = encounters
                .Select(e => new VisitRow(e.Type, $"visit on {e.OnDate:dd MMM yyyy}", e.CreatedAt))
                .Concat(pastAdmissions.Select(a => new VisitRow("IPD",
                    $"{a.AdmissionNo} — {Ui.Humanize(a.State)}", a.AdmittedAt)))
                .OrderByDescending(v => v.At).Take(8).ToList();
            return 0;
        });
    }
}
