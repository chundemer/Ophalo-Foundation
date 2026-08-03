using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Account-scoped optimistic publish lock (ADR-470, build-log/111). A price-book publish or
/// manual <see cref="CatalogItem"/> price-override transaction reads this row, holds
/// <see cref="PublishLockVersion"/> as its concurrency precondition, and bumps it before commit —
/// the single deterministic conflict point for a transaction that may touch many
/// <c>CatalogItem</c> rows at once, so a competing transaction against a stale lock fails closed
/// rather than partially writing or silently overwriting. Created lazily on first publish/
/// override, never on account creation.
/// </summary>
public sealed class PriceBookAccountState : BaseEntity
{
    public Guid AccountId { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <see cref="CatalogItem.ConcurrencyVersion"/> (ADR-330), but account-scoped rather than
    /// row-scoped: it is the single lock a publish/override transaction holds (ADR-470).
    /// </summary>
    public Guid PublishLockVersion { get; private set; } = Guid.NewGuid();

    private PriceBookAccountState()
    {
    }

    public static PriceBookAccountState Create(Guid accountId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));

        return new PriceBookAccountState
        {
            AccountId = accountId,
            PublishLockVersion = Guid.NewGuid(),
        };
    }

    /// <summary>Advances the lock to a new token, as every publish/override transaction must (ADR-470).</summary>
    public void Bump() => PublishLockVersion = Guid.NewGuid();
}
