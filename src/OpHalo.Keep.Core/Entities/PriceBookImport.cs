using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A staged price-book source-file upload awaiting validation and publish (build-log/108,
/// build-log/110, ADR-469, ADR-471, Session 2c.1a). Owns its <see cref="PriceBookImportRow"/>
/// children for creation and staging/publish lifecycle boundaries; this session implements only
/// row staging and the <c>Staged</c> &#8594; <c>Validated</c> and pre-publish
/// <c>Discarded</c> transitions — publish mutations are a later session (2d).
/// </summary>
public sealed class PriceBookImport : BaseEntity
{
    public Guid AccountId { get; private set; }

    /// <summary>Private object-storage reference (ADR-469, ADR-471) — never a database blob, never
    /// a public URL. Required/non-null from this session's first migration.</summary>
    public string SourceFileObjectKey { get; private set; } = string.Empty;

    public Guid UploadedByAccountUserId { get; private set; }

    public DateTime UploadedAtUtc { get; private set; }

    public PriceBookImportStatus Status { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    public Guid? PublishedByAccountUserId { get; private set; }

    /// <summary>FK to the resulting <c>PriceBookVersion</c>, set on publish success. Unconstrained
    /// until that table exists (Session 2d).</summary>
    public Guid? PublishedPriceBookVersionId { get; private set; }

    private readonly List<PriceBookImportRow> _rows = [];

    /// <summary>Staged rows. Owned children: created and transitioned only through this aggregate
    /// for creation, though a later row-scoped service may load and mutate an individual
    /// <see cref="PriceBookImportRow"/> directly (Session 2c.1b).</summary>
    public IReadOnlyCollection<PriceBookImportRow> Rows => _rows;

    private const int MaxSourceFileObjectKeyLength = 1024;

    private PriceBookImport()
    {
    }

    public static Result<PriceBookImport> Create(
        Guid accountId,
        string sourceFileObjectKey,
        Guid uploadedByAccountUserId,
        DateTime uploadedAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (uploadedByAccountUserId == Guid.Empty)
            throw new ArgumentException("UploadedByAccountUserId must not be empty.", nameof(uploadedByAccountUserId));

        var trimmedKey = sourceFileObjectKey?.Trim() ?? string.Empty;
        if (trimmedKey.Length == 0)
            return Result<PriceBookImport>.Failure(PriceBookImportErrors.SourceFileObjectKeyRequired);
        if (trimmedKey.Length > MaxSourceFileObjectKeyLength)
            return Result<PriceBookImport>.Failure(PriceBookImportErrors.SourceFileObjectKeyTooLong);

        return Result<PriceBookImport>.Success(new PriceBookImport
        {
            CreatedByUserId = uploadedByAccountUserId,
            AccountId = accountId,
            SourceFileObjectKey = trimmedKey,
            UploadedByAccountUserId = uploadedByAccountUserId,
            UploadedAtUtc = uploadedAtUtc,
            Status = PriceBookImportStatus.Staged,
        });
    }

    public Result<PriceBookImportRow> AddRow(
        int rowNumber,
        string? sourceTab,
        Guid? mappedCatalogItemId,
        string? proposedType,
        string? proposedDisplayName,
        string? proposedExternalKey,
        string? proposedCategoryLabel,
        string? proposedUnitOfMeasure,
        decimal? proposedCost,
        decimal? proposedSellPrice,
        string? proposedCurrency,
        decimal? proposedSourceLaborHours,
        decimal? proposedSourceConsumablesAllowance,
        decimal? proposedSourceTaxAmount,
        Guid createdByUserId)
    {
        if (Status != PriceBookImportStatus.Staged)
            return Result<PriceBookImportRow>.Failure(PriceBookImportErrors.NotStaged);

        if (_rows.Any(r => r.RowNumber == rowNumber))
            return Result<PriceBookImportRow>.Failure(PriceBookImportErrors.RowNumberDuplicate);

        var createResult = PriceBookImportRow.Create(
            AccountId,
            Id,
            rowNumber,
            sourceTab,
            mappedCatalogItemId,
            proposedType,
            proposedDisplayName,
            proposedExternalKey,
            proposedCategoryLabel,
            proposedUnitOfMeasure,
            proposedCost,
            proposedSellPrice,
            proposedCurrency,
            proposedSourceLaborHours,
            proposedSourceConsumablesAllowance,
            proposedSourceTaxAmount,
            createdByUserId);
        if (createResult.IsFailure)
            return createResult;

        _rows.Add(createResult.Value);
        return createResult;
    }

    public Result MarkValidated()
    {
        if (Status != PriceBookImportStatus.Staged)
            return Result.Failure(PriceBookImportErrors.NotStaged);

        Status = PriceBookImportStatus.Validated;
        return Result.Success();
    }

    public Result Discard()
    {
        if (Status != PriceBookImportStatus.Staged && Status != PriceBookImportStatus.Validated)
            return Result.Failure(PriceBookImportErrors.NotDiscardable);

        Status = PriceBookImportStatus.Discarded;
        return Result.Success();
    }
}
