using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpHalo.Api.Diagnostics;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.SharedKernel.Results;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Always reports a Resend-style delivery rejection, exercising the previously-discarded
/// Result.Failure branch in StartAuthService/SignInAuthService (GAP-039a).
/// </summary>
public sealed class FailingEmailSender : IEmailSender
{
    public Task<Result> SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(Error.Create("Email.DeliveryFailed", "Email delivery failed.")));
}

public sealed class FailingEmailWebFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine").Build();

    public readonly CapturingLoggerProvider LogCapture = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32]),
            });
        });

        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));

        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IEmailSender, FailingEmailSender>();
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// GAP-039a: a rejected Resend call must be logged with the auth-code ID, and must never
/// change the public response — /auth/start stays customer-neutral even when delivery fails.
/// </summary>
public sealed class AuthEmailFailureLoggingTests(FailingEmailWebFactory factory)
    : IClassFixture<FailingEmailWebFactory>
{
    private const string Email = "gap039-delivery-failure@example.com";

    [Fact]
    public async Task Start_WithFailingEmailProvider_StillReturnsNeutralSuccess()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/start", new
        {
            email = Email,
            businessName = "GAP-039 Test Co",
            name = "Test Owner",
            timeZone = "America/Chicago"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Start_WithFailingEmailProvider_LogsFailureWithCodeIdNotEmail()
    {
        factory.LogCapture.Clear();

        await factory.CreateClient().PostAsJsonAsync("/auth/start", new
        {
            email = Email,
            businessName = "GAP-039 Test Co",
            name = "Test Owner",
            timeZone = "America/Chicago"
        });

        var messages = factory.LogCapture.Messages;
        Assert.Contains(messages, m => m.Contains("magic link email delivery failed", StringComparison.OrdinalIgnoreCase)
            && m.Contains("Email.DeliveryFailed"));
        Assert.DoesNotContain(messages, m => m.Contains(Email));
    }

    [Fact]
    public async Task Start_WithFailingEmailProvider_LogsTheSpecificAuthCodeId()
    {
        factory.LogCapture.Clear();

        await factory.CreateClient().PostAsJsonAsync("/auth/start", new
        {
            email = Email,
            businessName = "GAP-039 Test Co",
            name = "Test Owner",
            timeZone = "America/Chicago"
        });

        var messages = factory.LogCapture.Messages;
        // "for code <guid>:" — proves the actual auth-code ID is logged, not just the error code.
        Assert.Contains(messages, m => Regex.IsMatch(
            m,
            @"for code [0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}:"));
    }

    [Fact]
    public async Task Start_WithFailingEmailProvider_LoggedCorrelationIdMatchesResponseHeader()
    {
        factory.LogCapture.Clear();

        var response = await factory.CreateClient().PostAsJsonAsync("/auth/start", new
        {
            email = Email,
            businessName = "GAP-039 Test Co",
            name = "Test Owner",
            timeZone = "America/Chicago"
        });

        var headerCorrelationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));

        var messages = factory.LogCapture.Messages;
        Assert.Contains(messages, m => m.Contains($"CorrelationId={headerCorrelationId}"));
    }

    [Fact]
    public async Task Start_WithFailingEmailProvider_LogEntryCarriesReleaseId()
    {
        factory.LogCapture.Clear();

        await factory.CreateClient().PostAsJsonAsync("/auth/start", new
        {
            email = Email,
            businessName = "GAP-039 Test Co",
            name = "Test Owner",
            timeZone = "America/Chicago"
        });

        var messages = factory.LogCapture.Messages;
        Assert.Contains(messages, m => m.Contains($"ReleaseId={ReleaseIdentity.Current}"));
    }
}
