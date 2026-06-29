using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Requests;

public interface IKeepBusinessRequestPersistence
{
    /// <summary>
    /// Returns the tracked customer for (accountId, canonicalPhone), or null if none exists yet.
    /// Tracked (not AsNoTracking) so a subsequent UpdateContactInfo call is persisted by CommitBusinessRequestAsync.
    /// </summary>
    Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(
        Guid accountId, string canonicalPhone, CancellationToken ct);

    /// <summary>
    /// Returns up to <paramref name="take"/> non-terminal requests for the given customer,
    /// ordered by max(LastBusinessActivityAt, LastCustomerActivityAt) DESC, CreatedAtUtc DESC.
    /// AsNoTracking — read-only projection for the phone lookup gate.
    /// </summary>
    Task<IReadOnlyList<KeepRequest>> FindActiveRequestsByCustomerIdAsync(
        Guid accountId, Guid customerId, int take, CancellationToken ct);

    Task<bool> PageTokenExistsAsync(string pageToken, CancellationToken ct);

    Task<bool> ReferenceCodeExistsAsync(Guid accountId, string referenceCode, CancellationToken ct);

    /// <summary>
    /// Atomically persists customer (add or tracked update), request, and request-created event.
    ///
    /// Returns <see cref="BusinessRequestCommitResult.UniqueTokenCollision"/> when a unique
    /// constraint on page_token or (account_id, reference_code) fires; detaches failed entities
    /// so the caller can retry with new tokens.
    /// Returns <see cref="BusinessRequestCommitResult.CustomerCanonicalPhoneCollision"/> when a
    /// concurrent insert wins the customer row; detaches all failed entities so the caller can
    /// re-read the winning customer and retry.
    /// All other failures propagate as exceptions.
    /// </summary>
    Task<BusinessRequestCommitResult> CommitBusinessRequestAsync(
        KeepCustomer customer,
        KeepRequest request,
        KeepRequestEvent requestEvent,
        CancellationToken ct);
}
