using System.Text.RegularExpressions;
using OpHalo.Foundation.Application.Updates;

namespace OpHalo.Api.Updates;

/// <summary>
/// Foundation-owned Help &amp; Updates read endpoints (GAP-038, BL149; routes per ADR-501 —
/// flat, no <c>/api/v1</c> prefix). Both sit behind the authenticated-app boundary with no
/// further role or account scope: the feed carries no account-scoped data and is identical for
/// every authenticated user. Anonymous callers get 401.
/// </summary>
public static partial class UpdatesEndpoints
{
    /// <summary>2 MiB — the exact guide-image cap (BL149).</summary>
    public const long MaxGuideImageBytes = 2_097_152;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.(png|jpe?g|webp)$")]
    private static partial Regex GuideImageName();

    public static void MapUpdatesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/updates", GetFeed).RequireAuthorization();
        app.MapGet("/updates/guides/img/{name}", GetGuideImage).RequireAuthorization();
    }

    private static async Task<IResult> GetFeed(
        UpdatesFeedCache cache,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var json = await cache.GetFeedJsonAsync(cancellationToken);
        http.Response.Headers.CacheControl = "private, max-age=300";
        return Results.Content(json, "application/json; charset=utf-8");
    }

    // Guard precedence (BL149; "preserve established guard ordering"):
    //   1. name fails the file-name regex          -> 404
    //   2. object missing                          -> 404 (not distinguished from an invalid name)
    //   3. storage unavailable / provider failure  -> 503
    //   4. object over the 2 MiB cap               -> 413 (detected before the MIME is known)
    //   5. stored MIME missing / disallowed / != extension -> 415
    //   6. otherwise                               -> 200 with a forced MIME
    private static async Task<IResult> GetGuideImage(
        string name,
        IUpdatesContentSource source,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!GuideImageName().IsMatch(name))
            return Results.NotFound();

        var expectedMime = ForcedMime(name);

        var fetch = await source.GetGuideImageAsync(name, MaxGuideImageBytes, cancellationToken);

        switch (fetch.Status)
        {
            case UpdatesContentStatus.Missing:
                return Results.NotFound();
            case UpdatesContentStatus.Unavailable:
            case UpdatesContentStatus.Failed:
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            case UpdatesContentStatus.TooLarge:
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            case UpdatesContentStatus.Available:
                break;
            default:
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        if (!MimeAgrees(fetch.StoredContentType, expectedMime))
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        http.Response.Headers.CacheControl = "private, max-age=86400";
        http.Response.Headers.ContentDisposition = "inline";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Bytes(fetch.Content!, expectedMime);
    }

    private static string ForcedMime(string name) =>
        name[(name.LastIndexOf('.') + 1)..].ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            _ => throw new InvalidOperationException($"Unreachable: name '{name}' passed the regex but has no known extension."),
        };

    private static bool MimeAgrees(string? storedContentType, string expectedMime)
    {
        if (string.IsNullOrWhiteSpace(storedContentType))
            return false;

        // Drop any parameters (e.g. "; charset=...") and normalise case.
        var stored = storedContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return stored == expectedMime;
    }
}
