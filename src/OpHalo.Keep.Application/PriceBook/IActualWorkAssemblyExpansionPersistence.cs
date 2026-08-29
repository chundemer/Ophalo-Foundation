namespace OpHalo.Keep.Application.PriceBook;

public enum ActualWorkExpandAssemblyResult
{
    Committed,
    NotFound,
    VersionMismatch,
    NotDraft,

    /// <summary>The caller is not the Draft's current recorder (GAP-055, superseding the
    /// active-Responsible-only recorder rule), checked directly against the just-locked Draft row's
    /// <c>RecorderAccountUserId</c>. Maps to the same indistinguishable-not-found error as an
    /// unknown <c>actualWorkId</c>, never a distinguishable 403.</summary>
    NotRecorder,

    AssemblyNotFound,

    /// <summary>ADR-494 D2 (4c-i-a-2): the row-locked Draft has no persisted "Performed by" default,
    /// so assembly expansion — which attributes every line it creates to that default — cannot
    /// proceed. Returned before any <c>AddLine</c>/write; zero lines are written and the transaction
    /// is rolled back. Distinct from <see cref="NotDraft"/>: the visit is a Draft, it just lacks a
    /// performer. Maps to <c>ActualWork.PerformerRequired</c>.</summary>
    PerformerRequired,

    /// <summary>The ADR-479 operational-eligibility predicate, recomputed from the row-locked
    /// assembly/catalog-item state, failed. Zero lines are ever written for this outcome.</summary>
    AssemblyNotOperationallyEligible,

    /// <summary>One or more submitted inclusion ids do not name a current <em>optional</em>
    /// associated item on this assembly (unknown id, or a required item's id) — zero lines
    /// written.</summary>
    InvalidInclusion,
}

/// <summary><see cref="ConcurrencyVersion"/> is set only when <see cref="Result"/> is
/// <see cref="ActualWorkExpandAssemblyResult.Committed"/>. <see cref="SkippedCatalogItemIds"/> lists
/// every candidate catalog item that was already present on the Draft (skip-and-report, locked
/// 2026-08-20) — distinct from a failure outcome, since the expansion still succeeds for the
/// remaining components.</summary>
public sealed record ActualWorkExpandAssemblyOutcome(
    ActualWorkExpandAssemblyResult Result,
    IReadOnlyList<Guid>? LineIds = null,
    IReadOnlyList<Guid>? SkippedCatalogItemIds = null,
    Guid? ConcurrencyVersion = null);

/// <summary>
/// Owns the entire atomic <c>expand-assembly</c> transaction as one boundary (build-log/129's 5d-i
/// preflight lock), mirroring <see cref="IOfferingAssemblyExpansionPersistence"/>'s "Assembly-
/// expansion locking protocol": row-locks the <c>ActualWork</c> Draft (the first tracked load of
/// that aggregate anywhere in the call path — <see cref="ActualWorkDraftApiService"/>'s gate reads
/// no row), then the recorder-ownership check (GAP-055) against that just-locked row, then the
/// <c>OfferingAssembly</c> and every referenced <c>CatalogItem</c> (ascending id order), re-checks
/// ADR-479 operational eligibility from those locked rows, then skip-and-reports any candidate
/// already present on the Draft's just-locked <c>Lines</c> and appends the rest with the same
/// per-item snapshot resolution a manual add uses — all inside one database transaction, committed
/// or rolled back together. No caller ever sees a transaction object.
/// </summary>
public interface IActualWorkAssemblyExpansionPersistence
{
    Task<ActualWorkExpandAssemblyOutcome> ExpandAsync(
        Guid accountId,
        Guid actualWorkId,
        Guid expectedVersion,
        Guid offeringAssemblyId,
        IReadOnlyCollection<Guid> includedOptionalItemIds,
        Guid callerAccountUserId,
        CancellationToken ct);
}
