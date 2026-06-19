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
    /// Returned set is unsorted; callers apply in-memory B5 ranking sort.
    /// </summary>
    Task<IReadOnlyList<KeepRequest>> GetActiveViewRequestsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        ActiveViewKind view,
        KeepRequestListFilters filters,
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
    /// Returns role-aware operational view counts (ADR-241/259).
    /// Counts are base view counts; they do not reflect the current q/status/date filters.
    /// Operator unassigned count returns 0 in Session 4B (gated until 4C eligibility filtering).
    /// </summary>
    Task<KeepRequestViewCounts> GetViewCountsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        bool isOwnerOrAdmin,
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
    FeedbackReview
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
