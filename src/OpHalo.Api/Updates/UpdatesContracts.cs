using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpHalo.Api.Updates;

/// <summary>
/// Wire contract for <c>GET /updates</c> (GAP-038, BL149). Field names are snake_case to match
/// the published feed document; source-side defaults (<c>status: "active"</c>,
/// <c>highlight: false</c>) are materialised here so every client sees a complete row. An
/// omitted <c>banner_until</c> stays absent.
/// </summary>
public sealed record UpdatesFeedResponse(
    int Schema,
    IReadOnlyList<UpdateEntryResponse> Entries,
    IReadOnlyList<UpdateGuideResponse> Guides);

/// <param name="PublishedAt">RFC 3339 date-time, preserved verbatim from the validated feed.</param>
public sealed record UpdateEntryResponse(
    string Id,
    string PublishedAt,
    string Section,
    string Title,
    string Body,
    string Status = "active",
    bool Highlight = false,
    string? BannerUntil = null);

/// <param name="UpdatedAt">RFC 3339 date-time, preserved verbatim from the validated feed.</param>
public sealed record UpdateGuideResponse(
    string Id,
    string UpdatedAt,
    string Title,
    string Body);

/// <summary>Shared JSON shape rules for the feed contract.</summary>
public static class UpdatesJson
{
    /// <summary>
    /// snake_case names, nulls dropped (so an absent <c>banner_until</c> is omitted, not
    /// rendered as <c>null</c>). Used for both parsing the feed document and writing the response.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The contracted payload when no instance has ever held a valid feed.</summary>
    public const string EmptyFeed = """{"schema":1,"entries":[],"guides":[]}""";
}
