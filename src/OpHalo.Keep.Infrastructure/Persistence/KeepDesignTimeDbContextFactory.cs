using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Foundation.Infrastructure.Services;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling when Keep entities are involved. Passes
/// the Keep.Infrastructure assembly so <see cref="OpHaloDbContext"/> discovers Keep EF
/// configurations without Foundation.Infrastructure taking a compile-time dependency on
/// Keep (architecture boundary: Foundation must not reference Keep).
///
/// Use this factory when generating migrations that include Keep tables:
///
///   dotnet ef migrations add [Name] \
///     --project src/OpHalo.Foundation.Infrastructure \
///     --startup-project src/OpHalo.Keep.Infrastructure \
///     --context OpHaloDbContext
///
///   dotnet ef database update \
///     --project src/OpHalo.Foundation.Infrastructure \
///     --startup-project src/OpHalo.Keep.Infrastructure \
///     --context OpHaloDbContext
///
/// Connection string via user secrets on OpHalo.Keep.Infrastructure:
///
///   dotnet user-secrets init --project src/OpHalo.Keep.Infrastructure
///   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." \
///     --project src/OpHalo.Keep.Infrastructure
///
/// Or via environment variable: ConnectionStrings__DefaultConnection=...
/// </summary>
public sealed class KeepDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OpHaloDbContext>
{
    public OpHaloDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<KeepDesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found. " +
                "Set it via user secrets on OpHalo.Keep.Infrastructure or the " +
                "ConnectionStrings__DefaultConnection environment variable.");

        var options = new DbContextOptionsBuilder<OpHaloDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__OpHaloMigrationsHistory");
                    npgsql.MigrationsAssembly(typeof(OpHaloDbContext).Assembly.FullName);
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OpHaloDbContext(
            options,
            new SystemClock(),
            [typeof(AssemblyMarker).Assembly]);
    }
}
