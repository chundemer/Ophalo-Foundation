namespace OpHalo.Keep.Application.PriceBook;

public enum ExpandAssemblyResult
{
    Committed,
    ScopeNotFound,
    VersionMismatch,
    NotDraft,
    AssemblyNotFound,

    /// <summary>The ADR-479 operational-eligibility predicate, recomputed from the row-locked
    /// assembly/catalog-item state, failed — the assembly itself is no longer <c>Active</c>, or a
    /// referenced catalog item went <c>Inactive</c>/lost its required price since the caller's own
    /// (uncommitted, pre-transaction) eligibility read. Zero lines are ever written for this
    /// outcome.</summary>
    AssemblyNotOperationallyEligible,

    /// <summary>One or more submitted exclusion ids do not name a current <em>optional</em>
    /// associated item on this assembly (unknown id, or a required item's id) — zero lines
    /// written.</summary>
    InvalidExclusion,
}

/// <summary><see cref="ConcurrencyVersion"/> is set only when <see cref="Result"/> is
/// <see cref="ExpandAssemblyResult.Committed"/>.</summary>
public sealed record ExpandAssemblyOutcome(
    ExpandAssemblyResult Result, IReadOnlyList<Guid>? LineIds = null, Guid? ConcurrencyVersion = null);

/// <summary>
/// Owns the entire atomic <c>expand-assembly</c> transaction as one boundary (Session 3.4e,
/// build-log/118 "Assembly-expansion locking protocol"): row-locks the <c>ProposedScope</c>, then
/// the <c>OfferingAssembly</c> and every referenced <c>CatalogItem</c> (ascending id order),
/// re-checks ADR-479 operational eligibility from those locked rows (never the caller's
/// pre-transaction read), then appends the <c>PrimaryOffering</c> line plus every non-excluded
/// <c>AssociatedItem</c> line and bumps the scope's <see cref="ExpandAssemblyOutcome.ConcurrencyVersion"/>
/// once — all inside one database transaction, committed or rolled back together. Matches the
/// <c>IProposedScopeSubmissionPersistence</c> pattern for an operation that must be atomic across
/// more than one aggregate. No caller ever sees a transaction object.
/// </summary>
public interface IOfferingAssemblyExpansionPersistence
{
    Task<ExpandAssemblyOutcome> ExpandAsync(
        Guid accountId,
        Guid proposedScopeId,
        Guid expectedVersion,
        Guid offeringAssemblyId,
        IReadOnlyCollection<Guid> excludedOptionalItemIds,
        Guid createdByUserId,
        CancellationToken ct);
}
