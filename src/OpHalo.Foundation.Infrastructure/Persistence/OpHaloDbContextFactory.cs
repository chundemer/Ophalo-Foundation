using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OpHalo.Foundation.Infrastructure.Services;

namespace OpHalo.Foundation.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling. Creates a Foundation-only model (no Keep
/// entities). Do NOT use this factory for migrations — it will generate spurious DROP
/// TABLE statements for all Keep tables.
///
/// CANONICAL MIGRATION COMMAND — always use KeepDesignTimeDbContextFactory instead:
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
/// Connection string for the Keep startup project:
///   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." \
///     --project src/OpHalo.Keep.Infrastructure
/// Or: ConnectionStrings__DefaultConnection=... (environment variable)
/// </summary>
public sealed class OpHaloDbContextFactory : IDesignTimeDbContextFactory<OpHaloDbContext>
{
    public OpHaloDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<OpHaloDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found. " +
                "Set it via user secrets on OpHalo.Foundation.Infrastructure or the " +
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

        return new OpHaloDbContext(options, new SystemClock());
    }
}
