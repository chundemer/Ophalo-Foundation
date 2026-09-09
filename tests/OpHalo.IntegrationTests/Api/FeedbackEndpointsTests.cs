using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for <c>POST /feedback</c> (GAP-038 / 038-2b, BL149): gated by
/// <c>Feedback:Enabled</c>, authenticated + per-<c>account_user</c> rate limited, persist-first
/// with one synchronous founder-channel delivery and scrub-on-confirmed-success.
///
/// Runs against a Testcontainers PostgreSQL database with <see cref="IFounderNotifier"/> swapped
/// for an in-memory fake so each test drives a specific delivery outcome.
/// </summary>
public sealed class FeedbackEndpointsTests : IClassFixture<FeedbackEndpointsTests.FeedbackWebFactory>, IAsyncLifetime
{
    private readonly FeedbackWebFactory _factory;
    private readonly HttpClient _client;
    private string _cookie = string.Empty;
    private Guid _accountUserId;

    public FeedbackEndpointsTests(FeedbackWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.Notifier.Reset();

        var now = DateTime.UtcNow;
        var provision = new AccountProvisioningService().CreateVerified(
            email: "owner@feedback-tests.com",
            name: "Feedback Owner",
            businessName: "Feedback Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));
        Assert.True(provision.IsSuccess);
        var graph = provision.Value;
        _accountUserId = graph.Owner.Id;

        await using (var scope = _factory.CreateScope())
        {
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
        }

        _cookie = $"ophalo.sid={await _factory.SeedSessionAsync(graph.Owner.Id, graph.Account.Id)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage Post(object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/feedback") { Content = JsonContent.Create(body) };
        req.Headers.Add("Cookie", _cookie);
        return req;
    }

    [Fact]
    public async Task Route_is_not_mapped_when_the_feature_flag_is_off()
    {
        using var disabled = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Feedback:Enabled"] = "false" })));
        var client = disabled.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/feedback")
        {
            Content = JsonContent.Create(new { message = "hi" }),
        };
        req.Headers.Add("Cookie", _cookie);
        using var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Submission_requires_authentication()
    {
        using var res = await _client.PostAsJsonAsync("/feedback", new { message = "hi" });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Confirmed_delivery_returns_200_and_scrubs_the_persisted_body()
    {
        _factory.Notifier.Result = true;

        using var res = await _client.SendAsync(Post(new
        {
            message = "The schedule view is confusing.",
            category = "confusing",
            context = new { route = "/schedule" },
        }));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var row = await db.FeedbackSubmissions.SingleAsync();
        Assert.Equal(FeedbackDeliveryState.Delivered, row.DeliveryState);
        Assert.Null(row.Message);
        Assert.Null(row.ContextJson);
        Assert.Equal(1, row.AttemptCount);

        var evt = Assert.Single(_factory.Notifier.Events);
        Assert.Equal("feedback_submitted", evt.Type);
        Assert.Equal(row.Id.ToString(), evt.Correlation);
    }

    [Fact]
    public async Task Unconfirmed_delivery_returns_202_and_leaves_a_pending_row_with_the_body()
    {
        _factory.Notifier.Result = false;

        using var res = await _client.SendAsync(Post(new { message = "Please add dark mode", category = "missing_thing" }));

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var row = await db.FeedbackSubmissions.SingleAsync();
        Assert.Equal(FeedbackDeliveryState.Pending, row.DeliveryState);
        Assert.Equal("Please add dark mode", row.Message);
        Assert.Equal(FeedbackCategory.MissingThing, row.Category);
        Assert.Equal(1, row.AttemptCount);
    }

    [Fact]
    public async Task Missing_category_defaults_to_other()
    {
        _factory.Notifier.Result = true;

        using var res = await _client.SendAsync(Post(new { message = "no category here" }));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var row = await db.FeedbackSubmissions.SingleAsync();
        Assert.Equal(FeedbackCategory.Other, row.Category);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_message_is_400(string message)
    {
        using var res = await _client.SendAsync(Post(new { message }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Over_length_message_is_413()
    {
        using var res = await _client.SendAsync(Post(new { message = new string('x', 4_001) }));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_category_is_400()
    {
        using var res = await _client.SendAsync(Post(new { message = "hi", category = "banana" }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Eleventh_submission_in_the_window_is_rate_limited()
    {
        _factory.Notifier.Result = true;

        for (var i = 0; i < 10; i++)
        {
            using var ok = await _client.SendAsync(Post(new { message = $"submission {i}" }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var limited = await _client.SendAsync(Post(new { message = "one too many" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    // --- test doubles / host ----------------------------------------------------

    public sealed class FakeFounderNotifier : IFounderNotifier
    {
        public bool Result { get; set; }
        public List<FounderEvent> Events { get; } = [];

        public void Reset()
        {
            Result = false;
            Events.Clear();
        }

        public Task<bool> NotifyAsync(FounderEvent founderEvent, CancellationToken cancellationToken)
        {
            Events.Add(founderEvent);
            return Task.FromResult(Result);
        }
    }

    public sealed class FeedbackWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container =
            new PostgreSqlBuilder("postgres:17.5-alpine").Build();

        public FakeFounderNotifier Notifier { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // RateLimitTesting (not Testing): keeps the rate limiter active so the per-account_user
            // 10/hour policy is exercised. HTTPS redirect and the production-config validator stay
            // disabled, exactly as the shared RateLimitWebFactory does.
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
                    ["Feedback:Enabled"] = "true",
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFounderNotifier>();
                services.AddSingleton<IFounderNotifier>(Notifier);
            });
        }

        public async Task<string> SeedSessionAsync(Guid accountUserId, Guid accountId)
        {
            var now = DateTime.UtcNow;
            var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var tokenHash = OpHalo.Foundation.Infrastructure.Security.SessionHasher.HashToken(rawToken);
            var session = OpHalo.Foundation.Core.Entities.Accounts.AccountSession.Create(
                accountId,
                accountUserId,
                tokenHash,
                OpHalo.Foundation.Core.Entities.Accounts.Enums.SessionClientType.Browser,
                null,
                now,
                now.AddDays(OpHalo.Foundation.Core.Constants.AuthConstants.SessionAbsoluteExpiryDays));

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.AccountSessions.Add(session);
            await db.SaveChangesAsync();
            return rawToken;
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
}
