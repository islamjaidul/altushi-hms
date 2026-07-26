using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Registration;
using Hms.Registration.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

[Collection("postgres")]
public class RegistrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RegistrationService _svc = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System), TimeProvider.System);

    public RegistrationTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var reg = CreateReg(null);
        // Applying this migration IS the dmetaphone-immutability proof (spec 0005 conflict flag 1):
        // a non-immutable function in a GENERATED column fails at DDL time.
        await reg.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private RegDbContext CreateReg(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<RegDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "reg"))
            : new DbContextOptionsBuilder<RegDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "reg")))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    private async Task<Patient> RegisterAsync(RegisterPatientCommand cmd)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var reg = CreateReg(conn);
        await using var kernel = CreateKernel(conn);
        await reg.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var p = await _svc.RegisterAsync(reg, kernel, cmd);
        await tx.CommitAsync();
        return p;
    }

    private static RegisterPatientCommand Cmd(string name, string? phone = null, short? age = null,
        DateOnly? dob = null, bool unknown = false) =>
        new(1, name, 'M', dob, age, false, phone, null, "Sylhet", null, unknown, 7, "Jashim");

    [Fact]
    public async Task Registration_issues_sequential_uhids_and_audits()
    {
        var a = await RegisterAsync(Cmd("Abdul Karim", "01711-000001", 40));
        var b = await RegisterAsync(Cmd("Rahima Begum", "01711-000002", 33));
        Assert.StartsWith("ALT-", a.Uhid);
        Assert.NotEqual(a.Uhid, b.Uhid);

        await using var kernel = CreateKernel(new NpgsqlConnection(_pg.ConnectionString));
        await kernel.Database.OpenConnectionAsync();
        Assert.True(await kernel.AuditEvents.AnyAsync(e => e.Entity == "reg.patient" && e.EntityId == a.Id));
    }

    [Fact]
    public async Task Phonetic_duplicate_warning_catches_spelling_variants()
    {
        await RegisterAsync(Cmd("Mohammed Rahman", "01722-111111", 50));

        await using var reg = CreateReg(null);
        // Different spelling, same phonetics, adjacent age, different phone → still flagged
        var candidates = await _svc.FindDuplicatesAsync(reg, "Muhammad Rahman", "01799-999999", 51);
        Assert.Contains(candidates, c => c.FullName == "Mohammed Rahman");

        // Different phone AND different name → clean
        var clean = await _svc.FindDuplicatesAsync(reg, "Shafiqul Bari", "01733-222222", 28);
        Assert.DoesNotContain(clean, c => c.FullName == "Mohammed Rahman");
    }

    [Fact]
    public async Task Same_phone_flags_duplicate_regardless_of_name()
    {
        await RegisterAsync(Cmd("Kamal Hossain", "01755-555555", 45));
        await using var reg = CreateReg(null);
        var candidates = await _svc.FindDuplicatesAsync(reg, "Completely Different", "01755-555555", 20);
        Assert.Contains(candidates, c => c.Phone == "01755-555555");
    }

    [Fact]
    public async Task Unknown_emergency_registers_without_identity()
    {
        var p = await RegisterAsync(Cmd("", phone: null, age: null, unknown: true));   // edge 25
        Assert.StartsWith("UNKNOWN", p.FullName);
        Assert.True(p.UnknownIdentity);
        Assert.NotEmpty(p.Uhid);                       // money can flow; identity completed later
    }

    [Fact]
    public async Task Missing_identity_without_unknown_flag_is_refused()
        => await Assert.ThrowsAsync<ArgumentException>(
            () => RegisterAsync(Cmd("Ghost Patient")));   // no DOB, no age, not unknown

    [Fact]
    public async Task No_phone_registration_succeeds()
    {
        var p = await RegisterAsync(Cmd("Fatema Khatun", phone: null, age: 60));       // edge 24
        Assert.Null(p.Phone);
    }
}
