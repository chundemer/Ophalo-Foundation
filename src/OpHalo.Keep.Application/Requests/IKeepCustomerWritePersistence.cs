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
    /// The request must have been loaded via GetRequestForUpdateAsync on the same DbContext.
    /// </summary>
    Task CommitAsync(KeepRequest request, KeepRequestEvent newEvent, CancellationToken ct);

    /// <summary>
    /// Returns all Visibility = All events for the request after commit, ordered
    /// chronologically. Used to build the updated customer page result returned to the caller.
    /// </summary>
    Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid requestId, CancellationToken ct);
}
