using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;

namespace Hms.Hr;

/// <summary>What a device push produced, in the words the caller gets back.</summary>
public sealed record DevicePushResult(
    string DeviceCode, int Accepted, int Rejected, int Duplicates, string Message);

/// <summary>
/// The endpoint a device or a site agent posts punches to (main PRD §13 I8, module PRD §7.4 G22).
/// <para>
/// Deliberately <b>not</b> a vendor protocol. §9A.3 excluded the live feed because the hardware is
/// not in the room, and hard rule 3 forbids asserting a manufacturer's wire format this project has
/// never tested against. So: an authenticated POST carrying the same CSV the import screen accepts,
/// landing in the same <see cref="AttendanceService.ImportAsync"/> the upload uses — one parser, one
/// idempotency rule, one place for the pairing logic to live.
/// </para>
/// <para>
/// Hosted as a minimal API by each host rather than a Razor page: it is machine-to-machine, carries
/// no antiforgery token and no cookie, and authenticates on the device's own key.
/// </para>
/// </summary>
public sealed class DevicePushEndpoint(
    DeviceService devices, AttendanceService attendance, AuditWriter audit)
{
    /// <summary>The upper bound on one push. A device sending more than this is malfunctioning.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public async Task<DevicePushResult> PushAsync(
        HrScope scope, long branchId, string? deviceCode, string? deviceKey, Stream body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(deviceKey))
            throw new HrException("A push carries X-Device-Code and X-Device-Key.");

        var device = await devices.AuthenticateAsync(scope.Hr, branchId, deviceCode, deviceKey, ct);

        // The device's own id is the actor. A punch that arrived by machine must not be attributed
        // to whichever human happened to configure the device — the audit would name the wrong
        // person for every punch it ever sent.
        var result = await attendance.ImportAsync(
            scope.Hr, scope.Kernel, branchId,
            $"{device.Code} push {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}",
            new CsvPunchSource(), body, actorId: 0, actorName: $"device:{device.Code}", ct);

        devices.MarkHeard(device, result.Accepted);

        audit.Append(scope.Kernel, branchId, 0, $"device:{device.Code}", "hr.device.push",
            "hr.attendance_device", device.Id,
            after: new { device.Code, result.Accepted, result.Rejected, result.Duplicate },
            tier: 2);

        return new DevicePushResult(
            device.Code, result.Accepted, result.Rejected, result.Duplicate,
            $"{result.Accepted} punch(es) accepted from {device.Code}");
    }
}
