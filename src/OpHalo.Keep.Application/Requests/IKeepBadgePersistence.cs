using OpHalo.Keep.Application.Abstractions;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Persistence contract for the badge count service. Intent-revealing by design:
/// named methods over raw DbSets so Keep.Application stays EF-free.
/// Implemented by EfKeepBadgePersistence in Keep.Infrastructure.
/// </summary>
public interface IKeepBadgePersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);

    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns the count of requests needing attention within the given visibility scope.
    /// When <paramref name="includeClosedUnresolvedFeedback"/> is true, Closed requests with
    /// active UnresolvedFeedback attention are added to the active-attention count (Owner/Admin only).
    /// Uses CountAsync — no materialization.
    /// </summary>
    Task<int> GetBadgeCountAsync(
        Guid accountId,
        Guid accountUserId,
        KeepRequestVisibilityScope scope,
        bool includeClosedUnresolvedFeedback,
        CancellationToken ct);
}
