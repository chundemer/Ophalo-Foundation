using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Core-owned registry of <see cref="KeepRequestWorkSignal.SourceModuleKey"/>/
/// <see cref="KeepRequestWorkSignal.SignalKey"/> values (ADR-463) — no arbitrary strings, no
/// customer-loaded code, no emergent plugin bus.
/// </summary>
public static class KeepRequestWorkSignalKeys
{
    public static class Modules
    {
        public const string PriceBookQuotesMaterials = "price_book_quotes_materials";
    }

    public static class Signals
    {
        public const string ProposedScopeNeedsOfficeReview = "proposed_scope_needs_office_review";

        /// <summary>Batch 4, build-log/129: raised when an Actual Work visit is submitted, resolved
        /// only when no submitted visit on the request remains unreviewed.</summary>
        public const string ActualWorkNeedsOfficeReview = "actual_work_needs_office_review";
    }
}

/// <summary>
/// Additive cross-module work signal for a <c>KeepRequest</c> (ADR-463). Core-owned — sits beside
/// <c>KeepRequest</c>/<c>AttentionReason</c>, not the Price Book module, even though its first
/// writer is Price Book's <c>ProposedScope</c> submission. Never overwrites, narrows, or replaces
/// <c>KeepRequest.AttentionLevel</c>/<c>AttentionReason</c>.
///
/// Resolution is aggregate-state-driven, not single-entity-driven (ADR-463): reviewing one of
/// several concurrently pending scopes on a request must not resolve this row while another
/// remains outstanding. A later submission after resolution reopens this same logical row —
/// unique on (<see cref="AccountId"/>, <see cref="KeepRequestId"/>, <see cref="SourceModuleKey"/>,
/// <see cref="SignalKey"/>) — rather than creating a second row for the same key.
///
/// No public mutation method exists yet: the only writer so far (Session 3.3a.2's atomic
/// submit/signal-reconciliation operation) raises/reopens this row via a native database upsert,
/// not tracked-entity mutation, so an insert-or-reopen race never needs an application-level
/// retry loop. A future reader/resolver (Session 3.5's office review) adds a tracked-mutation path
/// when it has an actual caller — no seam is built ahead of that need.
/// </summary>
public sealed class KeepRequestWorkSignal : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid KeepRequestId { get; private set; }

    public string SourceModuleKey { get; private set; } = string.Empty;

    public string SignalKey { get; private set; } = string.Empty;

    public DateTime RaisedAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <see cref="OfferingAssembly.ConcurrencyVersion"/> (ADR-330).
    /// </summary>
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    private KeepRequestWorkSignal()
    {
    }
}
