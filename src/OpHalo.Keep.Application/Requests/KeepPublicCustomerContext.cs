using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// The safe, non-tracked projection returned by KeepPublicCustomerAccessGuard after a
/// successful token validation and account/feature/expiry check.
///
/// Carries all customer-page state loaded during the guard evaluation so that callers
/// (GetKeepCustomerPageService, AddCustomerMessageService) do not need a second query
/// against the request. EF tracking is never exposed here (ADR-119).
///
/// When IsExpired is true, only BusinessName, ReferenceCode, IsExpired, and ExpiresAtUtc
/// are meaningful; all other fields reflect the terminal request at expiry time but are
/// not safe to surface individually — callers must build the tombstone page shape and
/// return AllowedActions = null (ADR-120, ADR-126).
/// </summary>
public sealed record KeepPublicCustomerContext(
    Guid RequestId,
    Guid AccountId,
    string ReferenceCode,
    string BusinessName,
    string CustomerName,
    KeepRequestStatus Status,
    string? Description,
    string? CurrentStatusText,
    bool IsTerminal,
    bool IsExpired,
    DateTime? ExpiresAtUtc,
    bool? FeedbackWasResolved,
    DateTime? FeedbackSubmittedAtUtc,
    bool IsOffSeason,
    IntakeUrgency IntakeUrgency,
    KeepRequestOrigin Origin,
    // Null when IsExpired — the tombstone cannot perform mutations and must not
    // disclose concurrency state (ADR-333).
    Guid? Version);
