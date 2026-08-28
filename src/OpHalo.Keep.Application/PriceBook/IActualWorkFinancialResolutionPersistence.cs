using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

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
