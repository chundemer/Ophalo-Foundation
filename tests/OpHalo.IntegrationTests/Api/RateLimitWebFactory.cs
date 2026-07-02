using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Production-like factory for rate limit proof tests (session-log G8a).
/// Uses RateLimitTesting environment: HTTPS redirect disabled, rate limiting enabled.
/// Trusts IPv4 and IPv6 loopback so TestServer requests simulate a trusted proxy.
/// </summary>
public sealed class RateLimitWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine")
        .Build();

    public readonly CapturingEmailSender EmailSender = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("RateLimitTesting");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Edge:TrustedProxyCidrs:0"] = "127.0.0.1/32",
                ["Edge:TrustedProxyCidrs:1"] = "::1/128",
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await db.Database.MigrateAsync();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ResetDatabaseAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// Production-like factory for spoof-resistance rate limit tests (session-log G8a).
/// Uses RateLimitTesting environment with no trusted proxies — forwarded headers are ignored
/// and all TestServer requests are partitioned by the loopback remote address.
/// </summary>
public sealed class RateLimitNoTrustWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine")
        .Build();

    public readonly CapturingEmailSender EmailSender = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("RateLimitTesting");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32]),
                // No Edge:TrustedProxyCidrs — forwarded headers from any remote are ignored.
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await db.Database.MigrateAsync();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ResetDatabaseAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
