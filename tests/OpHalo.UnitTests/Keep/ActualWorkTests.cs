using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the ActualWork/ActualWorkLine domain (ADR-487, build-log/129, Batch 1): Create, line
/// capture rules, Draft-only mutability, the zero-line outcome/completion-note submit invariant,
/// and the pure Submit status transition. No persistence and no Actual Work review-signal
/// coordination here — those are Batch 2/4 concerns.
/// </summary>
public class ActualWorkTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid RequestId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();
    static readonly Guid PriceBookVersionLineId = Guid.CreateVersion7();
    static readonly Guid CommercialBaselineSourceLineId = Guid.CreateVersion7();

    static Result<ActualWork> New() => ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor);

    static Result<ActualWorkLine> AddCatalogBackedLine(ActualWork work, decimal quantity = 1m) =>
        ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", quantity,
            sellPriceSnapshot: 42.50m, standardExpectedDirectCostSnapshot: 18.00m,
            note: null, commercialBaselineSourceLineId: null, Actor);

    static Result<ActualWorkLine> AddCustomLine(ActualWork work, decimal quantity = 1m) =>
        ActualWorkTestData.AddLine(
            work,
            catalogItemId: null, priceBookVersionLineId: null, "3/4 inch copper elbow", null, quantity,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor);

    // --- Create ---

    [Fact]
    public void Create_with_valid_fields_succeeds_as_Draft()
    {
        var result = New();

        Assert.True(result.IsSuccess);
        var work = result.Value;
        Assert.Equal(AccountId, work.AccountId);
        Assert.Equal(RequestId, work.RequestId);
        Assert.Equal(ActualWorkStatus.Draft, work.Status);
        Assert.Equal(Actor, work.CreatedByUserId);
        Assert.Equal(Actor, work.RecorderAccountUserId);
        Assert.Null(work.SubmittedAtUtc);
        Assert.Null(work.Outcome);
        Assert.Null(work.CompletionNote);
        Assert.Empty(work.Lines);
        Assert.NotEqual(Guid.Empty, work.ConcurrencyVersion);
    }

    [Fact]
    public void Create_with_empty_request_id_throws()
    {
        Assert.Throws<ArgumentException>(() => ActualWork.Create(AccountId, Guid.Empty, Actor));
    }

    [Fact]
    public void Create_with_empty_created_by_user_id_throws()
    {
        Assert.Throws<ArgumentException>(() => ActualWork.Create(AccountId, RequestId, Guid.Empty));
    }

    // --- AddLine: catalog-backed ---

    [Fact]
    public void AddLine_catalog_backed_with_valid_fields_succeeds()
    {
        var work = New().Value;

        var result = AddCatalogBackedLine(work);

        Assert.True(result.IsSuccess);
        var line = result.Value;
        Assert.Equal(CatalogItemId, line.CatalogItemId);
        Assert.Equal(PriceBookVersionLineId, line.PriceBookVersionLineId);
        Assert.Equal(42.50m, line.SellPriceSnapshot);
        Assert.Equal(18.00m, line.StandardExpectedDirectCostSnapshot);
        Assert.Single(work.Lines);
    }

    [Fact]
    public void AddLine_with_a_price_book_version_line_but_no_catalog_item_fails()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            catalogItemId: null, PriceBookVersionLineId, "Drain Pan", "each", 1m,
            42.50m, 18.00m, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.LinePriceBookVersionLineRequiresCatalogItem, result.Error);
    }

    [Fact]
    public void AddLine_with_snapshot_values_but_no_price_book_version_line_fails()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, priceBookVersionLineId: null, "Drain Pan", "each", 1m,
            42.50m, null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.LineSnapshotValuesRequirePriceBookVersionLine, result.Error);
    }

    // --- AddLine: catalog-backed without a price-book snapshot (state 2 of 3) ---

    [Fact]
    public void AddLine_catalog_item_without_a_price_book_snapshot_succeeds_as_incomplete()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, priceBookVersionLineId: null, "Drain Pan", "each", 1m,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: null, null, null, Actor);

        Assert.True(result.IsSuccess);
        var line = result.Value;
        Assert.Equal(CatalogItemId, line.CatalogItemId);
        Assert.Null(line.PriceBookVersionLineId);
        Assert.Null(line.SellPriceSnapshot);
        Assert.Null(line.StandardExpectedDirectCostSnapshot);
    }

    [Fact]
    public void AddLine_rejects_an_empty_guid_catalog_item_id()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            catalogItemId: Guid.Empty, priceBookVersionLineId: null, "3/4 inch copper elbow", null, 1m,
            null, null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.LineCatalogItemIdEmpty, result.Error);
    }

    [Fact]
    public void AddLine_rejects_an_empty_guid_price_book_version_line_id()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, priceBookVersionLineId: Guid.Empty, "Drain Pan", "each", 1m,
            null, null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.LinePriceBookVersionLineIdEmpty, result.Error);
    }

    // --- AddLine: custom/off-catalog ---

    [Fact]
    public void AddLine_custom_line_with_valid_fields_succeeds()
    {
        var work = New().Value;

        var result = AddCustomLine(work);

        Assert.True(result.IsSuccess);
        var line = result.Value;
        Assert.Null(line.CatalogItemId);
        Assert.Null(line.PriceBookVersionLineId);
        Assert.Null(line.SellPriceSnapshot);
        Assert.Null(line.StandardExpectedDirectCostSnapshot);
    }

    [Fact]
    public void AddLine_may_optionally_link_a_commercial_baseline_source_line()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", 1m,
            42.50m, 18.00m, null, CommercialBaselineSourceLineId, Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommercialBaselineSourceLineId, result.Value.CommercialBaselineSourceLineId);
    }

    // --- AddLine: field-level invariants ---

    [Fact]
    public void AddLine_without_a_display_name_snapshot_fails()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "  ", "each", 1m, 42.50m, 18.00m, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.LineDisplayNameSnapshotRequired, result.Error);
    }

    [Fact]
    public void AddLine_with_a_zero_quantity_fails()
    {
        var work = New().Value;

        var result = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", 0m, 42.50m, 18.00m, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.LineQuantityMustBePositive, result.Error);
    }

    [Fact]
    public void AddLine_after_submit_fails()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, null, null);

        var result = AddCatalogBackedLine(work);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.NotDraft, result.Error);
    }

    // --- UpdateLine / RemoveLine ---

    [Fact]
    public void UpdateLine_changes_quantity_and_note()
    {
        var work = New().Value;
        var line = AddCatalogBackedLine(work).Value;

        var result = work.UpdateLine(line.Id, 3m, "found a second unit");

        Assert.True(result.IsSuccess);
        Assert.Equal(3m, line.ActualQuantity);
        Assert.Equal("found a second unit", line.Note);
    }

    [Fact]
    public void UpdateLine_unknown_line_id_fails()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);

        var result = work.UpdateLine(Guid.CreateVersion7(), 3m, null);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.LineNotFound, result.Error);
    }

    [Fact]
    public void RemoveLine_removes_it_from_the_visit()
    {
        var work = New().Value;
        var line = AddCatalogBackedLine(work).Value;

        var result = work.RemoveLine(line.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(work.Lines);
    }

    [Fact]
    public void RemoveLine_after_submit_fails()
    {
        var work = New().Value;
        var line = AddCatalogBackedLine(work).Value;
        work.Submit(DateTime.UtcNow, null, null);

        var result = work.RemoveLine(line.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.NotDraft, result.Error);
    }

    // --- Submit: with lines ---

    [Fact]
    public void Submit_with_at_least_one_line_and_no_outcome_or_note_succeeds()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);

        var result = work.Submit(DateTime.UtcNow, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(ActualWorkStatus.Submitted, work.Status);
        Assert.NotNull(work.SubmittedAtUtc);
    }

    [Fact]
    public void Submit_with_lines_and_an_undefined_outcome_fails()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);

        var result = work.Submit(DateTime.UtcNow, (ActualWorkOutcome)999, null);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.InvalidOutcome, result.Error);
    }

    [Fact]
    public void Submit_twice_fails()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, null, null);

        var result = work.Submit(DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.NotDraft, result.Error);
    }

    // --- Submit: zero lines ---

    [Fact]
    public void Submit_zero_lines_with_note_and_outcome_succeeds()
    {
        var work = New().Value;

        var result = work.Submit(DateTime.UtcNow, ActualWorkOutcome.NoAccess, "Gate locked, no one on site.");

        Assert.True(result.IsSuccess);
        Assert.Equal(ActualWorkOutcome.NoAccess, work.Outcome);
        Assert.Equal("Gate locked, no one on site.", work.CompletionNote);
    }

    [Fact]
    public void Submit_zero_lines_without_a_completion_note_fails()
    {
        var work = New().Value;

        var result = work.Submit(DateTime.UtcNow, ActualWorkOutcome.DiagnosticOnly, "   ");

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.ZeroLineCompletionNoteRequired, result.Error);
    }

    [Fact]
    public void Submit_zero_lines_without_an_outcome_fails()
    {
        var work = New().Value;

        var result = work.Submit(DateTime.UtcNow, null, "Diagnosed only, no repair authorized.");

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.ZeroLineOutcomeRequired, result.Error);
    }

    [Fact]
    public void Submit_zero_lines_with_an_undefined_outcome_fails_with_invalid_outcome()
    {
        var work = New().Value;

        var result = work.Submit(DateTime.UtcNow, (ActualWorkOutcome)999, "Diagnosed only, no repair authorized.");

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.InvalidOutcome, result.Error);
    }

    // --- TransferRecorder (GAP-055) ---

    [Fact]
    public void TransferRecorder_on_a_draft_changes_the_recorder_and_bumps_the_version()
    {
        var work = New().Value;
        var newRecorder = Guid.CreateVersion7();
        var versionBefore = work.ConcurrencyVersion;

        var result = work.TransferRecorder(newRecorder);

        Assert.True(result.IsSuccess);
        Assert.Equal(newRecorder, work.RecorderAccountUserId);
        Assert.Equal(Actor, work.CreatedByUserId);
        Assert.NotEqual(versionBefore, work.ConcurrencyVersion);
    }

    [Fact]
    public void TransferRecorder_after_submit_fails()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);

        var result = work.TransferRecorder(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.NotDraft, result.Error);
    }

    [Fact]
    public void TransferRecorder_with_an_empty_guid_throws()
    {
        var work = New().Value;

        Assert.Throws<ArgumentException>(() => work.TransferRecorder(Guid.Empty));
    }

    // --- MarkReviewed (Batch 6) ---

    [Fact]
    public void MarkReviewed_on_a_submitted_visit_succeeds()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);
        var reviewer = Guid.CreateVersion7();
        var reviewedAt = DateTime.UtcNow;
        var versionBefore = work.ConcurrencyVersion;

        var result = work.MarkReviewed(
            reviewer, "Looks good.", reviewedAt,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(reviewedAt, work.ReviewedAtUtc);
        Assert.Equal(reviewer, work.ReviewedByAccountUserId);
        Assert.Equal("Looks good.", work.ReviewNote);
        Assert.Equal(ActualWorkStatus.Submitted, work.Status);
        Assert.NotEqual(versionBefore, work.ConcurrencyVersion);
    }

    [Fact]
    public void MarkReviewed_trims_whitespace_only_note_to_null()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), "   ", DateTime.UtcNow,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsSuccess);
        Assert.Null(work.ReviewNote);
    }

    [Fact]
    public void MarkReviewed_trims_surrounding_whitespace_from_a_note()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), "  Looks good.  ", DateTime.UtcNow,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("Looks good.", work.ReviewNote);
    }

    [Fact]
    public void MarkReviewed_with_a_note_over_2000_characters_fails()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), new string('x', 2001), DateTime.UtcNow,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.ReviewNoteTooLong, result.Error);
        Assert.Null(work.ReviewedAtUtc);
    }

    [Fact]
    public void MarkReviewed_on_a_draft_fails()
    {
        var work = New().Value;

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), null, DateTime.UtcNow,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.NotSubmitted, result.Error);
    }

    [Fact]
    public void MarkReviewed_twice_fails_and_does_not_overwrite_the_first_review()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);
        var firstReviewer = Guid.CreateVersion7();
        var firstReviewedAt = DateTime.UtcNow;
        work.MarkReviewed(
            firstReviewer, "First review.", firstReviewedAt,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), "Second review.", DateTime.UtcNow,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.AlreadyReviewed, result.Error);
        Assert.Equal(firstReviewer, work.ReviewedByAccountUserId);
        Assert.Equal(firstReviewedAt, work.ReviewedAtUtc);
        Assert.Equal("First review.", work.ReviewNote);
    }

    [Fact]
    public void MarkReviewed_is_blocked_when_financial_data_is_incomplete()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), null, DateTime.UtcNow,
            financialDataComplete: false, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.ReviewBlockedIncompleteFinancials, result.Error);
        Assert.Null(work.ReviewedAtUtc);
        Assert.Null(work.ReviewedByAccountUserId);
    }

    [Fact]
    public void MarkReviewed_is_blocked_on_a_zero_line_visit_with_no_disposition()
    {
        var work = New().Value;
        work.Submit(DateTime.UtcNow, outcome: ActualWorkOutcome.NoWorkAuthorized, completionNote: "No work needed.");

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), null, DateTime.UtcNow,
            financialDataComplete: true, zeroLineDispositionSatisfied: false);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.ReviewBlockedZeroLineDispositionRequired, result.Error);
        Assert.Null(work.ReviewedAtUtc);
    }

    [Fact]
    public void MarkReviewed_succeeds_on_a_zero_line_visit_once_disposition_is_satisfied()
    {
        var work = New().Value;
        work.Submit(DateTime.UtcNow, outcome: ActualWorkOutcome.NoWorkAuthorized, completionNote: "No work needed.");

        var result = work.MarkReviewed(
            Guid.CreateVersion7(), null, DateTime.UtcNow,
            financialDataComplete: true, zeroLineDispositionSatisfied: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(work.ReviewedAtUtc);
    }

    // --- RefreshConcurrencyVersionForFinancialResolution (BL135 §4 Batch 3a-ii) ---

    [Fact]
    public void RefreshConcurrencyVersionForFinancialResolution_rotates_the_token()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);
        var versionBefore = work.ConcurrencyVersion;

        work.RefreshConcurrencyVersionForFinancialResolution();

        Assert.NotEqual(versionBefore, work.ConcurrencyVersion);
        Assert.NotEqual(Guid.Empty, work.ConcurrencyVersion);
    }

    [Fact]
    public void RefreshConcurrencyVersionForFinancialResolution_touches_only_the_token()
    {
        var work = New().Value;
        AddCatalogBackedLine(work);
        work.Submit(DateTime.UtcNow, outcome: null, completionNote: null);

        work.RefreshConcurrencyVersionForFinancialResolution();

        Assert.Equal(ActualWorkStatus.Submitted, work.Status);
        Assert.Null(work.ReviewedAtUtc);
        Assert.Single(work.Lines);
    }
}
