using System.Security.Cryptography;
using System.Text;
using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// The attendance device registry (module PRD §7.4, G22; main PRD §13 I8).
/// <para>
/// <b>The registry, not the protocol.</b> §9A.3 excluded the live feed because the devices are not in
/// the room, and hard rule 3 forbids asserting a vendor protocol this project has not verified
/// against real hardware. What ships is the registry, the health surface, and an authenticated
/// endpoint a device or a site agent posts punches to. A vendor-specific poller is a spec written
/// with a device on the desk.
/// </para>
/// </summary>
public sealed class DeviceService(AuditWriter audit, TimeProvider clock)
{
    public async Task<AttendanceDevice> RegisterAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, AttendanceDevice draft, string pushKey,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Code))
            throw new HrException("Give the device a code — it is what a punch file names.");
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new HrException("Give the device a name an operator would recognise.");
        if (pushKey.Length < 16)
            throw new HrException(
                "The push key must be at least 16 characters. It is the only thing standing between "
                + "the endpoint and anybody who knows the device code.");
        if (draft.ExpectedIntervalMinutes is < 1 or > 10_080)
            throw new HrException("A device reports somewhere between every minute and every week.");

        var clash = await hr.Devices.AsNoTracking()
            .AnyAsync(d => d.BranchId == branchId && d.Code == draft.Code, ct);
        if (clash) throw new HrException($"A device with code {draft.Code} is already registered.");

        draft.BranchId = branchId;
        draft.PushKeyHash = HashKey(pushKey);
        draft.CreatedAt = clock.GetUtcNow();
        draft.CreatedBy = actorId;
        hr.Devices.Add(draft);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.device.register",
            "hr.attendance_device", draft.Id,
            after: new { draft.Code, draft.Name, draft.Location, draft.ExpectedIntervalMinutes },
            tier: 2);

        return draft;
    }

    public async Task RetireAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long deviceId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var device = await hr.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct)
                     ?? throw new HrException("That device is no longer registered.");

        // Deactivated, never deleted: the punches it sent are still attributed to it, and a device
        // row that vanishes takes the meaning of those punches with it.
        device.Active = false;

        audit.Append(kernel, branchId, actorId, actorName, "hr.device.retire",
            "hr.attendance_device", deviceId, before: new { device.Code, device.Name }, tier: 2);
    }

    /// <summary>
    /// Accepts a batch a device or a site agent posted. Authenticated by the device's own key, so a
    /// clock on the third floor cannot post as the one at the gate.
    /// <para>
    /// Returns the device so the caller can hand the rows to <c>AttendanceService.ImportAsync</c> —
    /// this service does not parse punches, because the import path already does and there must be
    /// exactly one of those.
    /// </para>
    /// </summary>
    public async Task<AttendanceDevice> AuthenticateAsync(
        HrDbContext hr, long branchId, string code, string pushKey, CancellationToken ct = default)
    {
        var device = await hr.Devices
            .FirstOrDefaultAsync(d => d.BranchId == branchId && d.Code == code && d.Active, ct)
            ?? throw new HrException("No active device is registered with that code.");

        if (device.PushKeyHash is null || !Verify(pushKey, device.PushKeyHash))
            throw new HrException("That key does not match the device.");

        return device;
    }

    /// <summary>Stamps a device as heard from. The whole point of the health surface.</summary>
    public void MarkHeard(AttendanceDevice device, int punches)
    {
        device.LastSeenAt = clock.GetUtcNow();
        device.LastBatchPunches = punches;
    }

    /// <summary>
    /// Devices that have gone quiet. Feeds the command centre, so a clock that stopped reporting on
    /// Tuesday is noticed on Tuesday rather than at payroll.
    /// </summary>
    public async Task<IReadOnlyList<(AttendanceDevice Device, string Health)>> SilentAsync(
        HrDbContext hr, long branchId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var devices = await hr.Devices.AsNoTracking()
            .Where(d => d.BranchId == branchId && d.Active)
            .ToListAsync(ct);

        return
        [
            .. devices
                .Select(d => (d, AttendanceDevice.HealthOf(d.LastSeenAt, d.ExpectedIntervalMinutes, now)))
                .Where(x => x.Item2 is DeviceHealth.Silent or DeviceHealth.NeverHeard)
                .OrderBy(x => x.d.Code),
        ];
    }

    /// <summary>
    /// Salted SHA-256. Not a password hash and not pretending to be one: this is a machine
    /// credential the operator generates and pastes into a device's configuration, so it has no
    /// human-memorable entropy to protect and no login rate to slow down. The salt stops one
    /// employer's stolen database revealing that two branches share a key.
    /// </summary>
    public static string HashKey(string key)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(key)]);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public static bool Verify(string key, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            var actual = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(key)]);
            // Fixed-time, so a wrong key cannot be narrowed down by how long the refusal took.
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
