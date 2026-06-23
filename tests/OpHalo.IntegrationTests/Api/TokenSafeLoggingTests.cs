using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Infrastructure.Persistence;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Captures all log messages that pass the application's configured filters.
/// Thread-safe; shared across all loggers created from one provider instance.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get { lock (_messages) return [.._messages]; }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(_messages, categoryName);

    public void Dispose() { }

    private sealed class Logger(List<string> messages, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            lock (messages)
                messages.Add($"[{category}] {text}");
        }
    }
}

/// <summary>
/// Factory for bearer-token log safety proof (G8b).
/// Uses the standard "Testing" environment (HTTPS redirect and rate limiting disabled)
/// and adds a CapturingLoggerProvider so tests can assert no raw tokens appear in logs.
/// </summary>
public sealed class TokenSafeLoggingWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine")
        .Build();

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
                ["App:OperatorBaseUrl"] = "https://app.test.ophalo.com",
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32]),
            });
        });

        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(LogCapture);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IEmailSender>(new CapturingEmailSender());
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// Proves that raw bearer tokens do not appear in application logs for public-token routes
/// (GAP-013, G8b). Uses obviously unique sentinel tokens that would be unmistakable if logged.
/// </summary>
public sealed class TokenSafeLoggingTests(TokenSafeLoggingWebFactory factory)
    : IClassFixture<TokenSafeLoggingWebFactory>
{
    private const string PublicIntakeSentinel = "g8b_PUBLIC_INTAKE_TOKEN_SHOULD_NOT_APPEAR_XZQ1A2B3";
    private const string LegacyIntakeSentinel = "g8b_LEGACY_INTAKE_TOKEN_SHOULD_NOT_APPEAR_XZQ4C5D6";
    private const string PageTokenSentinel = "g8b_PAGE_TOKEN_SHOULD_NOT_APPEAR_XZQ7E8F9";
    private const string PageWriteSentinel = "g8b_PAGE_WRITE_TOKEN_SHOULD_NOT_APPEAR_XZQ0G1H2";

    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task PublicIntakeRoute_RawTokenDoesNotAppearInLogs()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync(
            $"/keep/public-intake/token/{PublicIntakeSentinel}",
            new { phoneNumber = "+15550000001" });

        var messages = factory.LogCapture.Messages;
        Assert.DoesNotContain(messages, m => m.Contains(PublicIntakeSentinel));
    }

    [Fact]
    public async Task LegacyContinuityIntakeRoute_RawTokenDoesNotAppearInLogs()
    {
        var client = CreateClient();

        // Route is not registered (/continuity alias is not in Program.cs); the 404 still
        // exercises the request pipeline and proves the path is not logged raw.
        await client.PostAsJsonAsync(
            $"/continuity/public-intake/token/{LegacyIntakeSentinel}",
            new { phoneNumber = "+15550000002" });

        var messages = factory.LogCapture.Messages;
        Assert.DoesNotContain(messages, m => m.Contains(LegacyIntakeSentinel));
    }

    [Fact]
    public async Task CustomerPageRoute_RawTokenDoesNotAppearInLogs()
    {
        var client = CreateClient();

        await client.GetAsync($"/keep/r/{PageTokenSentinel}");

        var messages = factory.LogCapture.Messages;
        Assert.DoesNotContain(messages, m => m.Contains(PageTokenSentinel));
    }

    [Fact]
    public async Task CustomerWriteRoute_RawTokenDoesNotAppearInLogs()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync(
            $"/keep/r/{PageWriteSentinel}/message",
            new { message = "hello" });

        var messages = factory.LogCapture.Messages;
        Assert.DoesNotContain(messages, m => m.Contains(PageWriteSentinel));
    }
}
