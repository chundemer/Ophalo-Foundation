using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;

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
    /// Returns all non-terminal requests for the account, ordered by LastBusinessActivityAt DESC.
    /// Terminal = Closed or Cancelled.
    /// </summary>
    Task<IReadOnlyList<KeepRequest>> GetOpenRequestsAsync(Guid accountId, CancellationToken ct);
}
