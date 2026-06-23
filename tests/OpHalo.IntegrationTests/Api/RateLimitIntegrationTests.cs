using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Proves public-intake rate limiting works correctly in a production-like host (session-log G8a).
/// Uses RateLimitWebFactory (loopback trusted) for 429 and partition isolation proofs.
/// </summary>
public sealed class RateLimitProofTests : IClassFixture<RateLimitWebFactory>, IAsyncLifetime
{
    private readonly RateLimitWebFactory _factory;
    private readonly HttpClient _client;

    public RateLimitProofTests(RateLimitWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PublicIntake_Returns429AtEleventhRequest()
    {
        // Use a unique CF IP so this test's bucket is isolated from others in the fixture.
        const string cfIp = "203.0.113.30";

        for (var i = 0; i < 10; i++)
        {
            var resp = await SendIntakeAsync(cfIp);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        var final = await SendIntakeAsync(cfIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, final.StatusCode);
    }

    [Fact]
    public async Task PublicIntake_DifferentTrustedIps_HaveIsolatedBuckets()
    {
        const string cfIp1 = "203.0.113.31";
        const string cfIp2 = "203.0.113.32";

        // Exhaust cfIp1's bucket.
        for (var i = 0; i <= 10; i++)
            await SendIntakeAsync(cfIp1);

        // cfIp2's first request must not be rate limited (separate partition).
        var resp = await SendIntakeAsync(cfIp2);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
    }

    private Task<HttpResponseMessage> SendIntakeAsync(string cfIp)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/keep/public-intake/token/rl-test-token");
        req.Headers.TryAddWithoutValidation("CF-Connecting-IP", cfIp);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return _client.SendAsync(req);
    }
}

/// <summary>
/// Proves spoof resistance: with no trusted proxies, different CF-Connecting-IP values
/// all share the same loopback remote-address bucket (session-log G8a).
/// </summary>
public sealed class RateLimitSpoofResistanceTests : IClassFixture<RateLimitNoTrustWebFactory>, IAsyncLifetime
{
    private readonly RateLimitNoTrustWebFactory _factory;
    private readonly HttpClient _client;

    public RateLimitSpoofResistanceTests(RateLimitNoTrustWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PublicIntake_UntrustedRemote_DifferentCfHeadersShareBucket()
    {
        // No trusted proxies → loopback remote is the partition key regardless of CF header.
        // 5 requests from spoofed CF=1.1.1.1 plus 5 from CF=2.2.2.2 = 10 from the loopback bucket.
        // The 11th request (CF=3.3.3.3) must hit 429 — the bucket is exhausted.

        for (var i = 0; i < 5; i++)
        {
            var resp = await SendIntakeAsync("1.1.1.1");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        for (var i = 0; i < 5; i++)
        {
            var resp = await SendIntakeAsync("2.2.2.2");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        var final = await SendIntakeAsync("3.3.3.3");
        Assert.Equal(HttpStatusCode.TooManyRequests, final.StatusCode);
    }

    private Task<HttpResponseMessage> SendIntakeAsync(string cfIp)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/keep/public-intake/token/spoof-test-token");
        req.Headers.TryAddWithoutValidation("CF-Connecting-IP", cfIp);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return _client.SendAsync(req);
    }
}
