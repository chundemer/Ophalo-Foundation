using System.Text.Json;
using Json.Schema;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Application.Updates;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Api.Updates;

/// <summary>
/// Fetches, validates, and caches the Help &amp; Updates feed for <c>GET /updates</c>
/// (GAP-038, BL149).
///
/// <para>
/// A valid read populates two per-instance slots: a 5-minute fresh cache (a fresh hit never
/// touches storage) and a separate last-known-good (LKG) slot. Any read, parse, or validation
/// failure keeps serving LKG; a cold instance with neither returns the empty schema-1 feed.
/// Neither slot is shared or durable. Failures are logged (structured logs / Sentry) only —
/// the founder-channel notifier is 038-2.
/// </para>
///
/// <para>
/// Validation runs before deserialization: the reviewed <c>updates.schema.json</c> artifact is
/// embedded and evaluated as-is with format assertion enabled (so a bad <c>date-time</c> fails,
/// per BL149 correction #2), then a semantic pass rejects duplicate <c>entries[].id</c> or
/// <c>guides[].id</c> (correction #4) which JSON Schema cannot express.
/// </para>
///
/// <para>
/// 038-2c-ii: consecutive read/validation failures are counted (reset by the next good read);
/// on the <see cref="ConsecutiveFailureAlertThreshold"/>th and beyond, one
/// <c>content_source_failure</c> founder-channel alert is sent — rate-limited per instance via
/// <see cref="FounderAlertThrottle"/> to one per <see cref="AlertMinInterval"/>. The alert is
/// awaited after <c>_fetchGate</c> is released (never blocks other readers) and carries no feed
/// content. Best-effort and per-instance, matching the last-known-good slot.
/// </para>
/// </summary>
public sealed class UpdatesFeedCache
{
    private const string CacheKey = "updates.feed.v1";
    private const string SchemaResourceName = "OpHalo.Api.Updates.updates.schema.json";
    private static readonly TimeSpan FreshTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Hard cap on the feed document read (4 MiB). The schema bounds the feed to 100 entries +
    /// 50 guides of ≤12,000-char bodies (~1.9 MiB of content), so 4 MiB leaves generous headroom
    /// for JSON overhead while still refusing a mistakenly huge upload before it reaches memory.
    /// A read over the cap takes the normal feed-failure path (LKG, else the empty feed).
    /// </summary>
    public const long MaxFeedBytes = 4L * 1024 * 1024;

    /// <summary>
    /// Consecutive failed reads (fetch unavailable, parse, schema, or semantic failure) before the
    /// first <c>content_source_failure</c> founder-channel alert fires. Reset by the next good read.
    /// </summary>
    public const int ConsecutiveFailureAlertThreshold = 3;

    /// <summary>Per-instance minimum gap between <c>content_source_failure</c> alerts.</summary>
    public static readonly TimeSpan AlertMinInterval = TimeSpan.FromMinutes(30);

    /// <summary>Throttle bucket — deliberately separate from the feedback-delivery alert bucket.</summary>
    public const string AlertKey = "updates.content_source_failure";

    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);
    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.Flag,
        RequireFormatValidation = true,
    };

    private readonly IUpdatesContentSource _source;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;
    private readonly ILogger<UpdatesFeedCache> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FounderAlertThrottle _alertThrottle;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private volatile string? _lastKnownGood;

    /// <summary>
    /// Consecutive failed reads. Mutated on the read path only while <see cref="_fetchGate"/> is
    /// held; <see cref="Reset"/> also zeroes it (test isolation / a future single-threaded
    /// cache-bust seam — gate it there too if that seam ever becomes concurrently callable).
    /// </summary>
    private int _consecutiveFailures;

    public UpdatesFeedCache(
        IUpdatesContentSource source,
        IMemoryCache cache,
        IClock clock,
        ILogger<UpdatesFeedCache> logger,
        IServiceScopeFactory scopeFactory,
        FounderAlertThrottle alertThrottle)
    {
        _source = source;
        _cache = cache;
        _clock = clock;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _alertThrottle = alertThrottle;
    }

    /// <summary>
    /// Returns the feed as a JSON string ready to write to the response body: the fresh copy if
    /// one is cached, otherwise a freshly fetched-and-validated copy, otherwise last-known-good,
    /// otherwise <see cref="UpdatesJson.EmptyFeed"/>. Never throws for a content failure.
    /// </summary>
    public async Task<string> GetFeedJsonAsync(CancellationToken cancellationToken)
    {
        if (TryGetFresh(out var fresh))
            return fresh;

        string json;
        ContentFailureAlert? alert;

        await _fetchGate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while we waited on the gate.
            if (TryGetFresh(out fresh))
                return fresh;

            (json, alert) = await FetchValidateAndTrackAsync(cancellationToken);
        }
        finally
        {
            _fetchGate.Release();
        }

        // Awaited outside the gate: a slow founder-channel post must never stall other readers.
        if (alert is not null)
            await SendContentSourceFailureAlertAsync(alert, cancellationToken);

        return json;
    }

    private async Task<(string Json, ContentFailureAlert? Alert)> FetchValidateAndTrackAsync(
        CancellationToken cancellationToken)
    {
        var fetch = await _source.GetFeedDocumentAsync(MaxFeedBytes, cancellationToken);

        if (fetch.Status == UpdatesContentStatus.Available
            && TryBuildResponseJson(fetch.Content!, out var json))
        {
            _cache.Set(
                CacheKey,
                new CachedFeed(json, _clock.UtcNow),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = FreshTtl + FreshTtl });
            _lastKnownGood = json;
            _consecutiveFailures = 0;
            return (json, null);
        }

        var servedEmpty = _lastKnownGood is null;
        var reason = fetch.Status == UpdatesContentStatus.Available
            ? "schema or semantic validation"
            : fetch.Status.ToString();

        if (fetch.Status == UpdatesContentStatus.Available)
            _logger.LogError(
                "Updates feed failed schema or semantic validation; serving {Fallback}.",
                servedEmpty ? "the empty feed" : "last-known-good");
        else
            _logger.LogWarning(
                "Updates content source returned {Status}; serving {Fallback}.",
                fetch.Status,
                servedEmpty ? "the empty feed" : "last-known-good");

        _consecutiveFailures++;

        ContentFailureAlert? alert = null;
        if (_consecutiveFailures >= ConsecutiveFailureAlertThreshold
            && _alertThrottle.TryAcquire(AlertKey, AlertMinInterval, _clock.UtcNow))
        {
            alert = new ContentFailureAlert(_consecutiveFailures, reason, servedEmpty);
        }

        return (_lastKnownGood ?? UpdatesJson.EmptyFeed, alert);
    }

    private async Task SendContentSourceFailureAlertAsync(
        ContentFailureAlert alert, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IFounderNotifier>();
        await notifier.NotifyAsync(
            new FounderEvent(
                Type: "content_source_failure",
                Summary: $"Help & Updates content source has failed {alert.ConsecutiveFailures} "
                    + $"consecutive reads ({alert.Reason}); serving "
                    + (alert.ServedEmpty ? "the empty feed." : "last-known-good."),
                Count: alert.ConsecutiveFailures),
            cancellationToken);
    }

    private sealed record ContentFailureAlert(int ConsecutiveFailures, string Reason, bool ServedEmpty);

    /// <summary>
    /// Drops both the fresh cache and the last-known-good slot so the next call re-reads the
    /// source. Used by tests for isolation; also the seam a future founder-only cache-bust
    /// (BL149) would call.
    /// </summary>
    public void Reset()
    {
        _cache.Remove(CacheKey);
        _lastKnownGood = null;
        _consecutiveFailures = 0;
    }

    private bool TryGetFresh(out string json)
    {
        if (_cache.TryGetValue(CacheKey, out CachedFeed? cached)
            && cached is not null
            && _clock.UtcNow - cached.FetchedAt < FreshTtl)
        {
            json = cached.Json;
            return true;
        }

        json = string.Empty;
        return false;
    }

    private bool TryBuildResponseJson(byte[] document, out string json)
    {
        json = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(document);
            var root = doc.RootElement;

            if (!Schema.Value.Evaluate(root, EvaluationOptions).IsValid)
                return false;

            if (HasDuplicateIds(root, "entries") || HasDuplicateIds(root, "guides"))
                return false;

            var response = root.Deserialize<UpdatesFeedResponse>(UpdatesJson.Options);
            if (response is null)
                return false;

            json = JsonSerializer.Serialize(response, UpdatesJson.Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasDuplicateIds(JsonElement feed, string arrayName)
    {
        if (!feed.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
            return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !seen.Add(id.GetString()!))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonSchema LoadSchema()
    {
        using var stream = typeof(UpdatesFeedCache).Assembly
            .GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded schema resource '{SchemaResourceName}' is missing from the assembly manifest.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    private sealed record CachedFeed(string Json, DateTime FetchedAt);
}
