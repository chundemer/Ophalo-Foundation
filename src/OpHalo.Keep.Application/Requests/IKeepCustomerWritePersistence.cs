using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Persistence contract for anonymous customer write services (B3+).
///
/// Deliberately separate from IKeepRequestOperatePersistence (operator-scoped, authenticated)
/// and IKeepRequestDetailPersistence (non-tracked reads). Customer writes need a tracked
/// entity re-fetch after guard success, response policy lookup, and commit.
///
/// GetRequestForUpdateAsync takes only requestId (no accountId) because the guard has already
/// validated token → request → account ownership before any write service is reached.
/// </summary>
public interface IKeepCustomerWritePersistence
{
    /// <summary>
    /// Returns a tracked KeepRequest for mutation by the server-resolved request id.
    /// The guard has already validated ownership; no accountId scope is needed here.
    /// Returns null only if the row was deleted between guard evaluation and this call —
    /// treat as NotFound.
    /// </summary>
    Task<KeepRequest?> GetRequestForUpdateAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// Returns the account's Keep response policy, or null if none has been configured.
    /// AddCustomerMessageService falls back to pilot defaults when null.
    /// </summary>
    Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Saves the mutated request and the new event atomically in a single SaveChangesAsync.
    /// Rotates <see cref="KeepRequest.ConcurrencyVersion"/> immediately before SaveChangesAsync.
    /// Returns <see cref="KeepRequestCommitResult.Committed"/> on success or
    /// <see cref="KeepRequestCommitResult.Conflict"/> when a concurrent writer won the race.
    /// The request must have been loaded via GetRequestForUpdateAsync on the same DbContext.
    /// </summary>
    Task<KeepRequestCommitResult> CommitAsync(KeepRequest request, KeepRequestEvent newEvent, CancellationToken ct);

    /// <summary>
    /// Saves the mutated request and the FeedbackReceived event after feedback submission.
    /// Rotates <see cref="KeepRequest.ConcurrencyVersion"/> immediately before SaveChangesAsync.
    /// Returns <see cref="KeepRequestCommitResult.Committed"/> on success or
    /// <see cref="KeepRequestCommitResult.Conflict"/> when a concurrent writer won the race.
    /// The request must have been loaded via GetRequestForUpdateAsync on the same DbContext.
    /// </summary>
    Task<KeepRequestCommitResult> CommitFeedbackAsync(KeepRequest request, KeepRequestEvent feedbackEvent, CancellationToken ct);

    /// <summary>
    /// Returns all Visibility = All events for the request after commit, ordered
    /// chronologically. Used to build the updated customer page result returned to the caller.
    /// </summary>
    Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// Persists a page-view telemetry update (ADR-341). Does NOT rotate ConcurrencyVersion —
    /// page views must not produce stale-version conflicts for concurrent operator writes.
    /// Silently absorbs DbUpdateConcurrencyException: a concurrent operator write wins and
    /// the telemetry write is lost; this is acceptable for a best-effort signal.
    /// </summary>
    Task CommitPageViewAsync(KeepRequest request, CancellationToken ct);

    // --- Notification routing support (S8d) ---

    /// <summary>
    /// Returns all participants for the request (active and detached) joined with AccountUser
    /// for role, membership, and notification-opt-out. Used to build push routing context
    /// after commit without crossing DbContext scope boundaries.
    /// </summary>
    Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// Returns all Active Owner/Admin members of the account. Used as the Owner/Admin fallback
    /// list in push routing. Returns only Owner and Admin roles — Operators are excluded because
    /// they are not Owner/Admin fallback recipients for unresolved-feedback notifications.
    /// </summary>
    Task<IReadOnlyList<ParticipantCandidateRecord>> GetActiveOwnerAdminMembersAsync(Guid accountId, CancellationToken ct);
}
