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
    /// Returns the bounded candidate set for the default command-center list.
    /// Always includes active statuses (Received, Scheduled, InProgress, PendingCustomer, Resolved).
    /// Includes Closed + UnresolvedFeedback attention rows only when
    /// <paramref name="includeClosedUnresolvedFeedback"/> is true (Owner/Admin only).
    /// Excludes normal Closed and all Cancelled.
    /// </summary>
    Task<IReadOnlyList<KeepRequest>> GetDefaultListRequestsAsync(
        Guid accountId, bool includeClosedUnresolvedFeedback, CancellationToken ct);

    /// <summary>
    /// Returns participant summaries keyed by request ID.
    /// Requests with no active participants are omitted; callers use GetValueOrDefault.
    /// </summary>
    Task<Dictionary<Guid, KeepRequestParticipantSummary>> GetParticipantSummariesAsync(
        IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, CancellationToken ct);
}

/// <summary>
/// Aggregated participant metadata for a single request. Reflects only active
/// (non-detached) participants. CurrentUserParticipationType is null when the
/// current account user has no active participant row.
/// </summary>
public sealed record KeepRequestParticipantSummary(
    int ResponsibleCount,
    int WatchingCount,
    ParticipationType? CurrentUserParticipationType,
    bool? CurrentUserNotificationsEnabled);
