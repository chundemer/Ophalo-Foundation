using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// WebApplicationFactory that boots the real API host against a Testcontainers
/// PostgreSQL database (build-log/014, ADR-058).
///
/// Configuration approach: ConfigureAppConfiguration injects the container connection
/// string BEFORE Program.cs reads it, so the fail-fast check and the DbContext factory
/// both see the test value — no service replacement needed for the DbContext.
///
/// Tests control auth state by setting CurrentUser before each request. The factory
/// re-evaluates it per DI scope so each HTTP call sees the current value.
/// </summary>
public sealed class KeepApiWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine")
        .Build();

    /// <summary>
    /// Set before making a request to control which ICurrentUser the service layer sees.
    /// Defaults to unauthenticated. Changes take effect on the next request scope.
    /// </summary>
    public ICurrentUser CurrentUser { get; set; } =
        new Foundation.Infrastructure.Security.AnonymousCurrentUser();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" causes Program.cs to skip UseHttpsRedirection.
        builder.UseEnvironment("Testing");

        // Inject the container connection string into configuration BEFORE Program.cs
        // reads it. The container is guaranteed to be started (InitializeAsync runs before
        // the host is first built via Services or CreateClient).
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString()
            });
        });

        // Replace only ICurrentUser — the DbContext picks up the test connection string
        // from configuration automatically via the Program.cs factory.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddScoped<ICurrentUser>(_ => CurrentUser);
        });
    }

    /// <summary>
    /// Drops and recreates the public schema, then runs all migrations.
    /// Called in InitializeAsync and available to tests that need a clean slate.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await db.Database.MigrateAsync();
    }

    /// <summary>Creates a scope for seeding data directly via OpHaloDbContext.</summary>
    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    public async Task InitializeAsync()
    {
        // Container must be started before the host is first built — ConfigureWebHost
        // calls GetConnectionString() lazily when the host builds inside ResetDatabaseAsync.
        await _container.StartAsync();
        await ResetDatabaseAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
