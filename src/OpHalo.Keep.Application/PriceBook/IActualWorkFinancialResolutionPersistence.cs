using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Outcome of the transactional <see cref="IActualWorkFinancialResolutionPersistence.CreateResolutionAsync"/>
/// orchestrator (BL135 §4 Batch 3a-ii). <see cref="ActualWorkResolutionResult.Committed"/> is the
/// only success; every other value maps to a stable API error.</summary>
public enum ActualWorkResolutionResult
{
    Committed,

    /// <summary>No visit for <c>(accountId, actualWorkId)</c>.</summary>
    VisitNotFound,

    /// <summary>The loaded visit's <c>ConcurrencyVersion</c> did not match the caller's expected
    /// version, or the row changed under the save (EF concurrency-token mismatch).</summary>
    VersionMismatch,

    /// <summary>The visit is still <c>Draft</c> — resolutions apply only to a submitted visit.</summary>
    VisitNotSubmitted,

    /// <summary>Drift D5: the visit has already been financially reviewed.</summary>
    VisitAlreadyReviewed,

    /// <summary>The targeted line id is not one of the loaded visit's lines.</summary>
    LineNotFoundOnVisit,

    /// <summary>A targeted component already has a non-null captured snapshot on the line — a
    /// resolution may only fill a missing component.</summary>
    SnapshotComponentAlreadyValid,
}

/// <summary>The immutable field payload of one financial-resolution append (BL135 §4 Batch 3a-ii).
/// <see cref="Basis"/> is the raw request string, parsed to <see cref="FinancialResolutionBasis"/>
/// by <c>ActualWorkFinancialResolutionApiService</c>. At least one resolved component must be
/// non-null (enforced by <see cref="ActualWorkLineFinancialResolution.Create"/>); the orchestrator
/// additionally rejects resolving a component whose snapshot is already present.</summary>
public sealed record ActualWorkFinancialResolutionCommand(
    decimal? ResolvedUnitSellPrice,
    decimal? ResolvedUnitStandardExpectedDirectCost,
    string? Basis,
    string? Reason);

/// <summary><see cref="NewVisitConcurrencyVersion"/> is set only when <see cref="Result"/> is
/// <see cref="ActualWorkResolutionResult.Committed"/> — the visit token after the append, which the
/// review card must echo on a subsequent <c>POST .../review</c>.</summary>
public sealed record ActualWorkResolutionOutcome(
    ActualWorkResolutionResult Result, Guid? NewVisitConcurrencyVersion = null);

/// <summary>
/// Persistence seam for per-line financial resolutions and visit-level office financial
/// dispositions on submitted Actual Work visits (ADR-493 / build-log/129, build-log/135 §4
/// Batch 2). Reads are account-scoped in the query, never filtered after load. The two append
/// methods stage one immutable row into the caller's tracked change set and return without
/// saving — Batch 3a-ii / 3b-i load the visit, enforce the review/version guards, append, and
/// commit the unit of work. No method here opens or commits a transaction.
/// </summary>
public interface IActualWorkFinancialResolutionPersistence
{
    /// <summary>Every resolution row for the visit, newest-first (<c>ResolvedAtUtc DESC, Id DESC</c>),
    /// for the component-by-component effective-state projection: the effective resolved sell price
    /// is the most-recent row with a non-null <see cref="ActualWorkLineFinancialResolution.ResolvedUnitSellPrice"/>,
    /// the effective resolved direct cost the most-recent row with a non-null
    /// <see cref="ActualWorkLineFinancialResolution.ResolvedUnitStandardExpectedDirectCost"/> —
    /// resolved independently, each carrying its own provenance row. Empty when nothing has been
    /// resolved for the visit.</summary>
    Task<IReadOnlyList<ActualWorkLineFinancialResolution>> GetResolutionsForVisitAsync(
        Guid accountId, Guid actualWorkId, CancellationToken ct);

    /// <summary>Every office financial disposition row for the visit, newest-first
    /// (<c>DisposedAtUtc DESC, Id DESC</c>); the effective disposition is the first element. Empty
    /// when the visit has not been disposed.</summary>
    Task<IReadOnlyList<ActualWorkOfficeFinancialDisposition>> GetDispositionsForVisitAsync(
        Guid accountId, Guid actualWorkId, CancellationToken ct);

    /// <summary>
    /// Transactional orchestrator for a single financial-resolution append (BL135 §4 Batch 3a-ii).
    /// Loads the visit tracked by <c>(AccountId, Id)</c> with its lines and applies the guards in a
    /// fixed order: visit not found → <c>ConcurrencyVersion != expectedVisitVersion</c> →
    /// <c>Status != Submitted</c> → <c>ReviewedAtUtc != null</c> (drift D5) → the resolution's line
    /// is not on the visit → a targeted component's snapshot on the line is already non-null. On
    /// success it stages <paramref name="resolution"/> via <see cref="AddResolutionAsync"/>, calls
    /// <see cref="Core.Entities.ActualWork.RefreshConcurrencyVersionForFinancialResolution"/> to
    /// invalidate any stale review command, saves, and commits. Returns the post-append visit
    /// <c>ConcurrencyVersion</c>. Domain validation of the resolution's own values/basis/reason has
    /// already happened in <see cref="Core.Entities.ActualWorkLineFinancialResolution.Create"/>
    /// before this call.
    /// </summary>
    Task<ActualWorkResolutionOutcome> CreateResolutionAsync(
        ActualWorkLineFinancialResolution resolution, Guid expectedVisitVersion, CancellationToken ct);

    /// <summary>Stage one immutable resolution row into the caller's tracked change set. Does not
    /// call <c>SaveChangesAsync</c> and does not open a transaction — the Batch 3a-ii consumer owns
    /// the unit of work, having already loaded the visit and checked
    /// <c>ReviewedAtUtc</c> / <c>ConcurrencyVersion</c>.</summary>
    Task AddResolutionAsync(ActualWorkLineFinancialResolution resolution, CancellationToken ct);

    /// <summary>Stage one immutable visit-level disposition row into the caller's tracked change
    /// set. Same contract as <see cref="AddResolutionAsync"/>; the Batch 3b-i consumer owns the
    /// transaction.</summary>
    Task AddDispositionAsync(ActualWorkOfficeFinancialDisposition disposition, CancellationToken ct);
}
