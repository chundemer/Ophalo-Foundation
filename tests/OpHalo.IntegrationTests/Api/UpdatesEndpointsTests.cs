using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpHalo.Api.Updates;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Updates;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for the Foundation-owned Help &amp; Updates read endpoints
/// (GAP-038 / 038-1a, BL149): <c>GET /updates</c> and <c>GET /updates/guides/img/{name}</c>.
///
/// The real host runs against a Testcontainers PostgreSQL database (auth is exercised exactly
/// as in production — a seeded session cookie), with <see cref="IUpdatesContentSource"/> swapped
/// for an in-memory fake so each test drives a specific content outcome.
/// </summary>
public sealed class UpdatesEndpointsTests : IClassFixture<UpdatesEndpointsTests.UpdatesWebFactory>, IAsyncLifetime
{
    private readonly UpdatesWebFactory _factory;
    private readonly HttpClient _client;
    private string _cookie = string.Empty;

    public UpdatesEndpointsTests(UpdatesWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.Source.Reset();
        _factory.Services.GetRequiredService<UpdatesFeedCache>().Reset();

        var now = DateTime.UtcNow;
        var provision = new AccountProvisioningService().CreateVerified(
            email: "owner@updates-tests.com",
            name: "Updates Owner",
            businessName: "Updates Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));
        Assert.True(provision.IsSuccess);
        var graph = provision.Value;

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Users.Add(graph.User);
            db.Accounts.Add(graph.Account);
            db.AccountUsers.Add(graph.Owner);
            db.AccountEntitlements.Add(graph.Entitlements);

            // Break the Account <-> AccountUser circular FK the same way the other API tests do.
            var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
            ownerFk.CurrentValue = null;
            await db.SaveChangesAsync();
            ownerFk.CurrentValue = graph.Owner.Id;
            await db.SaveChangesAsync();
        }

        _cookie = $"ophalo.sid={await _factory.SeedSessionAsync(graph.Owner.Id, graph.Account.Id)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- GET /updates ---------------------------------------------------------

    [Fact]
    public async Task Feed_returns_the_validated_payload_with_contract_headers()
    {
        _factory.Source.Feed = FeedBytes("""
            { "schema": 1,
              "entries": [ { "id": "upd-1", "published_at": "2026-09-07T12:00:00Z",
                             "section": "known_issue", "title": "Messages delayed", "body": "Carriers queue." } ],
              "guides": [ { "id": "g-1", "updated_at": "2026-09-06T16:12:00Z",
                            "title": "Log a visit", "body": "1. Open the request." } ] }
            """);

        using var req = Get("/updates");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/json; charset=utf-8", res.Content.Headers.ContentType?.ToString());
        AssertPrivateMaxAge(res, 300);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var entry = doc.RootElement.GetProperty("entries")[0];
        Assert.Equal("upd-1", entry.GetProperty("id").GetString());
        Assert.Equal("active", entry.GetProperty("status").GetString());
        Assert.False(entry.GetProperty("highlight").GetBoolean());
    }

    [Fact]
    public async Task Feed_requires_authentication()
    {
        _factory.Source.Feed = FeedBytes(UpdatesJsonEmpty);

        using var res = await _client.GetAsync("/updates");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Feed_falls_back_to_the_empty_feed_when_the_schema_is_unknown()
    {
        _factory.Source.Feed = FeedBytes("""{ "schema": 9, "entries": [], "guides": [] }""");

        using var req = Get("/updates");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("schema").GetInt32());
        Assert.Empty(doc.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("guides").EnumerateArray());
    }

    [Fact]
    public async Task Feed_returns_the_empty_feed_when_content_is_unavailable_on_a_cold_instance()
    {
        _factory.Source.FeedStatusOverride = UpdatesContentStatus.Unavailable;

        using var req = Get("/updates");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Empty(doc.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Feed_document_over_the_size_cap_falls_back_to_the_empty_feed()
    {
        _factory.Source.Feed = new byte[UpdatesFeedCache.MaxFeedBytes + 1];

        using var req = Get("/updates");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Empty(doc.RootElement.GetProperty("entries").EnumerateArray());
    }

    // --- GET /updates/guides/img/{name} --------------------------------------

    [Fact]
    public async Task Guide_image_streams_with_a_forced_mime_and_security_headers()
    {
        _factory.Source.Image = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        _factory.Source.ImageContentType = "image/png";

        using var req = Get("/updates/guides/img/record-work-a1b2c3d4.png");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("image/png", res.Content.Headers.ContentType?.ToString());
        Assert.Equal("inline", res.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        AssertPrivateMaxAge(res, 86400);

        // The seam only ever sees the bare validated file name — never a client-controlled key/URL.
        Assert.Equal("record-work-a1b2c3d4.png", _factory.Source.LastImageName);
    }

    [Fact]
    public async Task Guide_image_requires_authentication()
    {
        using var res = await _client.GetAsync("/updates/guides/img/x.png");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("a%2Fb.png")]
    [InlineData("nope.gif")]
    [InlineData("noextension")]
    [InlineData(".png")]
    public async Task Guide_image_rejects_a_bad_name_with_404(string name)
    {
        _factory.Source.Image = new byte[] { 1, 2, 3 };
        _factory.Source.ImageContentType = "image/png";

        using var req = Get("/updates/guides/img/" + name);
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Null(_factory.Source.LastImageName); // never reached the seam
    }

    [Fact]
    public async Task Guide_image_missing_object_is_404()
    {
        _factory.Source.ImageStatusOverride = UpdatesContentStatus.Missing;

        using var req = Get("/updates/guides/img/gone.png");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Guide_image_over_the_two_mebibyte_cap_is_413()
    {
        _factory.Source.Image = new byte[2_097_153];
        _factory.Source.ImageContentType = "image/png";

        using var req = Get("/updates/guides/img/huge.png");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("image/jpeg")] // stored MIME disagrees with the .png extension
    public async Task Guide_image_with_a_missing_or_mismatched_mime_is_415(string? storedMime)
    {
        _factory.Source.Image = new byte[] { 1, 2, 3 };
        _factory.Source.ImageContentType = storedMime;

        using var req = Get("/updates/guides/img/mismatch.png");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
    }

    [Fact]
    public async Task Guide_image_provider_failure_is_503()
    {
        _factory.Source.ImageStatusOverride = UpdatesContentStatus.Failed;

        using var req = Get("/updates/guides/img/x.png");
        using var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    // --- helpers -------------------------------------------------------------

    private const string UpdatesJsonEmpty = """{ "schema": 1, "entries": [], "guides": [] }""";

    private static byte[] FeedBytes(string json) => Encoding.UTF8.GetBytes(json);

    private static void AssertPrivateMaxAge(HttpResponseMessage res, int seconds)
    {
        // Kestrel serialises the parsed Cache-Control header as "max-age=N, private" (directive
        // order is not significant per RFC 9111); assert both the wire form and the directives.
        Assert.Equal($"max-age={seconds}, private", res.Headers.GetValues("Cache-Control").Single());
        var cc = res.Headers.CacheControl;
        Assert.NotNull(cc);
        Assert.True(cc!.Private);
        Assert.Equal(TimeSpan.FromSeconds(seconds), cc.MaxAge);
        Assert.False(cc.Public);
    }

    private HttpRequestMessage Get(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("Cookie", _cookie);
        return req;
    }

    // --- test doubles / host -----------------------------------------------

    public sealed class FakeUpdatesContentSource : IUpdatesContentSource
    {
        public byte[]? Feed { get; set; }
        public UpdatesContentStatus? FeedStatusOverride { get; set; }

        public byte[]? Image { get; set; }
        public string? ImageContentType { get; set; }
        public UpdatesContentStatus? ImageStatusOverride { get; set; }
        public string? LastImageName { get; private set; }

        public void Reset()
        {
            Feed = null;
            FeedStatusOverride = null;
            Image = null;
            ImageContentType = null;
            ImageStatusOverride = null;
            LastImageName = null;
        }

        public Task<UpdatesFeedFetch> GetFeedDocumentAsync(long maxBytes, CancellationToken cancellationToken)
        {
            if (FeedStatusOverride is { } status)
                return Task.FromResult(new UpdatesFeedFetch(status, null));
            if (Feed is null)
                return Task.FromResult(UpdatesFeedFetch.Missing);
            return Task.FromResult(
                Feed.LongLength > maxBytes ? UpdatesFeedFetch.Failed : UpdatesFeedFetch.Available(Feed));
        }

        public Task<UpdatesImageFetch> GetGuideImageAsync(string name, long maxBytes, CancellationToken cancellationToken)
        {
            LastImageName = name;
            if (ImageStatusOverride is { } status)
                return Task.FromResult(new UpdatesImageFetch(status, null, null));
            if (Image is null)
                return Task.FromResult(UpdatesImageFetch.Missing);
            if (Image.LongLength > maxBytes)
                return Task.FromResult(UpdatesImageFetch.TooLarge);
            return Task.FromResult(UpdatesImageFetch.Available(Image, ImageContentType));
        }
    }

    public sealed class UpdatesWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container =
            new PostgreSqlBuilder("postgres:17.5-alpine").Build();

        public FakeUpdatesContentSource Source { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                    ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUpdatesContentSource>();
                services.AddSingleton<IUpdatesContentSource>(Source);
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
