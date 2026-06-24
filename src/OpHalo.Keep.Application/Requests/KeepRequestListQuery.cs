namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Parsed query parameters for GET /keep/requests.
/// Omitting view, or passing "default", selects the command-center list.
/// Date fields are raw strings; format is validated by the service (ADR-258).
/// </summary>
public sealed record KeepRequestListQuery(
    string? View = null,
    string? Status = null,
    string? AttentionReason = null,
    Guid? AssignedAccountUserId = null,
    string? Q = null,
    string? CreatedFrom = null,
    string? CreatedTo = null,
    string? ClosedFrom = null,
    string? ClosedTo = null,
    string? ClosedShortcut = null,
    int? Limit = null,
    string? Cursor = null);
