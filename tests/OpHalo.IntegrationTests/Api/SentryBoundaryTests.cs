using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpHalo.Api.Diagnostics;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// GAP-039 / ADR-495 Batch 1. Proves the Sentry integration is wired into the real API host,
/// is a complete no-op without a DSN, and does not alter the established ProblemDetails/status
/// contract or the opaque health endpoints.
/// </summary>
public sealed class SentryBoundaryTests(KeepApiWebFactory factory) : IClassFixture<KeepApiWebFactory>
{
    [Fact]
    public void WithoutDsn_SdkIsNotInitialized()
    {
        var options = factory.Services.GetRequiredService<IOptions<SentryAspNetCoreOptions>>().Value;

        Assert.False(options.InitializeSdk);
        Assert.True(string.IsNullOrEmpty(options.Dsn));
    }

    [Fact]
    public void RequestContextEventProcessor_IsRegistered()
    {
        var processors = factory.Services.GetServices<ISentryEventProcessor>();

        Assert.Contains(processors, p => p is RequestContextSentryEventProcessor);
    }

    [Fact]
    public async Task ProblemDetailsContract_IsUnchanged_WithSentryInThePipeline()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/keep/public-intake/token/token_that_does_not_exist_in_db",
            new
            {
                customerName = "Bob Jones",
                customerPhone = "0499999999",
                description = "Leaking tap",
                serviceAddressLine1 = "1 Test St",
                serviceCity = "Springfield",
                serviceState = "IL",
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("keep.public_intake.unavailable", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_RemainOpaqueAndHealthy(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// Records the envelopes the Sentry SDK would transmit, so a test can inspect the final
/// serialized event that leaves the process.
/// </summary>
public sealed class RecordingSentryTransport : ITransport
{
    private readonly List<string> _payloads = new();

    public IReadOnlyList<string> Payloads
    {
        get { lock (_payloads) return _payloads.ToArray(); }
    }

    public Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        envelope.Serialize(stream, null!);
        lock (_payloads)
            _payloads.Add(Encoding.UTF8.GetString(stream.ToArray()));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Boots the real API host with a configured (dummy) DSN so the SDK initializes, and swaps in a
/// recording transport. No database container — the test-only failure endpoint touches no
/// dependencies.
/// </summary>
public sealed class SentryUnhandledCaptureFactory : WebApplicationFactory<Program>
{
    public readonly RecordingSentryTransport Transport = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=x;Timeout=2",
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Sentry:Dsn"] = "https://0123456789abcdef0123456789abcdef@o0.ingest.sentry.io/1",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.Configure<SentryAspNetCoreOptions>(options =>
            {
                options.Transport = Transport;
                options.FlushOnCompletedRequest = true;
                options.AutoRegisterTracing = false;
            });
        });
    }
}

/// <summary>
/// GAP-039 / ADR-495: proof that an actual unhandled failure is observed by the Sentry boundary
/// (redacted) while the API's existing 500/empty-body contract is unchanged.
/// </summary>
public sealed class SentryUnhandledCaptureTests(SentryUnhandledCaptureFactory factory)
    : IClassFixture<SentryUnhandledCaptureFactory>
{
    [Fact]
    public async Task UnhandledFailure_IsObservedRedacted_AndResponseContractUnchanged()
    {
        const string note = "jane.customer@example.com";

        // The API installs no exception-handler middleware, so an unhandled failure is not
        // rewritten into a ProblemDetails response — under the test host it propagates. Adding
        // the Sentry middleware must not change that: it captures and re-throws.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateClient().GetAsync($"/__test/unhandled?note={note}"));
        Assert.Contains("deliberate unhandled test failure", thrown.Message);

        var payload = await WaitForEnvelopeAsync();

        // Observed: the event for our unhandled exception reached the transport.
        Assert.Contains("InvalidOperationException", payload);
        // Redacted: neither the exception message nor the query note survived the scrubber.
        Assert.DoesNotContain(note, payload);
        Assert.DoesNotContain("deliberate unhandled test failure", payload);
        Assert.DoesNotContain("note=", payload);
        // Retained safe context.
        Assert.Contains("/__test/unhandled", payload);
    }

    private async Task<string> WaitForEnvelopeAsync()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var match = factory.Transport.Payloads.FirstOrDefault(p => p.Contains("\"type\":\"event\""));
            if (match is not null)
                return match;
            await Task.Delay(100);
        }

        Assert.Fail("No Sentry event envelope was recorded within the timeout.");
        return string.Empty;
    }
}
