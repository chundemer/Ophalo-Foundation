using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PublicIntake;

/// <summary>
/// Persistence contract for the public intake service. Intent-revealing by
/// design: named methods over raw DbSets so Keep.Application stays EF-free.
/// Implemented by KeepIntakePersistence in Keep.Infrastructure.
/// </summary>
public interface IKeepIntakePersistence
{
    /// <summary>
    /// Returns the active (non-revoked, non-deleted) intake link matching
    /// the given token hash, or null if none exists.
    /// </summary>
    Task<KeepPublicIntakeLink?> FindActivePublicIntakeLinkByTokenHashAsync(
        string tokenHash, CancellationToken ct);

    /// <summary>
    /// Resolves an intake link by slug: checks the current active slug on
    /// <see cref="OpHalo.Keep.Core.Entities.KeepPublicIntakeLink"/> first, then
    /// active aliases in <c>keep_public_intake_slug_aliases</c>. Input is
    /// normalized to lowercase before querying. Returns null if no match.
    /// </summary>
    Task<KeepPublicIntakeLink?> FindActivePublicIntakeLinkBySlugAsync(
        string slug, CancellationToken ct);

    /// <summary>
    /// Combines Account + AccountEntitlements into a single snapshot for
    /// policy composition. Returns null if either row is missing.
    /// </summary>
    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
        Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns the tracked customer for (accountId, canonicalPhone), or null
    /// if no customer exists yet. Tracked (not AsNoTracking) so a subsequent
    /// UpdateContactInfo call is persisted by CommitPublicIntakeAsync.
    /// </summary>
    Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(
        Guid accountId, string canonicalPhone, CancellationToken ct);

    /// <summary>
    /// Returns the response policy for the account, or null if no policy row exists.
    /// Callers should fall back to pilot defaults (first=60, standard=240, priority=60 min).
    /// </summary>
    Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct);

    Task<bool> PageTokenExistsAsync(string pageToken, CancellationToken ct);

    Task<bool> ReferenceCodeExistsAsync(Guid accountId, string referenceCode, CancellationToken ct);

    /// <summary>
    /// Atomically persists customer (add or tracked update), request, and
    /// request-created event in one transaction.
    ///
    /// Returns <see cref="PublicIntakeCommitResult.UniqueTokenCollision"/> if
    /// a unique constraint on page_token or (account_id, reference_code) fires;
    /// the implementation detaches the failed added entities so the caller can
    /// retry with new tokens. All other failures propagate as exceptions.
    /// </summary>
    Task<PublicIntakeCommitResult> CommitPublicIntakeAsync(
        KeepCustomer customer,
        KeepRequest request,
        KeepRequestEvent requestEvent,
        CancellationToken ct);
}
