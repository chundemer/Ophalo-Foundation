using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the PriceBookImport/PriceBookImportRow domain foundation (build-log/108, build-log/110,
/// Session 2c.1a): Create validation, row staging and (ImportId, RowNumber) uniqueness,
/// Staged &#8594; Validated and pre-publish Discarded transitions, and row-level
/// validation/exception-resolution transition methods.
/// </summary>
public class PriceBookImportTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    const string SourceKey = "imports/2026/08/01/opaque-test-key.csv";

    static Result<PriceBookImport> StagedImport(string sourceFileObjectKey = SourceKey) =>
        PriceBookImport.Create(AccountId, sourceFileObjectKey, Actor, DateTime.UtcNow);

    static Result<PriceBookImportRow> Row(PriceBookImport import, int rowNumber = 1) =>
        import.AddRow(
            rowNumber,
            sourceTab: "Materials",
            mappedCatalogItemId: null,
            proposedType: "Material",
            proposedDisplayName: "1/2\" Copper Pipe",
            proposedExternalKey: "CU-12",
            proposedCategoryLabel: "Plumbing",
            proposedUnitOfMeasure: "ft",
            proposedCost: 1.25m,
            proposedSellPrice: 3.00m,
            proposedCurrency: "usd",
            proposedSourceLaborHours: null,
            proposedSourceConsumablesAllowance: null,
            proposedSourceTaxAmount: null,
            createdByUserId: Actor);

    // --- PriceBookImport.Create ---

    [Fact]
    public void Create_with_valid_fields_succeeds_as_Staged()
    {
        var result = StagedImport();

        Assert.True(result.IsSuccess);
        var import = result.Value;
        Assert.Equal(AccountId, import.AccountId);
        Assert.Equal(SourceKey, import.SourceFileObjectKey);
        Assert.Equal(Actor, import.UploadedByAccountUserId);
        Assert.Equal(Actor, import.CreatedByUserId);
        Assert.Equal(PriceBookImportStatus.Staged, import.Status);
        Assert.Null(import.PublishedAtUtc);
        Assert.Null(import.PublishedByAccountUserId);
        Assert.Null(import.PublishedPriceBookVersionId);
        Assert.Empty(import.Rows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_source_key_fails(string sourceKey)
    {
        var result = StagedImport(sourceKey);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.SourceFileObjectKeyRequired, result.Error);
    }

    [Fact]
    public void Create_with_source_key_over_1024_chars_fails()
    {
        var result = StagedImport(new string('x', 1025));

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.SourceFileObjectKeyTooLong, result.Error);
    }

    [Fact]
    public void Create_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => PriceBookImport.Create(Guid.Empty, SourceKey, Actor, DateTime.UtcNow));
    }

    [Fact]
    public void Create_with_empty_uploaded_by_throws()
    {
        Assert.Throws<ArgumentException>(() => PriceBookImport.Create(AccountId, SourceKey, Guid.Empty, DateTime.UtcNow));
    }

    // --- AddRow ---

    [Fact]
    public void AddRow_on_Staged_import_succeeds_and_defaults_row_state()
    {
        var import = StagedImport().Value;

        var result = Row(import);

        Assert.True(result.IsSuccess);
        var row = result.Value;
        Assert.Equal(AccountId, row.AccountId);
        Assert.Equal(import.Id, row.PriceBookImportId);
        Assert.Equal(1, row.RowNumber);
        Assert.Equal("USD", row.ProposedCurrency);
        Assert.Equal(PriceBookImportRowValidationStatus.Pending, row.ValidationStatus);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
        Assert.Empty(row.ValidationMessages);
        Assert.Contains(row, import.Rows);
    }

    [Fact]
    public void AddRow_with_duplicate_row_number_fails()
    {
        var import = StagedImport().Value;
        Row(import, rowNumber: 1);

        var result = Row(import, rowNumber: 1);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowNumberDuplicate, result.Error);
    }

    [Fact]
    public void AddRow_with_non_positive_row_number_throws()
    {
        var import = StagedImport().Value;

        Assert.Throws<ArgumentException>(() => Row(import, rowNumber: 0));
    }

    [Fact]
    public void AddRow_after_MarkValidated_fails_NotStaged()
    {
        var import = StagedImport().Value;
        import.MarkValidated();

        var result = Row(import);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.NotStaged, result.Error);
    }

    // --- MarkValidated / Discard ---

    [Fact]
    public void MarkValidated_from_Staged_succeeds()
    {
        var import = StagedImport().Value;

        var result = import.MarkValidated();

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportStatus.Validated, import.Status);
    }

    [Fact]
    public void MarkValidated_when_already_Validated_fails_NotStaged()
    {
        var import = StagedImport().Value;
        import.MarkValidated();

        var result = import.MarkValidated();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.NotStaged, result.Error);
    }

    [Fact]
    public void Discard_from_Staged_succeeds()
    {
        var import = StagedImport().Value;

        var result = import.Discard();

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportStatus.Discarded, import.Status);
    }

    [Fact]
    public void Discard_from_Validated_succeeds()
    {
        var import = StagedImport().Value;
        import.MarkValidated();

        var result = import.Discard();

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportStatus.Discarded, import.Status);
    }

    [Fact]
    public void Discard_when_already_Discarded_fails_NotDiscardable()
    {
        var import = StagedImport().Value;
        import.Discard();

        var result = import.Discard();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.NotDiscardable, result.Error);
    }

    // --- PriceBookImportRow validation transitions ---

    [Fact]
    public void MarkValid_clears_messages_and_sets_Valid()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkWarning(["needs review"]);

        var result = row.MarkValid();

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowValidationStatus.Valid, row.ValidationStatus);
        Assert.Empty(row.ValidationMessages);
    }

    [Fact]
    public void MarkWarning_with_messages_succeeds_and_retains_raw_text()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;

        var result = row.MarkWarning(["Sell price 'abc' could not be parsed as a number."]);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowValidationStatus.Warning, row.ValidationStatus);
        Assert.Equal(["Sell price 'abc' could not be parsed as a number."], row.ValidationMessages);
    }

    [Fact]
    public void MarkError_with_no_messages_fails()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;

        var result = row.MarkError([]);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowValidationMessagesRequired, result.Error);
        Assert.Equal(PriceBookImportRowValidationStatus.Pending, row.ValidationStatus);
    }

    [Fact]
    public void MarkError_with_blank_only_messages_fails()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;

        var result = row.MarkError(["   ", ""]);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowValidationMessagesRequired, result.Error);
    }

    // --- PriceBookImportRow exception resolution ---

    [Fact]
    public void ResolveAccepted_from_Warning_succeeds()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkWarning(["needs review"]);

        var result = row.ResolveAccepted();

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowExceptionResolution.Accepted, row.ExceptionResolution);
    }

    [Fact]
    public void ResolveAccepted_from_Error_fails_RowErrorCannotBeAccepted()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["bad value"]);

        var result = row.ResolveAccepted();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowErrorCannotBeAccepted, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
    }

    [Fact]
    public void ResolveAccepted_on_Pending_row_fails_RowHasNoException()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;

        var result = row.ResolveAccepted();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowHasNoException, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
    }

    [Fact]
    public void ResolveAccepted_on_Valid_row_fails_RowHasNoException()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkValid();

        var result = row.ResolveAccepted();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowHasNoException, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
    }

    [Theory]
    [InlineData("Warning")]
    [InlineData("Error")]
    public void ResolveSkipped_from_Warning_or_Error_succeeds(string status)
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        if (status == "Warning") row.MarkWarning(["needs review"]); else row.MarkError(["bad value"]);

        var result = row.ResolveSkipped();

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowExceptionResolution.Skipped, row.ExceptionResolution);
    }

    [Fact]
    public void ResolveSkipped_on_Pending_row_fails_RowHasNoException()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;

        var result = row.ResolveSkipped();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowHasNoException, result.Error);
    }

    [Fact]
    public void ResolveAccepted_when_already_resolved_fails()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkWarning(["needs review"]);
        row.ResolveAccepted();

        var result = row.ResolveSkipped();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowExceptionAlreadyResolved, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Accepted, row.ExceptionResolution);
    }

    // --- PriceBookImportRow.ApplyCorrection ---

    static Result ApplyValidCorrection(PriceBookImportRow row, PriceBookImportRowValidationStatus revalidatedStatus, IEnumerable<string>? messages = null) =>
        row.ApplyCorrection(
            "Material", "Corrected Name", "CU-99", "Plumbing", "ft",
            2.00m, 4.00m, "USD", null, null, null, null,
            revalidatedStatus, messages ?? []);

    [Fact]
    public void ApplyCorrection_with_Valid_revalidation_succeeds_and_replaces_values()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["bad value"]);

        var result = ApplyValidCorrection(row, PriceBookImportRowValidationStatus.Valid);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowValidationStatus.Valid, row.ValidationStatus);
        Assert.Equal(PriceBookImportRowExceptionResolution.Corrected, row.ExceptionResolution);
        Assert.Equal("Corrected Name", row.ProposedDisplayName);
        Assert.Equal("CU-99", row.ProposedExternalKey);
        Assert.Empty(row.ValidationMessages);
    }

    [Fact]
    public void ApplyCorrection_with_Warning_revalidation_succeeds_and_keeps_messages()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["bad value"]);

        var result = ApplyValidCorrection(row, PriceBookImportRowValidationStatus.Warning, ["still needs a look"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowValidationStatus.Warning, row.ValidationStatus);
        Assert.Equal(PriceBookImportRowExceptionResolution.Corrected, row.ExceptionResolution);
        Assert.Equal(["still needs a look"], row.ValidationMessages);
    }

    [Fact]
    public void ApplyCorrection_with_Warning_revalidation_and_no_messages_fails()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["bad value"]);

        var result = ApplyValidCorrection(row, PriceBookImportRowValidationStatus.Warning, []);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowValidationMessagesRequired, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
    }

    [Fact]
    public void ApplyCorrection_with_Error_revalidation_fails_and_leaves_row_unchanged()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["original error"]);

        var result = ApplyValidCorrection(row, PriceBookImportRowValidationStatus.Error, ["still bad"]);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowCorrectionStillInvalid, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
        Assert.Equal(PriceBookImportRowValidationStatus.Error, row.ValidationStatus);
        Assert.Equal(["original error"], row.ValidationMessages);
        Assert.NotEqual("Corrected Name", row.ProposedDisplayName);
    }

    [Fact]
    public void ApplyCorrection_on_Pending_row_fails_RowHasNoException()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;

        var result = ApplyValidCorrection(row, PriceBookImportRowValidationStatus.Valid);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowHasNoException, result.Error);
    }

    [Fact]
    public void ApplyCorrection_with_Pending_revalidation_status_throws()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["bad value"]);

        Assert.Throws<ArgumentException>(() => ApplyValidCorrection(row, PriceBookImportRowValidationStatus.Pending));
    }

    [Fact]
    public void ApplyCorrection_with_out_of_range_revalidation_status_throws()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["bad value"]);
        var outOfRange = (PriceBookImportRowValidationStatus)99;

        Assert.Throws<ArgumentException>(() => ApplyValidCorrection(row, outOfRange));
    }

    [Fact]
    public void ApplyCorrection_with_Valid_revalidation_and_nonempty_messages_throws()
    {
        var import = StagedImport().Value;
        var row = Row(import).Value;
        row.MarkError(["bad value"]);

        Assert.Throws<ArgumentException>(() =>
            ApplyValidCorrection(row, PriceBookImportRowValidationStatus.Valid, ["should not be here"]));
    }
}
