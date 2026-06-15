using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.SharedKernel.Abstractions;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Persistence;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public OpHaloDbContext CreateContext(IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<OpHaloDbContext>()
            .UseNpgsql(
                ConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__OpHaloMigrationsHistory");
                    npgsql.MigrationsAssembly(typeof(OpHaloDbContext).Assembly.FullName);
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OpHaloDbContext(options, clock ?? new FakeClock());
    }

    // Fixed clock so audit timestamp assertions are stable across all tests.
    public static readonly DateTime FixedNow = new(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow => FixedNow;
    }
}
