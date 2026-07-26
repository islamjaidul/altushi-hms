using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hms.Integration.Tests;

/// <summary>
/// One real PostgreSQL per test collection (G7: SQLite-in-memory is banned for these tests —
/// the guarantees under test live in Postgres row locks and constraints).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17.5-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public KernelDbContext CreateKernelContext()
    {
        var opts = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new KernelDbContext(opts);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateKernelContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
