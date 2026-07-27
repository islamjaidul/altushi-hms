using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Ipd;

public sealed class IpdException(string message) : Exception(message);

/// <summary>
/// Admission and bed state machines (§11) under the ADR-0015 discipline: the bed row is
/// locked with SELECT … FOR UPDATE and moved by state-guarded UPDATEs, so two operators
/// claiming the last cabin serialize — the loser gets a comprehensible error, not a shared bed.
/// Money never lives here: folio charges are bill.* rows posted by the composition root.
/// </summary>
public sealed class IpdService(
    NumberSeriesService numbers, AuditWriter audit, FiscalCalendar fiscal, TimeProvider clock)
{
    private async Task<string> LockBedAsync(IpdDbContext ipd, long bedId, CancellationToken ct)
    {
        var states = await ipd.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM ipd.bed WHERE id = {bedId} FOR UPDATE
            """).ToListAsync(ct);
        if (states.Count == 0) throw new IpdException("Unknown bed.");
        return states[0];
    }

    private async Task MoveBedAsync(
        IpdDbContext ipd, long bedId, string from, string to, string? reason, CancellationToken ct)
    {
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.bed SET state = {to}, state_reason = {reason}
            WHERE id = {bedId} AND state = {from}
            """, ct);
        if (affected == 0)
            throw new IpdException("This bed just changed state — refresh the board and try again.");
    }

    // ---- admission ---------------------------------------------------------

    /// <summary>Admit (or reserve) into a bed. The bed must be Free, or Reserved when admitting
    /// a reservation. Creates admission + open bed stay + folio in one transaction.</summary>
    public async Task<Admission> AdmitAsync(
        IpdDbContext ipd, KernelDbContext kernel, long branchId, long patientId, long? doctorId,
        string source, string? provisionalDx, long bedId, long? packageId, short serviceChargePct,
        bool reserveOnly, long actorId, string actorName, CancellationToken ct = default)
    {
        var bedState = await LockBedAsync(ipd, bedId, ct);
        if (bedState != BedState.Free && !(bedState == BedState.Reserved && !reserveOnly))
            throw new IpdException($"Bed is {bedState.Replace('_', ' ')} — pick another on the board.");

        var openAdmission = await ipd.Admissions.AnyAsync(a => a.PatientId == patientId
            && a.State != AdmissionState.Discharged && a.State != AdmissionState.Death
            && a.State != AdmissionState.Absconded, ct);
        if (openAdmission)
            throw new IpdException("This patient already has an open admission.");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (_, admissionNo) = await numbers.IssueAsync(
            kernel, branchId, "admission", fiscal.FiscalYearOf(today), "ADM-{fy}-{n:D5}", ct);

        var now = clock.GetUtcNow();
        var admission = new Admission
        {
            BranchId = branchId,
            AdmissionNo = admissionNo,
            PatientId = patientId,
            DoctorId = doctorId,
            Source = source,
            ProvisionalDx = provisionalDx,
            PackageId = packageId,
            ServiceChargePct = serviceChargePct,
            State = reserveOnly ? AdmissionState.Reserved : AdmissionState.Admitted,
            AdmittedAt = now,
            CreatedAt = now,
            CreatedBy = actorId,
        };
        ipd.Admissions.Add(admission);
        await ipd.SaveChangesAsync(ct);

        await MoveBedAsync(ipd, bedId, bedState,
            reserveOnly ? BedState.Reserved : BedState.Occupied, null, ct);
        ipd.BedStays.Add(new BedStay { AdmissionId = admission.Id, BedId = bedId, FromAt = now });

        if (!reserveOnly)
            ipd.Folios.Add(new Folio
            {
                BranchId = branchId, AdmissionId = admission.Id, PatientId = patientId,
            });
        await ipd.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "ipd.admit", "ipd.admission", admission.Id,
            after: new { admissionNo, patientId, bedId, source, packageId, serviceChargePct, reserveOnly });
        await kernel.SaveChangesAsync(ct);
        return admission;
    }

    /// <summary>Turns a reservation into an admission (bed Reserved → Occupied, folio created).</summary>
    public async Task ConfirmReservationAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        var stay = await OpenStayAsync(ipd, admissionId, ct);
        var bedState = await LockBedAsync(ipd, stay.BedId, ct);
        if (bedState != BedState.Reserved) throw new IpdException("The reserved bed moved on — re-check.");

        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'admitted', admitted_at = {clock.GetUtcNow()}
            WHERE id = {admissionId} AND state = 'reserved'
            """, ct);
        if (affected == 0) throw new IpdException("This reservation was already decided — refresh.");

        await MoveBedAsync(ipd, stay.BedId, BedState.Reserved, BedState.Occupied, null, ct);
        ipd.Folios.Add(new Folio
        {
            BranchId = admission.BranchId, AdmissionId = admissionId, PatientId = admission.PatientId,
        });
        await ipd.SaveChangesAsync(ct);

        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.reservation.confirm", "ipd.admission", admissionId, after: new { stay.BedId });
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Cancels a reservation: bed frees, admission ends Absconded? No — a cancelled
    /// reservation never admitted anyone, so the admission row ends Discharged with a note.</summary>
    public async Task CancelReservationAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        var stay = await OpenStayAsync(ipd, admissionId, ct);
        await LockBedAsync(ipd, stay.BedId, ct);

        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'discharged', discharged_at = {clock.GetUtcNow()}
            WHERE id = {admissionId} AND state = 'reserved'
            """, ct);
        if (affected == 0) throw new IpdException("Only a reservation can be cancelled this way.");

        stay.ToAt = clock.GetUtcNow();
        await MoveBedAsync(ipd, stay.BedId, BedState.Reserved, BedState.Free, null, ct);
        await ipd.SaveChangesAsync(ct);

        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.reservation.cancel", "ipd.admission", admissionId, after: new { stay.BedId });
        await kernel.SaveChangesAsync(ct);
    }

    // ---- transfer (US6.3) --------------------------------------------------

    /// <summary>Both beds lock (in id order — no deadlock), history keeps the exact moment.
    /// Charging re-points from the first unposted bed day (P18 rule).</summary>
    public async Task TransferAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long toBedId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        if (admission.State != AdmissionState.Admitted
            && admission.State != AdmissionState.DischargeInitiated)
            throw new IpdException("Only an admitted patient can be transferred.");

        var stay = await OpenStayAsync(ipd, admissionId, ct);
        if (stay.BedId == toBedId) throw new IpdException("The patient is already in that bed.");

        foreach (var bedId in new[] { stay.BedId, toBedId }.OrderBy(x => x))
            await LockBedAsync(ipd, bedId, ct);

        var toState = await ipd.Beds.AsNoTracking()
            .Where(b => b.Id == toBedId).Select(b => b.State).SingleAsync(ct);
        if (toState != BedState.Free)
            throw new IpdException($"Target bed is {toState.Replace('_', ' ')} — pick another.");

        var now = clock.GetUtcNow();
        stay.ToAt = now;
        ipd.BedStays.Add(new BedStay { AdmissionId = admissionId, BedId = toBedId, FromAt = now });
        await MoveBedAsync(ipd, stay.BedId, BedState.Occupied, BedState.Cleaning, null, ct);
        await MoveBedAsync(ipd, toBedId, BedState.Free, BedState.Occupied, null, ct);
        await ipd.SaveChangesAsync(ct);

        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.transfer", "ipd.admission", admissionId, after: new { from = stay.BedId, to = toBedId });
        await kernel.SaveChangesAsync(ct);
    }

    // ---- bed housekeeping --------------------------------------------------

    public async Task CleaningDoneAsync(IpdDbContext ipd, long bedId, CancellationToken ct = default)
    {
        await LockBedAsync(ipd, bedId, ct);
        await MoveBedAsync(ipd, bedId, BedState.Cleaning, BedState.Free, null, ct);
    }

    public async Task SetOutOfServiceAsync(
        IpdDbContext ipd, long bedId, string reason, CancellationToken ct = default)
    {
        var state = await LockBedAsync(ipd, bedId, ct);
        if (state is BedState.Occupied or BedState.Reserved)
            throw new IpdException("An occupied or reserved bed cannot go out of service.");
        await MoveBedAsync(ipd, bedId, state, BedState.OutOfService, reason, ct);
    }

    public async Task ReturnToServiceAsync(IpdDbContext ipd, long bedId, CancellationToken ct = default)
    {
        await LockBedAsync(ipd, bedId, ct);
        await MoveBedAsync(ipd, bedId, BedState.OutOfService, BedState.Free, null, ct);
    }

    // ---- R4 block ⚿ ⇄ release ⚿ -------------------------------------------

    /// <summary>R4: due-hold. Approval-gated (§11 ⚿); admission and folio flip together.</summary>
    public async Task BlockAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long approvalId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        await VerifyApprovalAsync(kernel, approvalId, "patient-block", ct);
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        if (admission.State is not (AdmissionState.Admitted or AdmissionState.DischargeInitiated
            or AdmissionState.ClinicallyCleared))
            throw new IpdException("Only an in-house admission can be blocked.");

        var prior = admission.State;
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'blocked', blocked_from = {prior},
                block_approval_id = {approvalId}
            WHERE id = {admissionId} AND state = {prior}
            """, ct);
        if (affected == 0) throw new IpdException("The admission just changed state — refresh.");

        await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.folio SET state = 'blocked'
            WHERE admission_id = {admissionId} AND state = 'open'
            """, ct);

        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.block", "ipd.admission", admissionId, after: new { approvalId, prior }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    public async Task ReleaseAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long approvalId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        await VerifyApprovalAsync(kernel, approvalId, "patient-release", ct);
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        if (admission.State != AdmissionState.Blocked)
            throw new IpdException("This admission is not blocked.");

        var restore = admission.BlockedFrom ?? AdmissionState.Admitted;
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = {restore}, blocked_from = NULL
            WHERE id = {admissionId} AND state = 'blocked'
            """, ct);
        if (affected == 0) throw new IpdException("The admission just changed state — refresh.");

        await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.folio SET state = 'open'
            WHERE admission_id = {admissionId} AND state = 'blocked'
            """, ct);

        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.release", "ipd.admission", admissionId, after: new { approvalId, restore }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    // ---- discharge flow (§11) ----------------------------------------------

    public async Task InitiateDischargeAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, string clinicalSummary,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clinicalSummary))
            throw new IpdException("Discharge needs a clinical summary (§5 M6).");
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'discharge_initiated', clinical_summary = {clinicalSummary}
            WHERE id = {admissionId} AND state = 'admitted'
            """, ct);
        if (affected == 0)
            throw new IpdException("Discharge can only start on an admitted (unblocked) patient.");
        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.discharge.initiate", "ipd.admission", admissionId, after: null);
        await kernel.SaveChangesAsync(ct);
    }

    public async Task ClinicallyClearAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'clinically_cleared'
            WHERE id = {admissionId} AND state = 'discharge_initiated'
            """, ct);
        if (affected == 0) throw new IpdException("Discharge has not been initiated (or moved on).");
        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.discharge.clear", "ipd.admission", admissionId, after: null);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Called by settlement (composition root) inside the settlement transaction.</summary>
    public async Task MarkFinanciallySettledAsync(IpdDbContext ipd, long admissionId, CancellationToken ct = default)
    {
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'financially_settled'
            WHERE id = {admissionId} AND state = 'clinically_cleared'
            """, ct);
        if (affected == 0)
            throw new IpdException("Settlement needs a clinically-cleared admission (§11 order).");
    }

    /// <summary>The gate pass: patient leaves, bed goes to cleaning, stay closes.</summary>
    public async Task DischargeAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'discharged', discharged_at = {clock.GetUtcNow()}
            WHERE id = {admissionId} AND state = 'financially_settled'
            """, ct);
        if (affected == 0) throw new IpdException("Discharge needs a financially-settled admission.");
        await CloseStayAsync(ipd, admissionId, ct);
        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.discharge", "ipd.admission", admissionId, after: null);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Death: clinical exit at any in-house point; dues survive on the folio/invoice.</summary>
    public async Task RecordDeathAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'death', discharged_at = {clock.GetUtcNow()}
            WHERE id = {admissionId} AND state IN
                ('admitted', 'blocked', 'discharge_initiated', 'clinically_cleared')
            """, ct);
        if (affected == 0) throw new IpdException("This admission is not in-house.");
        await CloseStayAsync(ipd, admissionId, ct);
        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.death", "ipd.admission", admissionId, after: null, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Absconded: the patient left without settling; the folio stays for due follow-up.</summary>
    public async Task RecordAbscondedAsync(
        IpdDbContext ipd, KernelDbContext kernel, long admissionId, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var admission = await GetAdmissionAsync(ipd, admissionId, ct);
        var affected = await ipd.Database.ExecuteSqlAsync($"""
            UPDATE ipd.admission SET state = 'absconded', discharged_at = {clock.GetUtcNow()}
            WHERE id = {admissionId} AND state IN
                ('admitted', 'blocked', 'discharge_initiated', 'clinically_cleared')
            """, ct);
        if (affected == 0) throw new IpdException("This admission is not in-house.");
        await CloseStayAsync(ipd, admissionId, ct);
        audit.Append(kernel, admission.BranchId, actorId, actorName,
            "ipd.absconded", "ipd.admission", admissionId, after: null, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    // ---- helpers -----------------------------------------------------------

    private async Task CloseStayAsync(IpdDbContext ipd, long admissionId, CancellationToken ct)
    {
        var stay = await ipd.BedStays
            .SingleOrDefaultAsync(x => x.AdmissionId == admissionId && x.ToAt == null, ct);
        if (stay is null) return;
        await LockBedAsync(ipd, stay.BedId, ct);
        stay.ToAt = clock.GetUtcNow();
        await MoveBedAsync(ipd, stay.BedId, BedState.Occupied, BedState.Cleaning, null, ct);
        await ipd.SaveChangesAsync(ct);
    }

    private static async Task<Admission> GetAdmissionAsync(
        IpdDbContext ipd, long admissionId, CancellationToken ct)
        => await ipd.Admissions.SingleOrDefaultAsync(a => a.Id == admissionId, ct)
           ?? throw new IpdException("Unknown admission.");

    private static async Task<BedStay> OpenStayAsync(IpdDbContext ipd, long admissionId, CancellationToken ct)
        => await ipd.BedStays.SingleOrDefaultAsync(x => x.AdmissionId == admissionId && x.ToAt == null, ct)
           ?? throw new IpdException("No open bed stay for this admission.");

    /// <summary>Never trust a posted approval id (verify-don't-trust): it must exist, be
    /// approved, and be of the claimed type.</summary>
    public static async Task VerifyApprovalAsync(
        KernelDbContext kernel, long approvalId, string type, CancellationToken ct = default)
    {
        var ok = await kernel.ApprovalRequests.AsNoTracking().AnyAsync(r =>
            r.Id == approvalId && r.Type == type && r.State == ApprovalState.Approved, ct);
        if (!ok) throw new IpdException($"No approved '{type}' request found — approval is required.");
    }
}
