using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Persistence seam for <see cref="PriceBookVersion"/> and its owned <see cref="PriceBookVersionLine"/>.
/// Every read is scoped by <c>accountId</c> directly in the query, never filtered after load, so a
/// cross-account id can never resolve to another account's row.
/// </summary>
public interface IPriceBookVersionPersistence
{
    /// <summary>The account's current <c>Published</c> version, with its <see cref="PriceBookVersion.Lines"/> loaded.</summary>
    Task<PriceBookVersion?> GetCurrentPublishedAsync(Guid accountId, CancellationToken ct);

    /// <summary>The account's highest existing <c>VersionNumber</c>, or 0 if none exist — feeds the next publish's number.</summary>
    Task<int> GetLatestVersionNumberAsync(Guid accountId, CancellationToken ct);

    /// <summary>Persists a newly published version and its line.</summary>
    Task AddAsync(PriceBookVersion version, CancellationToken ct);

    /// <summary>
    /// Saves a <see cref="PriceBookVersion.Supersede"/> already applied to a version loaded via
    /// <see cref="GetCurrentPublishedAsync"/> (tracked). This entity carries no independent
    /// concurrency token — ADR-470's account-scoped <c>PriceBookAccountState</c> lock is the sole
    /// concurrency precondition for a publish transaction, not a per-row token here.
    /// </summary>
    Task CommitAsync(PriceBookVersion version, CancellationToken ct);
}
