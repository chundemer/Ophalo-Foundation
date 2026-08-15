using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Distinct commit outcomes for <see cref="IQuickScopeActionPersistence.ReplaceSetAsync"/>
/// (build-log/119). The database is the actual guard for each of these — the in-domain
/// <see cref="QuickScopeActionSetValidator"/> check a caller runs first narrows the common case,
/// but a race between two concurrent Owner/Admin writes, or any future write path that bypasses
/// the validator, must still be caught here rather than surfacing as an unhandled 500.
/// </summary>
public enum QuickScopeActionCommitResult
{
    Committed,

    /// <summary>Two rows in the proposed set claimed the same <c>Order</c> — the
    /// <c>(AccountId, Order)</c> unique index.</summary>
    OrderConflict,

    /// <summary>Two rows in the proposed set targeted the same catalog item or offering/assembly —
    /// the per-account target unique indexes.</summary>
    TargetConflict,

    /// <summary>A row targeted zero or both of <c>CatalogItemId</c>/<c>OfferingAssemblyId</c> — the
    /// database exclusive-target check constraint. Not reachable through
    /// <see cref="QuickScopeAction.Create"/>, which enforces the same rule in-domain; this exists
    /// for a write path that bypasses it.</summary>
    ExclusiveTargetViolation,
}

/// <summary>
/// Persistence seam for the account's <see cref="QuickScopeAction"/> set (build-log/119). Every
/// read/write is scoped by <c>accountId</c> directly in the query, never filtered after load, so a
/// cross-account id can never resolve to another account's row. No API layer consumes this yet
/// (Session 2 is persistence/domain-only) — the Session 3 configuration read/write service and
/// field read are the first callers.
/// </summary>
public interface IQuickScopeActionPersistence
{
    /// <summary>Returns the account's configured slots ordered by <c>Order</c>. Empty for an
    /// account with no configuration — never null.</summary>
    Task<IReadOnlyList<QuickScopeAction>> ListForAccountAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Atomically replaces the account's entire configured set with <paramref name="actions"/> —
    /// delete-all-then-insert in one transaction, never an incremental per-slot diff. Every row in
    /// <paramref name="actions"/> must already carry the same <c>AccountId</c>; a mismatch is a
    /// caller bug, not a runtime-handled case. Empty <paramref name="actions"/> clears the account's
    /// configuration.
    /// </summary>
    Task<QuickScopeActionCommitResult> ReplaceSetAsync(
        Guid accountId, IReadOnlyList<QuickScopeAction> actions, CancellationToken ct);
}
