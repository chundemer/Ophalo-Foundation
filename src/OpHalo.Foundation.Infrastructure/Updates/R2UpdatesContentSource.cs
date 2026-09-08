using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Updates;
using OpHalo.Foundation.Infrastructure.Storage;

namespace OpHalo.Foundation.Infrastructure.Updates;

/// <summary>
/// Cloudflare R2 implementation of <see cref="IUpdatesContentSource"/> (GAP-038, BL149),
/// accessed through the S3-compatible AWS SDK against the same bucket as ADR-471 business
/// documents but a distinct, well-known key space:
/// <list type="bullet">
///   <item><c>platform/updates.json</c> — the feed document.</item>
///   <item><c>platform/updates/guides/img/&lt;name&gt;</c> — guide images.</item>
/// </list>
/// This adapter reads only those keys; it never publishes and never resolves a caller-supplied
/// key or URL. Every read is bounded by a 5-second cancellation-aware timeout; a provider error
/// or timeout is translated to a status, never thrown (caller cancellation still propagates).
/// </summary>
public sealed class R2UpdatesContentSource : IUpdatesContentSource, IDisposable
{
    internal const string FeedObjectKey = "platform/updates.json";
    internal const string GuideImagePrefix = "platform/updates/guides/img/";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly ILogger<R2UpdatesContentSource> _logger;

    public R2UpdatesContentSource(R2Settings settings, ILogger<R2UpdatesContentSource> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsConfigured)
            throw new InvalidOperationException("R2Settings is incomplete; the updates content source cannot start.");

        _bucketName = settings.BucketName;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{settings.CloudflareAccountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
        };
        _client = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey),
            config);
    }

    public async Task<UpdatesFeedFetch> GetFeedDocumentAsync(long maxBytes, CancellationToken cancellationToken)
    {
        using var timeout = LinkedTimeout(cancellationToken);
        try
        {
            using var response = await _client.GetObjectAsync(_bucketName, FeedObjectKey, timeout.Token);

            if (response.ContentLength > maxBytes)
            {
                _logger.LogError(
                    "Updates feed object is {Length} bytes, over the {Cap}-byte cap; treating as a read failure.",
                    response.ContentLength, maxBytes);
                return UpdatesFeedFetch.Failed;
            }

            await using var body = response.ResponseStream;
            var bytes = await ReadCappedAsync(body, maxBytes, timeout.Token);
            if (bytes is null)
            {
                _logger.LogError("Updates feed object exceeded the {Cap}-byte cap mid-stream; treating as a read failure.", maxBytes);
                return UpdatesFeedFetch.Failed;
            }

            return UpdatesFeedFetch.Available(bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Updates feed read from R2 timed out after {Timeout}s.", ReadTimeout.TotalSeconds);
            return UpdatesFeedFetch.Failed;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return UpdatesFeedFetch.Missing;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Updates feed read from R2 failed.");
            return UpdatesFeedFetch.Failed;
        }
    }

    public async Task<UpdatesImageFetch> GetGuideImageAsync(string name, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var key = GuideImagePrefix + name;

        using var timeout = LinkedTimeout(cancellationToken);
        try
        {
            using var response = await _client.GetObjectAsync(_bucketName, key, timeout.Token);

            if (response.ContentLength > maxBytes)
                return UpdatesImageFetch.TooLarge;

            await using var body = response.ResponseStream;
            var bytes = await ReadCappedAsync(body, maxBytes, timeout.Token);
            return bytes is null
                ? UpdatesImageFetch.TooLarge
                : UpdatesImageFetch.Available(bytes, response.Headers.ContentType);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Guide image read from R2 timed out after {Timeout}s.", ReadTimeout.TotalSeconds);
            return UpdatesImageFetch.Failed;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return UpdatesImageFetch.Missing;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Guide image read from R2 failed.");
            return UpdatesImageFetch.Failed;
        }
    }

    /// <summary>Reads at most <paramref name="maxBytes"/>; returns null if the stream exceeds it.</summary>
    private static async Task<byte[]?> ReadCappedAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static CancellationTokenSource LinkedTimeout(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ReadTimeout);
        return cts;
    }

    private static bool IsNotFound(AmazonS3Exception ex) =>
        ex.StatusCode == System.Net.HttpStatusCode.NotFound
        || string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.Ordinal);

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Fallback <see cref="IUpdatesContentSource"/> for instances where R2 is not configured
/// (local development, tests). Registered unconditionally in that case so the seam is always
/// resolvable — a DI activation failure here would surface as a 500 instead of the contracted
/// empty-feed / 503 fallback (BL149 correction #3).
/// </summary>
public sealed class UnavailableUpdatesContentSource : IUpdatesContentSource
{
    public Task<UpdatesFeedFetch> GetFeedDocumentAsync(long maxBytes, CancellationToken cancellationToken) =>
        Task.FromResult(UpdatesFeedFetch.Unavailable);

    public Task<UpdatesImageFetch> GetGuideImageAsync(string name, long maxBytes, CancellationToken cancellationToken) =>
        Task.FromResult(UpdatesImageFetch.Unavailable);
}
