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
        DateOnly? dob = null, bool unknown = false, short? months = null, string? guardian = null,
        string? bloodGroup = null, string? patientType = null) =>
        new(1, name, 'M', dob, age, months, false, phone, guardian, "Sylhet", null,
            bloodGroup, patientType, unknown, 7, "Jashim");

    /// <summary>Re-reads the row from its own connection — an assertion on the tracked entity
    /// would pass even if nothing had reached PostgreSQL.</summary>
    private async Task<Patient> ReloadAsync(long id)
    {
        await using var reg = CreateReg(null);
        return await reg.Patients.SingleAsync(p => p.Id == id);
    }

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

    /// <summary>
    /// M1-D1 / LC-REG-11. An infant has no whole year to give. §7 U13 says "8 months" is a legal
    /// thing to type, and <c>ParseAge</c> parses it, but the age was dropped between the page and
    /// the service: the guard refused the registration outright, and even a relaxed guard would
    /// have been refused by <c>ck_identity</c> at the row level. Paediatrics is not an edge case
    /// in a 50–300 bed hospital, and the operator's workarounds — a false DOB, or rounding to
    /// "1 year" — corrupt the clinical record and every age-banded lab reference range built on it.
    /// </summary>
    [Fact]
    public async Task Age_in_months_registers_and_persists()
    {
        var p = await RegisterAsync(Cmd("Shishu Miah", "01766-000008", months: 8));

        var row = await ReloadAsync(p.Id);
        Assert.Equal((short)8, row.AgeMonths);
        Assert.Null(row.AgeYears);
        Assert.Null(row.Dob);
        // An age without the date it was taken on is not an age (03 §2.2) — a months-only age
        // is stamped exactly as a years-only one is.
        Assert.NotNull(row.AgeAsOf);
    }

    /// <summary>
    /// LC-REG-11. DOB wins: when a date of birth is known the age columns must stay empty, or the
    /// record carries two answers to one question and display picks whichever it reads first.
    /// </summary>
    [Fact]
    public async Task Dob_wins_over_both_age_columns()
    {
        var dob = new DateOnly(1980, 3, 12);
        var p = await RegisterAsync(Cmd("Rafiq Ullah", "01766-000012", age: 45, months: 6, dob: dob));

        var row = await ReloadAsync(p.Id);
        Assert.Equal(dob, row.Dob);
        Assert.Null(row.AgeYears);
        Assert.Null(row.AgeMonths);
        Assert.Null(row.AgeAsOf);      // nothing was "taken on" a day; the DOB is absolute
    }

    /// <summary>
    /// LC-REG-11. Years and months are two components of one age, not two competing answers, so
    /// a caller that supplies both keeps both. Only a DOB clears them.
    /// </summary>
    [Fact]
    public async Task Years_and_months_together_are_both_kept()
    {
        var p = await RegisterAsync(Cmd("Chotto Mia", "01766-000018", age: 1, months: 6));

        var row = await ReloadAsync(p.Id);
        Assert.Equal((short)1, row.AgeYears);
        Assert.Equal((short)6, row.AgeMonths);
        Assert.NotNull(row.AgeAsOf);
    }

    /// <summary>
    /// LC-REG-12 / LC-REG-14. Guardian, blood group and patient type are captured on the ≤ 60-second
    /// screen and then never displayed back to the operator who typed them (guardian and patient
    /// type are read by no page at all), so nothing but a row assertion can prove they landed.
    /// </summary>
    [Fact]
    public async Task Guardian_blood_group_and_patient_type_persist()
    {
        var p = await RegisterAsync(Cmd("Rehana Akter", "01766-000014", age: 14,
            guardian: "Abdul Mannan (father)", bloodGroup: "B+", patientType: "corporate"));

        var row = await ReloadAsync(p.Id);
        Assert.Equal("Abdul Mannan (father)", row.Guardian);
        Assert.Equal("B+", row.BloodGroup);
        Assert.Equal("corporate", row.PatientType);
    }

    /// <summary>LC-REG-14: the default is "general", not null or empty.</summary>
    [Fact]
    public async Task Patient_type_defaults_to_general()
    {
        var p = await RegisterAsync(Cmd("Nurul Islam", "01766-000015", age: 30));
        Assert.Equal("general", (await ReloadAsync(p.Id)).PatientType);
    }
}
