using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpHalo.Api.Diagnostics;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// GAP-039a: liveness/readiness endpoints and the server-generated correlation ID.
/// Response bodies are asserted to stay minimal — no dependency names, config values, or
/// exception detail belong in a public, unauthenticated health response.
/// </summary>
public sealed class HealthEndpointsTests(KeepApiWebFactory factory) : IClassFixture<KeepApiWebFactory>
{
    private HttpClient CreateClient() => factory.CreateClient();

    [Fact]
    public async Task Live_ReturnsMinimalHealthyBody()
    {
        var response = await CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_ReturnsMinimalHealthyBodyWhenDatabaseReachable()
    {
        var response = await CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Response_CarriesServerGeneratedCorrelationId()
    {
        var response = await CreateClient().GetAsync("/health/live");

        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        var id = Assert.Single(values!);
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task Response_IgnoresClientSuppliedCorrelationId()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "client-supplied-value");

        var response = await CreateClient().SendAsync(request);

        var id = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.NotEqual("client-supplied-value", id);
    }
}

/// <summary>
/// Points at a connection string with no listener (loopback, closed port) so the readiness check
/// fails deterministically without needing Testcontainers/Docker — no container is started.
/// </summary>
public sealed class UnreachableDatabaseWebFactory : WebApplicationFactory<Program>
{
    public readonly CapturingLoggerProvider LogCapture = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=nope;Username=x;Password=x;Timeout=2",
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32]),
            });
        });

        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));
    }
}

/// <summary>
/// GAP-039a: /health/ready must report the outage as a plain unhealthy status while still
/// logging enough internally (redacted) to diagnose it.
/// </summary>
public sealed class HealthReadyUnavailableTests(UnreachableDatabaseWebFactory factory)
    : IClassFixture<UnreachableDatabaseWebFactory>
{
    [Fact]
    public async Task Ready_ReturnsUnhealthyWithMinimalBody_WhenDatabaseUnreachable()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("{\"status\":\"unhealthy\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_WhenDatabaseUnreachable_LogsFailureInternallyWithoutConnectionDetail()
    {
        factory.LogCapture.Clear();

        await factory.CreateClient().GetAsync("/health/ready");

        var messages = factory.LogCapture.Messages;
        Assert.Contains(messages, m => m.Contains("Database readiness check failed"));
        Assert.DoesNotContain(messages, m => m.Contains("127.0.0.1") || m.Contains("Password"));
    }
}
