using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// An immutable, published snapshot of catalog pricing (Build 107/ADR-458, ADR-467, ADR-470,
/// build-log/108, build-log/111). Every version in this module is created directly by office
/// price entry — CSV import is deferred (ADR-472) — so <see cref="SourceImportId"/> is always
/// null; the column is retained only for schema continuity with the original ERD rather than
/// re-adding it if import ever returns. Owns exactly one <see cref="PriceBookVersionLine"/> in
/// this batch, because V1 direct entry publishes one <see cref="CatalogItem"/> per transaction.
/// Never edited after creation except the single <see cref="Supersede"/> transition performed
/// when a later version publishes for the same account.
/// </summary>
public sealed class PriceBookVersion : BaseEntity
{
    public Guid AccountId { get; private set; }

    /// <summary>Sequential per account (uniqueness enforced at persistence, not here).</summary>
    public int VersionNumber { get; private set; }

    /// <summary>Always null in this batch — see class remarks.</summary>
    public Guid? SourceImportId { get; private set; }

    public DateTime PublishedAtUtc { get; private set; }

    public Guid PublishedByAccountUserId { get; private set; }

    public PriceBookVersionStatus Status { get; private set; }

    private readonly List<PriceBookVersionLine> _lines = [];

    /// <summary>
    /// Owned child snapshot line(s). Created only by <see cref="CreatePublished"/>; mutation is
    /// guarded by this parent aggregate, not by an independent concurrency token on the child.
    /// </summary>
    public IReadOnlyCollection<PriceBookVersionLine> Lines => _lines;

    private PriceBookVersion()
    {
    }

    public static Result<PriceBookVersion> CreatePublished(
        Guid accountId,
        int versionNumber,
        Guid publishedByAccountUserId,
        DateTime publishedAtUtc,
        Guid catalogItemId,
        string displayNameSnapshot,
        CatalogItemType typeSnapshot,
        string unitOfMeasureSnapshot,
        string currencySnapshot,
        decimal? costSnapshot,
        decimal? sellPriceSnapshot)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (versionNumber <= 0)
            throw new ArgumentException("VersionNumber must be positive.", nameof(versionNumber));
        if (publishedByAccountUserId == Guid.Empty)
            throw new ArgumentException("PublishedByAccountUserId must not be empty.", nameof(publishedByAccountUserId));

        var version = new PriceBookVersion
        {
            AccountId = accountId,
            VersionNumber = versionNumber,
            SourceImportId = null,
            PublishedAtUtc = publishedAtUtc,
            PublishedByAccountUserId = publishedByAccountUserId,
            Status = PriceBookVersionStatus.Published,
        };

        var lineResult = PriceBookVersionLine.Create(
            accountId,
            version.Id,
            catalogItemId,
            displayNameSnapshot,
            typeSnapshot,
            unitOfMeasureSnapshot,
            currencySnapshot,
            costSnapshot,
            sellPriceSnapshot);
        if (lineResult.IsFailure)
            return Result<PriceBookVersion>.Failure(lineResult.Error);

        version._lines.Add(lineResult.Value);
        return Result<PriceBookVersion>.Success(version);
    }

    /// <summary>
    /// Marks this version superseded because a new version has just published for the same
    /// account (ADR-470's publish transaction performs this on the prior <c>Published</c> row
    /// before inserting the new one).
    /// </summary>
    public Result Supersede()
    {
        if (Status != PriceBookVersionStatus.Published)
            return Result.Failure(PriceBookVersionErrors.AlreadySuperseded);

        Status = PriceBookVersionStatus.Superseded;
        return Result.Success();
    }
}
