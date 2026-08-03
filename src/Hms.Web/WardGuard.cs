using Hms.Emr;
using Hms.Ipd.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// Spec 0042: the admission-state gate for ward writes. The emr module cannot look at
/// ipd.admission (ADR-0003), so before 0042 every ward write accepted any admission id in any
/// state — a task on a discharged patient, a prescription on a dead one. The check belongs here,
/// at the composition root, for the same reason <see cref="IpdBilling"/> does.
///
/// Two levels on purpose. NEW clinical work (a task, a scheduled dose, a prescription) needs a
/// <b>live</b> admission. CLOSE-OUT actions (record a dose outcome, complete or cancel a task)
/// need only an <b>existing</b> one — after the 0042 audit, closing open work on a discharged or
/// deceased patient is exactly the path that must keep working, with the person's reason on it.
///
/// Throws <see cref="EmrException"/> because every ward page's error contract already catches it.
/// </summary>
public static class WardGuard
{
    /// <summary>The admission, whatever its state — for reading a record and closing open items.</summary>
    public static async Task<Admission> RequireAsync(
        TxScope s, long? admissionId, CancellationToken ct = default)
    {
        if (admissionId is not { } id)
            throw new EmrException("Which patient? Pick one from the ward list.");
        // The branch query filter applies here, so a cross-branch id reads as absent — the
        // caller learns nothing about other branches' admissions (guardrails §2).
        return await s.Ipd.Admissions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
               ?? throw new EmrException("No such admission.");
    }

    /// <summary>The admission, required in-house — for creating new clinical work.</summary>
    public static async Task<Admission> RequireLiveAsync(
        TxScope s, long? admissionId, CancellationToken ct = default)
    {
        var admission = await RequireAsync(s, admissionId, ct);
        if (admission.State is not (AdmissionState.Admitted
            or AdmissionState.DischargeInitiated or AdmissionState.Blocked))
            throw new EmrException(
                "This admission is closed — the record is read-only except for closing open items.");
        return admission;
    }

    public static bool IsLive(string state) => state is AdmissionState.Admitted
        or AdmissionState.DischargeInitiated or AdmissionState.Blocked;
}
