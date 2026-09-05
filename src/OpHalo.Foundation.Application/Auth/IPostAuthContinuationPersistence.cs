using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Persistence seam for PostAuthContinuation (ADR-497). Keeps the Application layer free of
/// DbContext references (architecture boundary §8 — Application must not depend on Infrastructure).
/// </summary>
public interface IPostAuthContinuationPersistence
{
    /// <summary>
    /// Opportunistically deletes up to 100 rows that are consumed or expired more than 24 hours
    /// ago, then persists the new continuation. No hosted/background cleanup job backs this table —
    /// every creation call bears the bounded cleanup cost instead.
    /// </summary>
    Task CreateAsync(PostAuthContinuation continuation, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a continuation by its SHA-256 hash. Returns null if not found. Uses AsNoTracking —
    /// callers that need to consume it use ConsumeAsync.
    /// </summary>
    Task<PostAuthContinuation?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically sets ConsumedAtUtc using ExecuteUpdateAsync conditioned on the continuation
    /// being unconsumed. Returns true if this call won the race, false if another concurrent
    /// request consumed it first.
    /// </summary>
    Task<bool> ConsumeAsync(Guid continuationId, DateTime consumedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Terminally deletes a continuation row. Unlike AccountAuthCode, spent and presented-expired
    /// continuations are removed immediately rather than retained — callers use this after a
    /// successful ConsumeAsync and when a presented continuation is found expired.
    /// </summary>
    Task DeleteAsync(Guid continuationId, CancellationToken cancellationToken);
}
