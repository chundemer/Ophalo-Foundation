using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpHalo.Api.Updates;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Application.Updates;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Api;

/// <summary>
/// Focused tests for <see cref="UpdatesFeedCache"/> (GAP-038, BL149): the 5-minute fresh cache,
/// last-known-good replacement, and the schema + semantic validation gate (format assertion for
/// bad date-times; duplicate-id rejection).
/// </summary>
public sealed class UpdatesFeedCacheTests
{
    private static readonly DateTime T0 = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private const string ValidFeed = """
        {
          "schema": 1,
          "entries": [
            { "id": "upd-1", "published_at": "2026-09-07T12:00:00Z",
              "section": "known_issue", "title": "Messages delayed", "body": "Some carriers queue." }
          ],
          "guides": [
            { "id": "guide-1", "updated_at": "2026-09-06T16:12:00Z",
              "title": "Log a visit", "body": "1. Open the request." }
          ]
        }
        """;

    [Fact]
    public async Task Valid_feed_is_returned_with_source_side_defaults_materialised()
    {
        var (cache, _) = Build(Feed(ValidFeed));

        using var doc = JsonDocument.Parse(await cache.GetFeedJsonAsync(default));

        var entry = doc.RootElement.GetProperty("entries")[0];
        Assert.Equal("active", entry.GetProperty("status").GetString());
        Assert.False(entry.GetProperty("highlight").GetBoolean());
        Assert.False(entry.TryGetProperty("banner_until", out _));
        Assert.Equal("2026-09-07T12:00:00Z", entry.GetProperty("published_at").GetString());
    }

    [Fact]
    public async Task Fresh_cache_hit_within_five_minutes_does_not_touch_the_source()
    {
        var (cache, source) = Build(Feed(ValidFeed));

        await cache.GetFeedJsonAsync(default);
        source.Clock.Advance(TimeSpan.FromMinutes(4));
        await cache.GetFeedJsonAsync(default);

        Assert.Equal(1, source.FeedCalls);
    }

    [Fact]
    public async Task Source_is_re_read_once_the_fresh_window_expires()
    {
        var (cache, source) = Build(Feed(ValidFeed));

        await cache.GetFeedJsonAsync(default);
        source.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await cache.GetFeedJsonAsync(default);

        Assert.Equal(2, source.FeedCalls);
    }

    [Fact]
    public async Task After_a_valid_read_a_later_failure_serves_last_known_good()
    {
        var (cache, source) = Build(Feed(ValidFeed));
        var good = await cache.GetFeedJsonAsync(default);

        source.Clock.Advance(TimeSpan.FromMinutes(6));
        source.Next = UpdatesFeedFetch.Failed;
        var served = await cache.GetFeedJsonAsync(default);

        Assert.Equal(good, served);
        Assert.NotEqual(UpdatesJson.EmptyFeed, served);
    }

    [Fact]
    public async Task Cold_instance_with_an_unavailable_source_returns_the_empty_feed()
    {
        var (cache, _) = Build(UpdatesFeedFetch.Unavailable);

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Fact]
    public async Task Malformed_json_falls_back_to_the_empty_feed()
    {
        var (cache, _) = Build(Feed("{ not json"));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Fact]
    public async Task Schema_invalid_feed_falls_back_to_the_empty_feed()
    {
        // "title" is required on an entry.
        var (cache, _) = Build(Feed("""
            { "schema": 1, "guides": [],
              "entries": [ { "id": "x", "published_at": "2026-09-07T12:00:00Z",
                             "section": "known_issue", "body": "no title" } ] }
            """));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Fact]
    public async Task Unknown_schema_version_falls_back_to_the_empty_feed()
    {
        var (cache, _) = Build(Feed("""{ "schema": 2, "entries": [], "guides": [] }"""));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Theory]
    [InlineData("\"not-a-date\"")]
    [InlineData("\"2026-13-01T00:00:00Z\"")]
    public async Task Invalid_entry_date_time_is_rejected_by_format_assertion(string publishedAt)
    {
        var (cache, _) = Build(Feed($$"""
            { "schema": 1, "guides": [],
              "entries": [ { "id": "x", "published_at": {{publishedAt}},
                             "section": "known_issue", "title": "t", "body": "b" } ] }
            """));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Fact]
    public async Task Invalid_guide_date_time_is_rejected_by_format_assertion()
    {
        var (cache, _) = Build(Feed("""
            { "schema": 1, "entries": [],
              "guides": [ { "id": "g", "updated_at": "yesterday", "title": "t", "body": "b" } ] }
            """));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Fact]
    public async Task Duplicate_entry_ids_are_rejected()
    {
        var (cache, _) = Build(Feed("""
            { "schema": 1, "guides": [],
              "entries": [
                { "id": "dup", "published_at": "2026-09-07T12:00:00Z", "section": "s", "title": "a", "body": "b" },
                { "id": "dup", "published_at": "2026-09-07T12:00:00Z", "section": "s", "title": "c", "body": "d" }
              ] }
            """));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Fact]
    public async Task Duplicate_guide_ids_are_rejected()
    {
        var (cache, _) = Build(Feed("""
            { "schema": 1, "entries": [],
              "guides": [
                { "id": "dup", "updated_at": "2026-09-06T16:12:00Z", "title": "a", "body": "b" },
                { "id": "dup", "updated_at": "2026-09-06T16:12:00Z", "title": "c", "body": "d" }
              ] }
            """));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
    }

    [Fact]
    public async Task Feed_document_over_the_size_cap_falls_back_to_the_empty_feed()
    {
        // A well-formed feed whose byte length exceeds MaxFeedBytes: padded via a long body.
        var oversized = "{\"schema\":1,\"entries\":[],\"guides\":[{\"id\":\"g\",\"updated_at\":\"2026-09-06T16:12:00Z\","
            + "\"title\":\"t\",\"body\":\"" + new string('x', (int)UpdatesFeedCache.MaxFeedBytes) + "\"}]}";
        var (cache, source) = Build(UpdatesFeedFetch.Available(Encoding.UTF8.GetBytes(oversized)));

        Assert.Equal(UpdatesJson.EmptyFeed, await cache.GetFeedJsonAsync(default));
        Assert.Equal(1, source.FeedCalls);
    }

    // --- 038-2c-ii: content-source failure alert ---

    [Fact]
    public async Task No_alert_before_the_consecutive_failure_threshold()
    {
        var (cache, _, notifier) = BuildFull(UpdatesFeedFetch.Failed, new FakeNotifier());

        for (var i = 0; i < UpdatesFeedCache.ConsecutiveFailureAlertThreshold - 1; i++)
            await cache.GetFeedJsonAsync(default);

        Assert.Empty(notifier.Events);
    }

    [Fact]
    public async Task The_third_consecutive_failure_sends_one_body_free_alert()
    {
        var (cache, _, notifier) = BuildFull(UpdatesFeedFetch.Failed, new FakeNotifier());

        for (var i = 0; i < UpdatesFeedCache.ConsecutiveFailureAlertThreshold; i++)
            await cache.GetFeedJsonAsync(default);

        var evt = Assert.Single(notifier.Events);
        Assert.Equal("content_source_failure", evt.Type);
        Assert.Equal(3, evt.Count);
        Assert.Null(evt.Context);
        Assert.Contains("consecutive reads", evt.Summary);
    }

    [Fact]
    public async Task Alert_is_rate_limited_then_fires_again_after_the_window()
    {
        var (cache, source, notifier) = BuildFull(UpdatesFeedFetch.Failed, new FakeNotifier());

        for (var i = 0; i < 5; i++)
            await cache.GetFeedJsonAsync(default);
        Assert.Single(notifier.Events);

        source.Clock.Advance(UpdatesFeedCache.AlertMinInterval + TimeSpan.FromMinutes(1));
        await cache.GetFeedJsonAsync(default);

        Assert.Equal(2, notifier.Events.Count);
    }

    [Fact]
    public async Task A_good_read_resets_the_consecutive_failure_counter()
    {
        var (cache, source, notifier) = BuildFull(Feed(ValidFeed), new FakeNotifier());

        source.Next = UpdatesFeedFetch.Failed;
        await cache.GetFeedJsonAsync(default);
        await cache.GetFeedJsonAsync(default);

        source.Next = Feed(ValidFeed);
        source.Clock.Advance(TimeSpan.FromMinutes(6));
        await cache.GetFeedJsonAsync(default);

        source.Next = UpdatesFeedFetch.Failed;
        source.Clock.Advance(TimeSpan.FromMinutes(6));
        await cache.GetFeedJsonAsync(default);
        await cache.GetFeedJsonAsync(default);

        Assert.Empty(notifier.Events);
    }

    private static UpdatesFeedFetch Feed(string json) =>
        UpdatesFeedFetch.Available(Encoding.UTF8.GetBytes(json));

    private static (UpdatesFeedCache Cache, FakeSource Source) Build(UpdatesFeedFetch first)
    {
        var (cache, source, _) = BuildFull(first, new FakeNotifier());
        return (cache, source);
    }

    private static (UpdatesFeedCache Cache, FakeSource Source, FakeNotifier Notifier) BuildFull(
        UpdatesFeedFetch first, FakeNotifier notifier)
    {
        var source = new FakeSource(new FakeClock(T0)) { Next = first };
        var services = new ServiceCollection();
        services.AddSingleton<IFounderNotifier>(notifier);
        var cache = new UpdatesFeedCache(
            source,
            new MemoryCache(new MemoryCacheOptions()),
            source.Clock,
            NullLogger<UpdatesFeedCache>.Instance,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new FounderAlertThrottle());
        return (cache, source, notifier);
    }

    private sealed class FakeNotifier : IFounderNotifier
    {
        public List<FounderEvent> Events { get; } = [];

        public Task<bool> NotifyAsync(FounderEvent founderEvent, CancellationToken cancellationToken)
        {
            Events.Add(founderEvent);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSource(FakeClock clock) : IUpdatesContentSource
    {
        public FakeClock Clock { get; } = clock;
        public UpdatesFeedFetch Next { get; set; } = UpdatesFeedFetch.Unavailable;
        public int FeedCalls { get; private set; }

        public Task<UpdatesFeedFetch> GetFeedDocumentAsync(long maxBytes, CancellationToken cancellationToken)
        {
            FeedCalls++;
            if (Next is { Status: UpdatesContentStatus.Available, Content: { } c } && c.LongLength > maxBytes)
                return Task.FromResult(UpdatesFeedFetch.Failed);
            return Task.FromResult(Next);
        }

        public Task<UpdatesImageFetch> GetGuideImageAsync(string name, long maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(UpdatesImageFetch.Unavailable);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        private DateTime _utcNow = utcNow;
        public DateTime UtcNow => _utcNow;
        public void Advance(TimeSpan by) => _utcNow += by;
    }
}
