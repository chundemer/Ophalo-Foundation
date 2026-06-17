using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Persistence contract for operator write services (B2+). Intentionally separate from
/// IKeepRequestDetailPersistence: write operations need tracked entities; read operations
/// after commit re-use IKeepRequestDetailPersistence on the same DbContext scope.
///
/// Snapshot methods are duplicated from IKeepRequestListPersistence and
/// IKeepRequestDetailPersistence — no shared base until a fourth service repeats the
/// pattern (Option A, chosen 2026-06-16 per session log).
/// </summary>
public interface IKeepRequestOperatePersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);

    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns the operator's display name (email) for stamping onto events.
    /// Null only if the AccountUser no longer exists — treated as a 403 by the service.
    /// </summary>
    Task<string?> GetActorDisplayNameAsync(Guid accountUserId, CancellationToken ct);

    /// <summary>
    /// Returns a tracked KeepRequest for mutation. Null if not found or cross-account.
    /// Cross-account and not-found are intentionally indistinguishable at this layer.
    /// </summary>
    Task<KeepRequest?> GetRequestForUpdateAsync(Guid requestId, Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns the response policy for the account, or null if no policy row exists.
    /// Callers fall back to pilot defaults (standard=240 min).
    /// </summary>
    Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Persists the mutated request and, when provided, the new event in a single
    /// SaveChangesAsync. The request must have been loaded via GetRequestForUpdateAsync
    /// on the same DbContext instance.
    /// </summary>
    Task CommitAsync(KeepRequest request, KeepRequestEvent? newEvent, CancellationToken ct);
}
