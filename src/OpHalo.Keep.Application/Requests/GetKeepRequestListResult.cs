namespace OpHalo.Keep.Application.Requests;

public sealed record GetKeepRequestListResult(
    IReadOnlyList<KeepRequestSummary> Requests,
    KeepRequestPageInfo PageInfo,
    KeepRequestViewCounts? ViewCounts,
    KeepRequestListContext ListContext);

/// <summary>Cursor pagination metadata returned on every list response (ADR-249).</summary>
public sealed record KeepRequestPageInfo(
    int Limit,
    bool HasMore,
    string? NextCursor);

/// <summary>
/// Role-aware operational view counts (ADR-241/259).
/// Null until Session 4B wires the count queries.
/// Never zero-filled: zero means computed-and-empty; null means not-yet-computed.
/// </summary>
public sealed record KeepRequestViewCounts(
    int Default,
    int AssignedToMe,
    int Watching,
    int Unassigned,
    int NeedsAttention,
    int FeedbackReview);

/// <summary>Describes the query mode in effect for the returned page (ADR-253).</summary>
public sealed record KeepRequestListContext(
    string View,
    bool IsDefaultCommandCenter,
    bool IsHistory,
    bool IsSearch);
