using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Server-authoritative undo-delete record for a deleted <see cref="ProposedScopeLine"/> (Session
/// 4b, build-log/119 decision 1). Captures the complete line field set so
/// <see cref="ProposedScope.RestoreLine"/> can recreate the exact line at its original
/// <see cref="DisplayOrder"/>. Keyed by <see cref="ProposedScopeId"/>/<see cref="LineId"/> (unique
/// index, not this row's own <c>Id</c>).
///
/// <see cref="RemovedAtUtc"/> is the sole restore-window authority: the five-second undo grace
/// period is always measured against it, never against a client-supplied timer. This row is
/// hard-deleted on a successful restore — no consumed-flag audit trail — and may otherwise be
/// cleaned up asynchronously once expired, since <see cref="ProposedScope.RestoreLine"/> re-checks
/// <see cref="RemovedAtUtc"/> itself rather than relying on cleanup timing.
/// </summary>
public sealed class RemovedProposedScopeLineSnapshot : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid ProposedScopeId { get; private set; }

    /// <summary>The removed <see cref="ProposedScopeLine"/>'s original id — the identity a
    /// successful restore reinserts, distinct from this snapshot row's own <c>Id</c>.</summary>
    public Guid LineId { get; private set; }

    public ProposedScopeLineType LineType { get; private set; }

    public Guid? CatalogItemId { get; private set; }

    public Guid? OfferingAssemblyId { get; private set; }

    public decimal Quantity { get; private set; }

    public bool IsException { get; private set; }

    public string? OffCatalogDescription { get; private set; }

    public decimal? OffCatalogQuantity { get; private set; }

    public string? Note { get; private set; }

    public int DisplayOrder { get; private set; }

    public string DisplayNameSnapshot { get; private set; } = string.Empty;

    public string? UnitOfMeasureSnapshot { get; private set; }

    public string? OfferingAssemblyNameSnapshot { get; private set; }

    public decimal? DefaultQuantitySnapshot { get; private set; }

    public DateTime RemovedAtUtc { get; private set; }

    private RemovedProposedScopeLineSnapshot()
    {
    }

    /// <summary>Captures a full-field snapshot of <paramref name="line"/> at the moment it is
    /// removed from <paramref name="line"/>'s parent scope. Internal — only
    /// <see cref="ProposedScope.RemoveLine"/> may create one, from a line it is itself removing.</summary>
    internal static RemovedProposedScopeLineSnapshot FromLine(ProposedScopeLine line, DateTime removedAtUtc) =>
        new()
        {
            CreatedByUserId = line.CreatedByUserId,
            AccountId = line.AccountId,
            ProposedScopeId = line.ProposedScopeId,
            LineId = line.Id,
            LineType = line.LineType,
            CatalogItemId = line.CatalogItemId,
            OfferingAssemblyId = line.OfferingAssemblyId,
            Quantity = line.Quantity,
            IsException = line.IsException,
            OffCatalogDescription = line.OffCatalogDescription,
            OffCatalogQuantity = line.OffCatalogQuantity,
            Note = line.Note,
            DisplayOrder = line.DisplayOrder,
            DisplayNameSnapshot = line.DisplayNameSnapshot,
            UnitOfMeasureSnapshot = line.UnitOfMeasureSnapshot,
            OfferingAssemblyNameSnapshot = line.OfferingAssemblyNameSnapshot,
            DefaultQuantitySnapshot = line.DefaultQuantitySnapshot,
            RemovedAtUtc = removedAtUtc,
        };
}
