using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OpHalo.Foundation.Infrastructure.Services;

namespace OpHalo.Foundation.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling (dotnet ef migrations). Not used at runtime —
/// host DI registration is deferred to the auth phase, when the first endpoint consumes
/// the context. This factory reads its own configuration so migrations need no host wiring.
///
/// Loads configuration in priority order:
///   appsettings.json → appsettings.{Environment}.json → user secrets → environment variables
///
/// User secrets are scoped to OpHalo.Foundation.Infrastructure only:
///
///   dotnet user-secrets init --project src/OpHalo.Foundation.Infrastructure
///   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project src/OpHalo.Foundation.Infrastructure
///
/// Alternatively supply the connection string via environment variable:
///   ConnectionStrings__DefaultConnection=...
///
/// Migration commands (no --startup-project required):
///   dotnet ef migrations add [Name] --project src/OpHalo.Foundation.Infrastructure --context OpHaloDbContext
///   dotnet ef database update       --project src/OpHalo.Foundation.Infrastructure --context OpHaloDbContext
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
