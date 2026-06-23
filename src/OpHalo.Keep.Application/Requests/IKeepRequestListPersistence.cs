using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Persistence contract for the operator request list service. Intent-revealing by
/// design: named methods over raw DbSets so Keep.Application stays EF-free.
/// Implemented by KeepRequestListPersistence in Keep.Infrastructure.
/// </summary>
public interface IKeepRequestListPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);

    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns the bounded candidate set for the default command-center list (Session 4A path,
    /// kept for interface stability; service uses GetActiveViewRequestsAsync from 4B onwards).
    /// </summary>
    Task<IReadOnlyList<KeepRequest>> GetDefaultListRequestsAsync(
        Guid accountId, bool includeClosedUnresolvedFeedback, CancellationToken ct);

    /// <summary>
    /// Returns all rows matching the named active view + additional filters (ADR-239/250).
    /// Infrastructure applies <paramref name="scope"/> as the base predicate before view filters.
    /// Returned set is unsorted; callers apply in-memory B5 ranking sort.
    /// </summary>
    Task<IReadOnlyList<KeepRequest>> GetActiveViewRequestsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        ActiveViewKind view,
        KeepRequestListFilters filters,
        KeepRequestVisibilityScope scope,
        CancellationToken ct);

    /// <summary>
    /// Returns up to <paramref name="fetchCount"/> history rows in TerminatedAtUtc DESC order,
    /// optionally starting after the supplied keyset cursor (ADR-249/260).
    /// Rows with null TerminatedAtUtc are excluded even if status is terminal.
    /// </summary>
    Task<IReadOnlyList<KeepRequest>> GetHistoryRequestsAsync(
        Guid accountId,
        HistoryViewKind view,
        KeepRequestListFilters filters,
        DateTime? cursorTerminatedAt,
        Guid? cursorLastId,
        int fetchCount,
        CancellationToken ct);

    /// <summary>
    /// Returns role-aware operational view counts (ADR-241/259, G4d).
    /// Counts are base view counts; they do not reflect the current q/status/date filters.
    /// Default/NeedsAttention counts are scoped by <paramref name="scope"/>.
    /// Unassigned: AccountWide+isOwnerOrAdmin → effective-unassignment count;
    ///             MyWork (Operator) → Available count; Viewer → 0.
    /// </summary>
    Task<KeepRequestViewCounts> GetViewCountsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        bool isOwnerOrAdmin,
        KeepRequestVisibilityScope scope,
        CancellationToken ct);

    /// <summary>
    /// Returns up to <paramref name="fetchCount"/> Available items in CreatedAtUtc ASC, Id ASC order,
    /// starting after the supplied keyset cursor. Available = non-terminal, no active eligible
    /// Responsible, current user is active eligible. Projects only bounded fields (G4d ADR-325).
    /// </summary>
    Task<IReadOnlyList<KeepRequestAvailableRow>> GetAvailableRequestsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        int fetchCount,
        DateTime? cursorCreatedAtUtc,
        Guid? cursorRequestId,
        CancellationToken ct);

    /// <summary>
    /// Returns participant summaries keyed by request ID.
    /// Requests with no active participants are omitted; callers use GetValueOrDefault.
    /// accountId is used to scope the AccountUser eligibility lookup against cross-account data corruption.
    /// </summary>
    Task<Dictionary<Guid, KeepRequestParticipantSummary>> GetParticipantSummariesAsync(
        IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, Guid accountId, CancellationToken ct);
}

/// <summary>Selects the active (non-history) view for GetActiveViewRequestsAsync (ADR-239).</summary>
public enum ActiveViewKind
{
    Default,
    AssignedToMe,
    Watching,
    Unassigned,
    NeedsAttention,
    FeedbackReview,
    NeedsStatusCheck
}

/// <summary>Selects the history view for GetHistoryRequestsAsync (ADR-246).</summary>
public enum HistoryViewKind
{
    Closed,
    Cancelled,
    All
}

/// <summary>
/// Parsed, validated filter parameters for the request list query.
/// Date fields are UTC offsets; the persistence layer converts to UTC DateTime for DB comparison.
/// Q is trimmed and non-empty (null when no search term was provided).
/// IsOwnerOrAdmin gates feedback comment search visibility (ADR-247).
/// </summary>
public sealed record KeepRequestListFilters(
    KeepRequestStatus? Status = null,
    AttentionReason? AttentionReason = null,
    Guid? AssignedAccountUserId = null,
    string? Q = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    DateTimeOffset? ClosedFrom = null,
    DateTimeOffset? ClosedTo = null,
    bool IsOwnerOrAdmin = false);

/// <summary>
/// Aggregated participant metadata for a single request. Reflects only active
/// (non-detached) participants. CurrentUserParticipationType is null when the
/// current account user has no active participant row. ResponsibleDisplayName is
/// null when unassigned; ResponsibleIsStale is true when the responsible user is
/// no longer Active Owner/Admin/Operator (ADR-226).
/// </summary>
public sealed record KeepRequestParticipantSummary(
    int ResponsibleCount,
    int WatchingCount,
    ParticipationType? CurrentUserParticipationType,
    bool? CurrentUserNotificationsEnabled,
    string? ResponsibleDisplayName,
    bool ResponsibleIsStale);

/// <summary>
/// Narrow projection returned by GetAvailableRequestsAsync. Contains only the locked
/// Available fields plus a bounded description prefix for preview construction.
/// No customer contact, events, feedback, page token, or participation data is exposed (G4d).
/// CurrentUserIsWatching is an internal SQL-computed flag (never surfaced in the API contract)
/// used only to align the per-row CanWatch affordance with the shared action policy: an Available
/// row the current user is already Watching must report CanWatch=false (G4e-3 correction). On an
/// Available row the current user is never the eligible Responsible, so this flag fully captures
/// the policy's "no current participation" condition for CanWatch.
/// </summary>
public sealed record KeepRequestAvailableRow(
    Guid RequestId,
    string ReferenceCode,
    string CustomerName,
    KeepRequestStatus Status,
    DateTime CreatedAtUtc,
    DateTime? AttentionSinceUtc,
    DateTime? NextAttentionAtUtc,
    PriorityBand PriorityBand,
    AttentionLevel AttentionLevel,
    Guid Version,
    string RawDescriptionPrefix,
    bool DescriptionWasTruncated,
    bool CurrentUserIsWatching);
