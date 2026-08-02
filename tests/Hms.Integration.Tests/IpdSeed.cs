using Hms.Ipd.Data;

namespace Hms.Integration.Tests;

/// <summary>
/// Valid parent rows for IPD child fixtures. Spec 0039 made the intra-schema FKs real
/// (folio → admission, bed_day → admission/bed), so a test can no longer invent an admission
/// id — it seeds the admission. Each call uses a fresh patient id because
/// <c>ux_admission_open_per_patient</c> allows exactly one open stay per patient.
/// </summary>
internal static class IpdSeed
{
    public static async Task<Admission> OpenAdmissionAsync(IpdDbContext ipd, long branchId = 1)
    {
        var stamp = Random.Shared.NextInt64(1_000_000, 9_000_000);
        var admission = new Admission
        {
            BranchId = branchId, AdmissionNo = $"ADM-T{stamp}", PatientId = stamp,
            Source = "direct", AdmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
        };
        ipd.Admissions.Add(admission);
        await ipd.SaveChangesAsync();
        return admission;
    }

    public static async Task<Bed> BedAsync(IpdDbContext ipd, long branchId = 1)
    {
        var stamp = Random.Shared.NextInt64(1_000_000, 9_000_000);
        var ward = new Ward { BranchId = branchId, Name = $"Test Ward {stamp}", Class = "general" };
        ipd.Wards.Add(ward);
        await ipd.SaveChangesAsync();
        var bed = new Bed
        {
            BranchId = branchId, WardId = ward.Id, Code = $"T-{stamp}", TariffServiceId = 1,
        };
        ipd.Beds.Add(bed);
        await ipd.SaveChangesAsync();
        return bed;
    }
}
