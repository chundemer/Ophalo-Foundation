using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Foundation.Infrastructure.Security;
using OpHalo.SharedKernel.Results;
using System.Collections.Concurrent;
using System.Security.Cryptography;
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
/// Auth: tests seed real AccountSession rows and send cookie or Bearer headers.
/// No ICurrentUser override — the auth handler runs exactly as in production.
/// </summary>
public sealed class KeepApiWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine")
        .Build();

    /// <summary>
    /// Captures emails sent via IEmailSender during tests. Thread-safe.
    /// Inspect after calling /auth/signin to retrieve the magic link.
    /// </summary>
    public readonly CapturingEmailSender EmailSender = new();

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
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["App:OperatorBaseUrl"] = "https://app.test.ophalo.com",
                // Deterministic 32-byte test key for HmacKeepRequestListCursorProtector.
                // Must be base64-encoded. 32 zero bytes = 256-bit key, sufficient for HMAC-SHA256.
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32])
            });
        });

        // Replace IEmailSender with the capturing implementation so tests can read
        // the magic link without making real HTTP calls to Resend.
        builder.ConfigureServices(services =>
        {
            // Remove the real typed HttpClient registration for IEmailSender.
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }

    /// <summary>
    /// Seeds a real AccountSession row and returns the raw token.
    /// Pass the token as a cookie (<c>ophalo.sid=rawToken</c>) or Bearer header in tests.
    /// </summary>
    /// <param name="overrideCreatedAt">
    /// Set to a past date to produce a session whose ExpiresAtUtc (createdAt + 30 days)
    /// is in the past, simulating an expired session.
    /// </param>
    public async Task<string> SeedSessionAsync(
        Guid accountUserId,
        Guid accountId,
        SessionClientType clientType = SessionClientType.Browser,
        DateTime? overrideCreatedAt = null)
    {
        var now = overrideCreatedAt ?? DateTime.UtcNow;
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = SessionHasher.HashToken(rawToken);
        var session = AccountSession.Create(accountId, accountUserId, tokenHash, clientType, null, now, now.AddDays(30));

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.AccountSessions.Add(session);
        await db.SaveChangesAsync();
        return rawToken;
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

/// <summary>
/// Test double for IEmailSender. Stores sent emails in-memory so tests can read
/// the magic link URL without making real HTTP calls to Resend.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<CapturedEmail> _emails = new();

    public IReadOnlyList<CapturedEmail> SentEmails => _emails.ToArray();

    public Task<Result> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        _emails.Enqueue(new CapturedEmail(to, subject, htmlBody));
        return Task.FromResult(Result.Success());
    }

    public void Clear() => _emails.Clear();
}

/// <summary>
/// WebApplicationFactory with Classification=Pilot and MaxPilotAccounts=1.
/// Used by AuthStartPilotCapTests to exercise the pilot capacity gate.
/// </summary>
public sealed class PilotCapWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine")
        .Build();

    public readonly CapturingEmailSender EmailSender = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["SignupDefaults:Classification"] = "Pilot",
                ["SignupDefaults:TrialDurationDays"] = "30",
                ["SignupDefaults:MaxPilotAccounts"] = "1"
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

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

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

public sealed record CapturedEmail(string To, string Subject, string HtmlBody)
{
    /// <summary>
    /// Extracts the magic link href from the HTML body.
    /// Returns null if no href is found.
    /// </summary>
    public string? ExtractMagicLink()
    {
        const string hrefPrefix = "href=\"";
        var start = HtmlBody.IndexOf(hrefPrefix, StringComparison.Ordinal);
        if (start < 0) return null;
        start += hrefPrefix.Length;
        var end = HtmlBody.IndexOf('"', start);
        return end < 0 ? null : HtmlBody[start..end];
    }

    public string? ExtractCode()
    {
        var link = ExtractMagicLink();
        if (link is null) return null;
        const string codeParam = "code=";
        var idx = link.IndexOf(codeParam, StringComparison.Ordinal);
        return idx < 0 ? null : Uri.UnescapeDataString(link[(idx + codeParam.Length)..]);
    }

    public string? ExtractInviteToken()
    {
        var link = ExtractMagicLink();
        if (link is null) return null;
        const string tokenParam = "token=";
        var idx = link.IndexOf(tokenParam, StringComparison.Ordinal);
        return idx < 0 ? null : Uri.UnescapeDataString(link[(idx + tokenParam.Length)..]);
    }
}
