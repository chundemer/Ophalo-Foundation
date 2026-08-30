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

    /// <summary>BL136 D6c (slice 4e-ii-b-2): the visit has been superseded by a replacement copy.
    /// Checked immediately after <see cref="VersionMismatch"/>. Maps to
    /// <see cref="Core.Errors.ActualWorkErrors.Superseded"/>.</summary>
    Superseded,

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

/// <summary>Outcome of the transactional <see cref="IActualWorkFinancialResolutionPersistence.RecordDispositionAsync"/>
/// orchestrator (BL135 §4 Batch 3b-i). <see cref="ActualWorkDispositionResult.Committed"/> is the
/// only success; every other value maps to a stable API error. Guards are applied in the same fixed
/// order as <see cref="ActualWorkResolutionResult"/>: not found → version → superseded → not
/// submitted → already reviewed → visit has lines.</summary>
public enum ActualWorkDispositionResult
{
    Committed,

    /// <summary>No visit for <c>(accountId, actualWorkId)</c>.</summary>
    VisitNotFound,

    /// <summary>The loaded visit's <c>ConcurrencyVersion</c> did not match the caller's expected
    /// version, or the row changed under the save (EF concurrency-token mismatch). Checked ahead of
    /// every business guard, so a stale request on a since-reviewed/lined visit still returns this.</summary>
    VersionMismatch,

    /// <summary>BL136 D6c (slice 4e-ii-b-2): the visit has been superseded by a replacement copy.
    /// Checked immediately after <see cref="VersionMismatch"/>. Maps to
    /// <see cref="Core.Errors.ActualWorkErrors.Superseded"/>.</summary>
    Superseded,

    /// <summary>The visit is still <c>Draft</c> — a disposition applies only to a submitted visit.</summary>
    VisitNotSubmitted,

    /// <summary>Drift D5: the visit has already been financially reviewed.</summary>
    VisitAlreadyReviewed,

    /// <summary>The visit has at least one line — locked §6.2: a <c>NoCharge</c> disposition is
    /// zero-line only.</summary>
    VisitHasLines,
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

/// <summary>The immutable field payload of one office financial disposition append (BL135 §4
/// Batch 3b-i). <see cref="Kind"/> is the raw request string, parsed to
/// <see cref="Core.Entities.Enums.OfficeFinancialDispositionKind"/> (trimmed, case-insensitive) by
/// <c>ActualWorkOfficeFinancialDispositionApiService</c>. <see cref="Reason"/> is validated and
/// normalized by <see cref="Core.Entities.ActualWorkOfficeFinancialDisposition.Create"/>.</summary>
public sealed record ActualWorkDispositionCommand(string? Kind, string? Reason);

/// <summary><see cref="NewVisitConcurrencyVersion"/> is set only when <see cref="Result"/> is
/// <see cref="ActualWorkDispositionResult.Committed"/> — the visit token after the append, which a
/// subsequent <c>POST .../review</c> must echo.</summary>
public sealed record ActualWorkDispositionOutcome(
    ActualWorkDispositionResult Result, Guid? NewVisitConcurrencyVersion = null);

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
    /// <c>SupersededAtUtc != null</c> (BL136 D6c) → <c>Status != Submitted</c> →
    /// <c>ReviewedAtUtc != null</c> (drift D5) → the resolution's line
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

    /// <summary>
    /// Transactional orchestrator for a single office-financial-disposition append (BL135 §4
    /// Batch 3b-i). Loads the visit tracked by <c>(AccountId, Id)</c> with its lines and applies the
    /// guards in a fixed order matching <see cref="CreateResolutionAsync"/>: visit not found →
    /// <c>ConcurrencyVersion != expectedVisitVersion</c> → <c>SupersededAtUtc != null</c>
    /// (BL136 D6c) → <c>Status != Submitted</c> →
    /// <c>ReviewedAtUtc != null</c> (drift D5) → <b>the visit has ≥1 line</b> (locked §6.2 —
    /// <c>NoCharge</c> is zero-line only). On success it stages <paramref name="disposition"/> via
    /// <see cref="AddDispositionAsync"/>, calls
    /// <see cref="Core.Entities.ActualWork.RefreshConcurrencyVersionForOfficeFinancialDisposition"/>
    /// to invalidate any stale review command, saves, and commits in one transaction — a concurrency
    /// exception leaves no persisted row. Returns the post-append visit <c>ConcurrencyVersion</c>.
    /// Domain validation of the reason has already happened in
    /// <see cref="Core.Entities.ActualWorkOfficeFinancialDisposition.Create"/> before this call.
    /// Multiple dispositions on a still-eligible visit are permitted — the row is append-only and
    /// the effective disposition is the most-recent one.
    /// </summary>
    Task<ActualWorkDispositionOutcome> RecordDispositionAsync(
        ActualWorkOfficeFinancialDisposition disposition, Guid expectedVisitVersion, CancellationToken ct);
}
