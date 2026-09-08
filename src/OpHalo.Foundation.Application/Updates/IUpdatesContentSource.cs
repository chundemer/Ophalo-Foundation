namespace OpHalo.Foundation.Application.Updates;

/// <summary>
/// Read-only seam for the founder-maintained Help &amp; Updates content (GAP-038, BL149).
/// The published copy lives in object storage; the repository is the source of truth. This
/// seam only reads — it never publishes. Implementations resolve fixed, well-known object
/// keys only; callers pass validated domain scalars, never a storage key or URL.
/// </summary>
/// <remarks>
/// An implementation is ALWAYS registered (BL149 correction #3): when storage is not
/// configured, the unavailable implementation reports <see cref="UpdatesContentStatus.Unavailable"/>
/// so the feed falls back to last-known-good / empty rather than a DI activation failure.
/// Implementations must not throw for a provider, timeout, or parse failure — they translate
/// it to a status. Caller cancellation still propagates as <see cref="OperationCanceledException"/>.
/// </remarks>
public interface IUpdatesContentSource
{
    /// <summary>
    /// Fetches the raw feed document bytes (<c>platform/updates.json</c>). The bytes are
    /// unparsed and unvalidated; schema and semantic validation are the caller's concern. The
    /// read is capped at <paramref name="maxBytes"/> — an object known to exceed the cap, or one
    /// that exceeds it mid-stream, yields <see cref="UpdatesContentStatus.Failed"/> with no
    /// content, so a mistakenly huge upload can never exhaust process memory before validation.
    /// </summary>
    Task<UpdatesFeedFetch> GetFeedDocumentAsync(long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches one guide image by its already-validated file name (matching
    /// <c>^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.(png|jpe?g|webp)$</c>). The implementation maps it
    /// only to <c>platform/updates/guides/img/&lt;name&gt;</c> and resolves nothing outside that
    /// prefix. The returned bytes are capped at <paramref name="maxBytes"/>: an object whose
    /// length is known to exceed the cap, or which exceeds it while streaming, yields
    /// <see cref="UpdatesContentStatus.TooLarge"/> with no content.
    /// </summary>
    Task<UpdatesImageFetch> GetGuideImageAsync(string name, long maxBytes, CancellationToken cancellationToken);
}

/// <summary>Outcome of a content read. Only <see cref="Available"/> carries content.</summary>
public enum UpdatesContentStatus
{
    /// <summary>The object was read successfully.</summary>
    Available,

    /// <summary>The object does not exist.</summary>
    Missing,

    /// <summary>The object exceeds the caller's byte cap (images only).</summary>
    TooLarge,

    /// <summary>Content storage is not configured for this instance.</summary>
    Unavailable,

    /// <summary>A provider error or timeout prevented the read.</summary>
    Failed,
}

/// <summary>Result of <see cref="IUpdatesContentSource.GetFeedDocumentAsync"/>.</summary>
public sealed record UpdatesFeedFetch(UpdatesContentStatus Status, byte[]? Content)
{
    public static UpdatesFeedFetch Available(byte[] content) => new(UpdatesContentStatus.Available, content);
    public static UpdatesFeedFetch Missing { get; } = new(UpdatesContentStatus.Missing, null);
    public static UpdatesFeedFetch Unavailable { get; } = new(UpdatesContentStatus.Unavailable, null);
    public static UpdatesFeedFetch Failed { get; } = new(UpdatesContentStatus.Failed, null);
}

/// <summary>Result of <see cref="IUpdatesContentSource.GetGuideImageAsync"/>.</summary>
/// <param name="StoredContentType">
/// The content type recorded on the stored object, for the caller to reconcile against the
/// file extension. Null unless <see cref="UpdatesContentStatus.Available"/>.
/// </param>
public sealed record UpdatesImageFetch(UpdatesContentStatus Status, byte[]? Content, string? StoredContentType)
{
    public static UpdatesImageFetch Available(byte[] content, string? storedContentType) =>
        new(UpdatesContentStatus.Available, content, storedContentType);

    public static UpdatesImageFetch Missing { get; } = new(UpdatesContentStatus.Missing, null, null);
    public static UpdatesImageFetch TooLarge { get; } = new(UpdatesContentStatus.TooLarge, null, null);
    public static UpdatesImageFetch Unavailable { get; } = new(UpdatesContentStatus.Unavailable, null, null);
    public static UpdatesImageFetch Failed { get; } = new(UpdatesContentStatus.Failed, null, null);
}
