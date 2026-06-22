using OpHalo.Foundation.Core.Entities.Accounts.Enums;
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
    /// Returns a tracked KeepRequest for mutation, applying row-authorization scope before
    /// filtering to the requested ID. Null if not found, cross-account, or invisible under scope.
    /// All cases are intentionally indistinguishable at this layer.
    /// </summary>
    Task<KeepRequest?> GetVisibleRequestForUpdateAsync(
        Guid requestId, Guid accountId, Guid currentAccountUserId,
        KeepRequestVisibilityScope scope, CancellationToken ct);

    /// <summary>
    /// Returns the response policy for the account, or null if no policy row exists.
    /// Callers fall back to pilot defaults (standard=240 min).
    /// </summary>
    Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Persists the mutated request and, when provided, the new event in a single
    /// SaveChangesAsync. Rotates ConcurrencyVersion immediately before saving; the pre-rotation
    /// value remains in the EF concurrency-token predicate. Returns <see cref="KeepRequestCommitResult.Committed"/>
    /// on success or <see cref="KeepRequestCommitResult.Conflict"/> when a concurrent write wins
    /// the race (DbUpdateConcurrencyException); all other exceptions propagate. The request must
    /// have been loaded via GetVisibleRequestForUpdateAsync on the same DbContext instance.
    /// </summary>
    Task<KeepRequestCommitResult> CommitAsync(KeepRequest request, KeepRequestEvent? newEvent, CancellationToken ct);

    // --- Participation write support (Session 3B) ---

    /// <summary>
    /// Returns all participation rows for the request as tracked entities.
    /// The persistence implementation saves changes to these rows when CommitParticipationAsync
    /// is called on the same DbContext instance.
    /// </summary>
    Task<List<KeepRequestParticipant>> GetParticipantsForUpdateAsync(Guid requestId, Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns compact account-scoped info for a candidate participant, or null if the
    /// AccountUser does not belong to the account or does not exist.
    /// Used to validate target eligibility and obtain a display-name snapshot.
    /// </summary>
    Task<ParticipantTargetInfo?> GetParticipantTargetAsync(Guid accountUserId, Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns all Active Owner/Admin/Operator members of the account ordered by display name.
    /// Used by the participant-candidates lookup endpoint (ADR-235).
    /// </summary>
    Task<IReadOnlyList<ParticipantCandidateRecord>> GetParticipantCandidatesAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Inserts newParticipants and newEvent (when non-null) and saves all tracked
    /// participant-row changes (mutations by the domain service) in a single SaveChangesAsync.
    /// Does not require a KeepRequest — participation writes do not modify request state.
    /// </summary>
    Task CommitParticipationAsync(
        IReadOnlyList<KeepRequestParticipant> newParticipants,
        KeepRequestEvent? newEvent,
        CancellationToken ct);

    /// <summary>
    /// Versioned participation commit (G5c/ADR-333). Inserts newParticipants and newEvent (when
    /// non-null), rotates <paramref name="request"/>'s ConcurrencyVersion immediately before
    /// SaveChangesAsync, and returns <see cref="KeepRequestCommitResult.Committed"/> on success
    /// or <see cref="KeepRequestCommitResult.Conflict"/> on DbUpdateConcurrencyException.
    /// The request must have been loaded via GetVisibleRequestForUpdateAsync on the same
    /// DbContext instance. All other exceptions propagate.
    /// </summary>
    Task<KeepRequestCommitResult> CommitParticipationAsync(
        KeepRequest request,
        IReadOnlyList<KeepRequestParticipant> newParticipants,
        KeepRequestEvent? newEvent,
        CancellationToken ct);
}

/// <summary>
/// Account-scoped target user info used to validate eligibility and snapshot display name.
/// </summary>
public sealed record ParticipantTargetInfo(
    Guid AccountUserId,
    string DisplayName,
    AccountUserRole Role,
    MembershipStatus MembershipStatus);

/// <summary>
/// Compact candidate record returned by GetParticipantCandidatesAsync.
/// </summary>
public sealed record ParticipantCandidateRecord(
    Guid AccountUserId,
    string DisplayName,
    AccountUserRole Role);
