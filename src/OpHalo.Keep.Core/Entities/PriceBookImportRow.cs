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

    /// <summary>Valid only for a <c>Warning</c> row — an <c>Error</c> row is structurally
    /// unpublishable and must be skipped or corrected, never accepted as-is.</summary>
    public Result ResolveAccepted()
    {
        var eligibility = CheckExceptionEligibility();
        if (eligibility is not null)
            return eligibility;

        if (ValidationStatus == PriceBookImportRowValidationStatus.Error)
            return Result.Failure(PriceBookImportErrors.RowErrorCannotBeAccepted);

        ExceptionResolution = PriceBookImportRowExceptionResolution.Accepted;
        return Result.Success();
    }

    /// <summary>Valid for either a <c>Warning</c> or an <c>Error</c> row — the row is excluded from
    /// future publish, so no revalidation is needed.</summary>
    public Result ResolveSkipped()
    {
        var eligibility = CheckExceptionEligibility();
        if (eligibility is not null)
            return eligibility;

        ExceptionResolution = PriceBookImportRowExceptionResolution.Skipped;
        return Result.Success();
    }

    /// <summary>
    /// Replaces this row's proposed values with corrected ones and records the caller-supplied
    /// revalidation outcome (<paramref name="revalidatedStatus"/>/<paramref name="revalidationMessages"/>) —
    /// computed by the caller against the new values, since only the caller (an application service)
    /// can reach persistence for duplicate/mapped-item checks. If that outcome is still <c>Error</c>,
    /// the correction is rejected and this row is left completely unchanged: no partial field writes,
    /// no dishonest <c>Corrected</c> flip on a row that is still structurally invalid.
    /// </summary>
    public Result ApplyCorrection(
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
        Guid? mappedCatalogItemId,
        PriceBookImportRowValidationStatus revalidatedStatus,
        IEnumerable<string> revalidationMessages)
    {
        var eligibility = CheckExceptionEligibility();
        if (eligibility is not null)
            return eligibility;

        if (revalidatedStatus == PriceBookImportRowValidationStatus.Error)
            return Result.Failure(PriceBookImportErrors.RowCorrectionStillInvalid);
        if (revalidatedStatus != PriceBookImportRowValidationStatus.Valid
            && revalidatedStatus != PriceBookImportRowValidationStatus.Warning)
        {
            // Covers Pending and any out-of-range value from an unchecked enum cast — a correction
            // can only land on Valid or Warning; every other value is a caller bug, not a domain
            // outcome to represent.
            throw new ArgumentException("Revalidated status must be Valid or Warning.", nameof(revalidatedStatus));
        }

        var normalizedMessages = NormalizeMessages(revalidationMessages);
        if (revalidatedStatus == PriceBookImportRowValidationStatus.Warning && normalizedMessages.Count == 0)
            return Result.Failure(PriceBookImportErrors.RowValidationMessagesRequired);
        if (revalidatedStatus == PriceBookImportRowValidationStatus.Valid && normalizedMessages.Count > 0)
        {
            throw new ArgumentException(
                "A Valid revalidation result must not carry messages.", nameof(revalidationMessages));
        }

        MappedCatalogItemId = mappedCatalogItemId;
        ProposedType = Trim(proposedType);
        ProposedDisplayName = Trim(proposedDisplayName);
        ProposedExternalKey = Trim(proposedExternalKey);
        ProposedCategoryLabel = Trim(proposedCategoryLabel);
        ProposedUnitOfMeasure = Trim(proposedUnitOfMeasure);
        ProposedCost = proposedCost;
        ProposedSellPrice = proposedSellPrice;
        ProposedCurrency = string.IsNullOrWhiteSpace(proposedCurrency)
            ? null
            : proposedCurrency.Trim().ToUpperInvariant();
        ProposedSourceLaborHours = proposedSourceLaborHours;
        ProposedSourceConsumablesAllowance = proposedSourceConsumablesAllowance;
        ProposedSourceTaxAmount = proposedSourceTaxAmount;

        ValidationStatus = revalidatedStatus;
        _validationMessages.Clear();
        _validationMessages.AddRange(normalizedMessages);

        ExceptionResolution = PriceBookImportRowExceptionResolution.Corrected;
        return Result.Success();
    }

    private Result SetValidationResult(PriceBookImportRowValidationStatus status, IEnumerable<string> messages)
    {
        var normalizedMessages = NormalizeMessages(messages);
        if (normalizedMessages.Count == 0)
            return Result.Failure(PriceBookImportErrors.RowValidationMessagesRequired);

        ValidationStatus = status;
        _validationMessages.Clear();
        _validationMessages.AddRange(normalizedMessages);
        return Result.Success();
    }

    /// <summary>Null when the row is an unresolved exception (<c>Warning</c>/<c>Error</c>,
    /// <c>ExceptionResolution</c> still <c>Unresolved</c>); otherwise the failure to return.</summary>
    private Result? CheckExceptionEligibility()
    {
        if (ValidationStatus != PriceBookImportRowValidationStatus.Warning
            && ValidationStatus != PriceBookImportRowValidationStatus.Error)
            return Result.Failure(PriceBookImportErrors.RowHasNoException);

        if (ExceptionResolution != PriceBookImportRowExceptionResolution.Unresolved)
            return Result.Failure(PriceBookImportErrors.RowExceptionAlreadyResolved);

        return null;
    }

    private static List<string> NormalizeMessages(IEnumerable<string> messages) =>
        (messages ?? [])
            .Select(m => m?.Trim() ?? string.Empty)
            .Where(m => m.Length > 0)
            .ToList();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
