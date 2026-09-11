using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;

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

/// <summary>
/// Proves GET /keep/r/{pageToken} rate limiting (AUDIT-V6-A / F6.1): the composite IP+pageToken
/// "customer-write" partition (ADR-129) throttles correctly, stays isolated per token the way
/// every other {pageToken} route already does, and — the specific regression risk of adding a
/// limiter to a route with a side effect — the debounced CustomerPageLastViewedAtUtc write still
/// lands correctly on every request that stays within the limit.
/// </summary>
public sealed class CustomerPageRateLimitProofTests : IClassFixture<RateLimitWebFactory>, IAsyncLifetime
{
    private readonly RateLimitWebFactory _factory;
    private readonly HttpClient _client;

    public CustomerPageRateLimitProofTests(RateLimitWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Seeds a live KeepRequest under the given page token.</summary>
    private async Task SeedRequestAsync(string pageToken)
    {
        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: $"owner-{pageToken}@rate-limit-tests.com",
            name: "Rate Limit Test Owner",
            businessName: "Acme Plumbing",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));
        Assert.True(provisionResult.IsSuccess);
        var graph = provisionResult.Value;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(graph.Account.Id, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.CreateFromCustomerIntake(
            graph.Account.Id, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "PQRS7842", pageToken, now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, graph.Account.Id, now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetCustomerPage_DebouncesPageViewWriteWithinTheLimit()
    {
        const string pageToken = "rl-customer-page-within-limit";
        const string cfIp = "203.0.113.40";
        await SeedRequestAsync(pageToken);

        // First request, still within the 10/min limit: records the page view.
        var first = await SendCustomerPageAsync(pageToken, cfIp);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        DateTime? firstViewedAt;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var request = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(r => r.PageToken == pageToken);
            firstViewedAt = request.CustomerPageLastViewedAtUtc;
        }
        Assert.NotNull(firstViewedAt);

        // 9 more requests, still within the limit (10 total): the 5-minute debounce must still
        // hold under the limiter — none of these re-stamp the timestamp. Proves the limiter
        // addition didn't disturb the write path's own debounce logic.
        for (var i = 0; i < 9; i++)
        {
            var resp = await SendCustomerPageAsync(pageToken, cfIp);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        await using var scope2 = _factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var request2 = await db2.Set<KeepRequest>().AsNoTracking().FirstAsync(r => r.PageToken == pageToken);
        Assert.Equal(firstViewedAt, request2.CustomerPageLastViewedAtUtc);
    }

    [Fact]
    public async Task GetCustomerPage_Returns429AtEleventhRequest()
    {
        const string pageToken = "rl-customer-page-eleventh";
        const string cfIp = "203.0.113.41";
        await SeedRequestAsync(pageToken);

        for (var i = 0; i < 10; i++)
        {
            var resp = await SendCustomerPageAsync(pageToken, cfIp);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        var final = await SendCustomerPageAsync(pageToken, cfIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, final.StatusCode);
    }

    [Fact]
    public async Task GetCustomerPage_DifferentPageTokensSameIp_HaveIsolatedBuckets()
    {
        // ADR-129: the composite IP+pageToken partition means one customer's request page can't
        // throttle a different customer's page even from the same IP (e.g. shared office wifi).
        const string cfIp = "203.0.113.42";
        var tokenA = "rl-customer-page-isolation-a";
        var tokenB = "rl-customer-page-isolation-b";
        await SeedRequestAsync(tokenA);
        await SeedRequestAsync(tokenB);

        // Exhaust tokenA's bucket.
        for (var i = 0; i <= 10; i++)
            await SendCustomerPageAsync(tokenA, cfIp);

        // tokenB's first request, same IP, must not be rate limited (separate partition).
        var resp = await SendCustomerPageAsync(tokenB, cfIp);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
    }

    private Task<HttpResponseMessage> SendCustomerPageAsync(string pageToken, string cfIp)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/keep/r/{pageToken}");
        req.Headers.TryAddWithoutValidation("CF-Connecting-IP", cfIp);
        return _client.SendAsync(req);
    }
}
