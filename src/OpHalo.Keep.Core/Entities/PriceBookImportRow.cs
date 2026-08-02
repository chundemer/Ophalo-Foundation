using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// One staged row of a <see cref="PriceBookImport"/> (build-log/108, build-log/110, Session
/// 2c.1a). Created only through the parent import (<see cref="PriceBookImport.AddRow"/>), but its
/// validation/exception-resolution transition methods are public and directly callable by a later
/// row-scoped service (Session 2c.1b) without loading the complete, potentially very large, parent
/// import. Carries no independent <c>ConcurrencyVersion</c> — the import governs staging/publish
/// boundaries.
/// </summary>
public sealed class PriceBookImportRow : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid PriceBookImportId { get; private set; }

    public int RowNumber { get; private set; }

    public string? SourceTab { get; private set; }

    /// <summary>FK to an existing <see cref="CatalogItem"/> — null means "new item" (build-log/108).</summary>
    public Guid? MappedCatalogItemId { get; private set; }

    /// <summary>Raw source text, not <see cref="CatalogItemType"/> — an unrecognized or misspelled
    /// source value must still be stageable and correctable before catalog mapping.</summary>
    public string? ProposedType { get; private set; }

    public string? ProposedDisplayName { get; private set; }

    public string? ProposedExternalKey { get; private set; }

    /// <summary>Raw source text; confirmed into a real <see cref="CatalogCategory"/> only by an
    /// explicit later office mapping action.</summary>
    public string? ProposedCategoryLabel { get; private set; }

    public string? ProposedUnitOfMeasure { get; private set; }

    public decimal? ProposedCost { get; private set; }

    public decimal? ProposedSellPrice { get; private set; }

    public string? ProposedCurrency { get; private set; }

    public decimal? ProposedSourceLaborHours { get; private set; }

    public decimal? ProposedSourceConsumablesAllowance { get; private set; }

    public decimal? ProposedSourceTaxAmount { get; private set; }

    public PriceBookImportRowValidationStatus ValidationStatus { get; private set; }

    private readonly List<string> _validationMessages = [];

    /// <summary>Messages explaining a <c>Warning</c>/<c>Error</c> validation status. Must retain
    /// offending raw source text (for example, unparseable sell-price text) so a null proposed
    /// value alone cannot explain the failure to the exception-review user.</summary>
    public IReadOnlyCollection<string> ValidationMessages => _validationMessages;

    public PriceBookImportRowExceptionResolution ExceptionResolution { get; private set; }

    private PriceBookImportRow()
    {
    }

    internal static Result<PriceBookImportRow> Create(
        Guid accountId,
        Guid priceBookImportId,
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
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (priceBookImportId == Guid.Empty)
            throw new ArgumentException("PriceBookImportId must not be empty.", nameof(priceBookImportId));
        if (rowNumber <= 0)
            throw new ArgumentException("RowNumber must be a positive integer.", nameof(rowNumber));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        return Result<PriceBookImportRow>.Success(new PriceBookImportRow
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            PriceBookImportId = priceBookImportId,
            RowNumber = rowNumber,
            SourceTab = Trim(sourceTab),
            MappedCatalogItemId = mappedCatalogItemId,
            ProposedType = Trim(proposedType),
            ProposedDisplayName = Trim(proposedDisplayName),
            ProposedExternalKey = Trim(proposedExternalKey),
            ProposedCategoryLabel = Trim(proposedCategoryLabel),
            ProposedUnitOfMeasure = Trim(proposedUnitOfMeasure),
            ProposedCost = proposedCost,
            ProposedSellPrice = proposedSellPrice,
            ProposedCurrency = string.IsNullOrWhiteSpace(proposedCurrency)
                ? null
                : proposedCurrency.Trim().ToUpperInvariant(),
            ProposedSourceLaborHours = proposedSourceLaborHours,
            ProposedSourceConsumablesAllowance = proposedSourceConsumablesAllowance,
            ProposedSourceTaxAmount = proposedSourceTaxAmount,
            ValidationStatus = PriceBookImportRowValidationStatus.Pending,
            ExceptionResolution = PriceBookImportRowExceptionResolution.Unresolved,
        });
    }

    public Result MarkValid()
    {
        ValidationStatus = PriceBookImportRowValidationStatus.Valid;
        _validationMessages.Clear();
        return Result.Success();
    }

    public Result MarkWarning(IEnumerable<string> messages) =>
        SetValidationResult(PriceBookImportRowValidationStatus.Warning, messages);

    public Result MarkError(IEnumerable<string> messages) =>
        SetValidationResult(PriceBookImportRowValidationStatus.Error, messages);

    public Result ResolveAccepted() => Resolve(PriceBookImportRowExceptionResolution.Accepted);

    public Result ResolveSkipped() => Resolve(PriceBookImportRowExceptionResolution.Skipped);

    public Result ResolveCorrected() => Resolve(PriceBookImportRowExceptionResolution.Corrected);

    private Result SetValidationResult(PriceBookImportRowValidationStatus status, IEnumerable<string> messages)
    {
        var normalizedMessages = (messages ?? [])
            .Select(m => m?.Trim() ?? string.Empty)
            .Where(m => m.Length > 0)
            .ToList();
        if (normalizedMessages.Count == 0)
            return Result.Failure(PriceBookImportErrors.RowValidationMessagesRequired);

        ValidationStatus = status;
        _validationMessages.Clear();
        _validationMessages.AddRange(normalizedMessages);
        return Result.Success();
    }

    private Result Resolve(PriceBookImportRowExceptionResolution resolution)
    {
        if (ValidationStatus != PriceBookImportRowValidationStatus.Warning
            && ValidationStatus != PriceBookImportRowValidationStatus.Error)
            return Result.Failure(PriceBookImportErrors.RowHasNoException);

        if (ExceptionResolution != PriceBookImportRowExceptionResolution.Unresolved)
            return Result.Failure(PriceBookImportErrors.RowExceptionAlreadyResolved);

        ExceptionResolution = resolution;
        return Result.Success();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
