using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record DeviceRow(
    long Id, string Code, string Name, string? Location, string? SerialNo,
    int ExpectedIntervalMinutes, DateTimeOffset? LastSeenAt, int LastBatchPunches,
    bool Active, string Health);

/// <summary>
/// The attendance device registry (module PRD §7.4, G22; main PRD §13 I8).
/// <para>
/// The registry and its health, not a vendor protocol: §9A.3 excluded the live feed because nobody
/// has held the hardware, and hard rule 3 forbids asserting a protocol this project cannot verify.
/// A device or a site agent posts punches to <c>/api/hr/punches</c> with the key generated here; the
/// import path they land in is the one the CSV upload already uses.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.PolicyManage)]
public class DevicesModel(IHrTx tx, DeviceService devices) : HmsPageModel
{
    public IReadOnlyList<DeviceRow> Devices { get; private set; } = [];

    /// <summary>
    /// Shown exactly once, on the render after registration. Never stored in the clear and never
    /// recoverable — an operator who loses it registers a new key rather than reading the old one.
    /// </summary>
    public string? IssuedKey { get; private set; }

    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Location { get; set; }
    [BindProperty] public string? SerialNo { get; set; }
    [BindProperty] public int ExpectedIntervalMinutes { get; set; } = 60;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostRegisterAsync()
    {
        // A generated key, not one the operator invents: this is a machine credential and the
        // commonest way it goes wrong is somebody typing the device's name into the box.
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

        try
        {
            await tx.RunAsync(s => devices.RegisterAsync(
                s.Hr, s.Kernel, BranchId, new AttendanceDevice
                {
                    Code = (Code ?? "").Trim(),
                    Name = (Name ?? "").Trim(),
                    Location = Location,
                    SerialNo = SerialNo,
                    ExpectedIntervalMinutes = ExpectedIntervalMinutes,
                }, key, ActorId, ActorName));
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }

        await LoadAsync();
        IssuedKey = key;
        Toast($"{Code} registered — copy the key now, it is not shown again", "sensors");
        return Page();
    }

    public async Task<IActionResult> OnPostRetireAsync(long id)
    {
        try
        {
            await tx.RunAsync(s => devices.RetireAsync(
                s.Hr, s.Kernel, BranchId, id, ActorId, ActorName));
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }

        Toast("Deactivated — the punches it sent are kept", "sensors_off");
        return Redirect("/hr/devices");
    }

    private async Task LoadAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await tx.RunAsync(async s =>
        {
            Devices =
            [
                .. (await s.Hr.Devices.AsNoTracking()
                        .Where(d => d.BranchId == BranchId)
                        .OrderByDescending(d => d.Active).ThenBy(d => d.Code)
                        .ToListAsync())
                    .Select(d => new DeviceRow(
                        d.Id, d.Code, d.Name, d.Location, d.SerialNo, d.ExpectedIntervalMinutes,
                        d.LastSeenAt, d.LastBatchPunches, d.Active,
                        AttendanceDevice.HealthOf(d.LastSeenAt, d.ExpectedIntervalMinutes, now))),
            ];
        });
    }

    public static string HealthTone(string health) => health switch
    {
        DeviceHealth.Healthy => "good",
        DeviceHealth.Late => "warn",
        _ => "bad",
    };

    public static string Seen(DateTimeOffset? at) => at is { } seen
        ? Ui.DateLong(DateOnly.FromDateTime(Ui.Local(seen).DateTime)) + " " + Ui.Time(seen)
        : "never";
}
