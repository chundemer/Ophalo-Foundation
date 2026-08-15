using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the ProposedScope/ProposedScopeLine domain (ADR-461, ADR-480, ADR-481, build-log/108,
/// Session 3.3a.1): Create, line capture rules per LineType, Draft-only mutability, and the pure
/// Submit status transition. No persistence, no KeepRequestWorkSignal coordination, and no
/// terminal-request precondition here — those are Session 3.3a.2/3.3b concerns.
/// </summary>
public class ProposedScopeTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid RequestId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();
    static readonly Guid OfferingAssemblyId = Guid.CreateVersion7();

    static Result<ProposedScope> New() => ProposedScope.Create(AccountId, RequestId, Actor);

    static Result<ProposedScopeLine> AddKnownCatalogItemLine(ProposedScope scope, decimal quantity = 1m, int displayOrder = 0) =>
        scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, CatalogItemId, null, quantity, isException: false,
            offCatalogDescription: null, offCatalogQuantity: null, note: null, displayOrder,
            displayNameSnapshot: "Drain Pan", unitOfMeasureSnapshot: "each",
            offeringAssemblyNameSnapshot: null, defaultQuantitySnapshot: null, Actor);

    static Result<ProposedScopeLine> AddPrimaryOfferingLine(ProposedScope scope, decimal quantity = 1m) =>
        scope.AddLine(
            ProposedScopeLineType.PrimaryOffering, CatalogItemId, OfferingAssemblyId, quantity, isException: false,
            offCatalogDescription: null, offCatalogQuantity: null, note: null, displayOrder: 0,
            displayNameSnapshot: "Control Board", unitOfMeasureSnapshot: "each",
            offeringAssemblyNameSnapshot: "Control Board Replacement", defaultQuantitySnapshot: null, Actor);

    static Result<ProposedScopeLine> AddAssociatedItemLine(
        ProposedScope scope, decimal quantity = 1m, bool isException = false, decimal defaultQuantitySnapshot = 1m) =>
        scope.AddLine(
            ProposedScopeLineType.AssociatedItem, CatalogItemId, OfferingAssemblyId, quantity, isException,
            offCatalogDescription: null, offCatalogQuantity: null, note: null, displayOrder: 0,
            displayNameSnapshot: "Labor", unitOfMeasureSnapshot: "hour",
            offeringAssemblyNameSnapshot: "Control Board Replacement", defaultQuantitySnapshot, Actor);

    static Result<ProposedScopeLine> AddOffCatalogLine(ProposedScope scope, decimal quantity = 2m) =>
        scope.AddLine(
            ProposedScopeLineType.OffCatalogItem, null, null, quantity, isException: false,
            offCatalogDescription: "3/4 inch copper elbow", offCatalogQuantity: quantity, note: null, displayOrder: 0,
            displayNameSnapshot: "3/4 inch copper elbow", unitOfMeasureSnapshot: null,
            offeringAssemblyNameSnapshot: null, defaultQuantitySnapshot: null, Actor);

    // --- Create ---

    [Fact]
    public void Create_with_valid_fields_succeeds_as_Draft()
    {
        var result = New();

        Assert.True(result.IsSuccess);
        var scope = result.Value;
        Assert.Equal(AccountId, scope.AccountId);
        Assert.Equal(RequestId, scope.RequestId);
        Assert.Equal(ProposedScopeStatus.Draft, scope.Status);
        Assert.Equal(Actor, scope.CreatedByUserId);
        Assert.Null(scope.SubmittedAtUtc);
        Assert.Empty(scope.Lines);
        Assert.NotEqual(Guid.Empty, scope.ConcurrencyVersion);
    }

    [Fact]
    public void Create_with_empty_request_id_throws()
    {
        Assert.Throws<ArgumentException>(() => ProposedScope.Create(AccountId, Guid.Empty, Actor));
    }

    // --- AddLine: KnownCatalogItem ---

    [Fact]
    public void AddLine_KnownCatalogItem_with_valid_fields_succeeds()
    {
        var scope = New().Value;

        var result = AddKnownCatalogItemLine(scope);

        Assert.True(result.IsSuccess);
        var line = result.Value;
        Assert.Equal(CatalogItemId, line.CatalogItemId);
        Assert.Null(line.OfferingAssemblyId);
        Assert.Equal("Drain Pan", line.DisplayNameSnapshot);
        Assert.Single(scope.Lines);
    }

    [Fact]
    public void AddLine_KnownCatalogItem_without_a_catalog_item_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, null, null, 1m, false,
            null, null, null, 0, "Drain Pan", "each", null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineCatalogItemRequired, result.Error);
    }

    [Fact]
    public void AddLine_KnownCatalogItem_with_an_offering_assembly_reference_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, CatalogItemId, OfferingAssemblyId, 1m, false,
            null, null, null, 0, "Drain Pan", "each", null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineOfferingAssemblyMustBeEmpty, result.Error);
    }

    // --- AddLine: PrimaryOffering / AssociatedItem ---

    [Fact]
    public void AddLine_PrimaryOffering_with_valid_fields_succeeds()
    {
        var scope = New().Value;

        var result = AddPrimaryOfferingLine(scope);

        Assert.True(result.IsSuccess);
        var line = result.Value;
        Assert.Equal(OfferingAssemblyId, line.OfferingAssemblyId);
        Assert.Equal("Control Board Replacement", line.OfferingAssemblyNameSnapshot);
        Assert.Null(line.DefaultQuantitySnapshot);
        Assert.False(line.IsException);
    }

    [Fact]
    public void AddLine_PrimaryOffering_with_IsException_true_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.PrimaryOffering, CatalogItemId, OfferingAssemblyId, 1m, isException: true,
            null, null, null, 0, "Control Board", "each", "Control Board Replacement", null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem, result.Error);
    }

    [Fact]
    public void AddLine_PrimaryOffering_with_a_default_quantity_snapshot_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.PrimaryOffering, CatalogItemId, OfferingAssemblyId, 1m, false,
            null, null, null, 0, "Control Board", "each", "Control Board Replacement", 1m, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineDefaultQuantitySnapshotMustBeEmpty, result.Error);
    }

    // --- AddLine: AssociatedItem (IsException / DefaultQuantitySnapshot) ---

    [Fact]
    public void AddLine_AssociatedItem_with_valid_fields_and_IsException_true_succeeds()
    {
        var scope = New().Value;

        var result = AddAssociatedItemLine(scope, isException: true, defaultQuantitySnapshot: 1m);

        Assert.True(result.IsSuccess);
        var line = result.Value;
        Assert.True(line.IsException);
        Assert.Equal(1m, line.DefaultQuantitySnapshot);
    }

    [Fact]
    public void AddLine_AssociatedItem_without_a_default_quantity_snapshot_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.AssociatedItem, CatalogItemId, OfferingAssemblyId, 1m, false,
            null, null, null, 0, "Labor", "hour", "Control Board Replacement", null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineDefaultQuantitySnapshotRequired, result.Error);
    }

    [Fact]
    public void AddLine_AssociatedItem_with_a_zero_default_quantity_snapshot_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.AssociatedItem, CatalogItemId, OfferingAssemblyId, 1m, false,
            null, null, null, 0, "Labor", "hour", "Control Board Replacement", 0m, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineDefaultQuantitySnapshotRequired, result.Error);
    }

    [Fact]
    public void AddLine_KnownCatalogItem_with_IsException_true_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, CatalogItemId, null, 1m, isException: true,
            null, null, null, 0, "Drain Pan", "each", null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem, result.Error);
    }

    [Fact]
    public void AddLine_OffCatalogItem_with_IsException_true_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.OffCatalogItem, null, null, 2m, isException: true,
            "Elbow", 2m, null, 0, "Elbow", null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem, result.Error);
    }

    // --- AddLine: UnitOfMeasureSnapshot ---

    [Fact]
    public void AddLine_KnownCatalogItem_without_a_unit_of_measure_snapshot_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, CatalogItemId, null, 1m, false,
            null, null, null, 0, "Drain Pan", "  ", null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineUnitOfMeasureSnapshotRequired, result.Error);
    }

    [Fact]
    public void AddLine_OffCatalogItem_ignores_a_supplied_unit_of_measure_snapshot()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.OffCatalogItem, null, null, 2m, false,
            "Elbow", 2m, null, 0, "Elbow", "each", null, null, Actor);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.UnitOfMeasureSnapshot);
    }

    // --- AddLine: Quantity / OffCatalogQuantity coupling ---

    [Fact]
    public void AddLine_OffCatalogItem_with_mismatched_off_catalog_quantity_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.OffCatalogItem, null, null, 2m, false,
            "Elbow", 5m, null, 0, "Elbow", null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineOffCatalogQuantityMustMatchQuantity, result.Error);
    }

    [Fact]
    public void AddLine_AssociatedItem_without_an_offering_assembly_reference_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.AssociatedItem, CatalogItemId, null, 1m, false,
            null, null, null, 0, "Labor", "hour", null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineOfferingAssemblyRequired, result.Error);
    }

    [Fact]
    public void AddLine_PrimaryOffering_without_a_catalog_item_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.PrimaryOffering, null, OfferingAssemblyId, 1m, false,
            null, null, null, 0, "Control Board", "each", "Control Board Replacement", 1m, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineCatalogItemRequired, result.Error);
    }

    // --- AddLine: OffCatalogItem ---

    [Fact]
    public void AddLine_OffCatalogItem_with_valid_fields_succeeds()
    {
        var scope = New().Value;

        var result = AddOffCatalogLine(scope);

        Assert.True(result.IsSuccess);
        var line = result.Value;
        Assert.Null(line.CatalogItemId);
        Assert.Equal("3/4 inch copper elbow", line.OffCatalogDescription);
        Assert.Equal(2m, line.OffCatalogQuantity);
    }

    [Fact]
    public void AddLine_OffCatalogItem_with_a_catalog_item_reference_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.OffCatalogItem, CatalogItemId, null, 1m, false,
            "Elbow", 1m, null, 0, "Elbow", null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineCatalogItemMustBeEmptyForOffCatalog, result.Error);
    }

    [Fact]
    public void AddLine_OffCatalogItem_without_a_description_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.OffCatalogItem, null, null, 1m, false,
            "  ", 1m, null, 0, "Elbow", null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineOffCatalogDescriptionRequired, result.Error);
    }

    [Fact]
    public void AddLine_OffCatalogItem_with_a_missing_off_catalog_quantity_fails()
    {
        var scope = New().Value;

        var result = scope.AddLine(
            ProposedScopeLineType.OffCatalogItem, null, null, 1m, false,
            "Elbow", null, null, 0, "Elbow", null, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineOffCatalogQuantityMustMatchQuantity, result.Error);
    }

    // --- AddLine: shared validation ---

    [Fact]
    public void AddLine_with_zero_quantity_fails()
    {
        var scope = New().Value;

        var result = AddKnownCatalogItemLine(scope, quantity: 0m);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineQuantityMustBePositive, result.Error);
    }

    [Fact]
    public void AddLine_with_negative_display_order_fails()
    {
        var scope = New().Value;

        var result = AddKnownCatalogItemLine(scope, displayOrder: -1);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineDisplayOrderMustNotBeNegative, result.Error);
    }

    [Fact]
    public void AddLine_when_not_Draft_fails()
    {
        var scope = New().Value;
        AddKnownCatalogItemLine(scope);
        scope.Submit(DateTime.UtcNow);

        var result = AddKnownCatalogItemLine(scope, displayOrder: 1);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.NotDraft, result.Error);
    }

    // --- UpdateLine ---

    [Fact]
    public void UpdateLine_while_Draft_updates_quantity_exception_note_and_display_order()
    {
        var scope = New().Value;
        var line = AddAssociatedItemLine(scope).Value;

        var result = scope.UpdateLine(line.Id, 5m, isException: true, note: "Swapped size", displayOrder: 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, line.Quantity);
        Assert.True(line.IsException);
        Assert.Equal("Swapped size", line.Note);
        Assert.Equal(2, line.DisplayOrder);
    }

    [Fact]
    public void UpdateLine_does_not_change_LineType_or_snapshot_fields()
    {
        var scope = New().Value;
        var line = AddPrimaryOfferingLine(scope).Value;

        scope.UpdateLine(line.Id, 3m, isException: false, note: null, displayOrder: 1);

        Assert.Equal(ProposedScopeLineType.PrimaryOffering, line.LineType);
        Assert.Equal("Control Board", line.DisplayNameSnapshot);
        Assert.Null(line.DefaultQuantitySnapshot);
    }

    [Fact]
    public void UpdateLine_with_IsException_true_on_a_non_AssociatedItem_line_fails()
    {
        var scope = New().Value;
        var line = AddPrimaryOfferingLine(scope).Value;

        var result = scope.UpdateLine(line.Id, 1m, isException: true, note: null, displayOrder: 0);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem, result.Error);
    }

    [Fact]
    public void UpdateLine_on_an_OffCatalogItem_line_keeps_OffCatalogQuantity_in_sync_with_Quantity()
    {
        var scope = New().Value;
        var line = AddOffCatalogLine(scope, quantity: 2m).Value;

        var result = scope.UpdateLine(line.Id, 7m, isException: false, note: null, displayOrder: 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(7m, line.Quantity);
        Assert.Equal(7m, line.OffCatalogQuantity);
    }

    [Fact]
    public void UpdateLine_for_an_unknown_line_id_fails()
    {
        var scope = New().Value;

        var result = scope.UpdateLine(Guid.CreateVersion7(), 1m, false, null, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineNotFound, result.Error);
    }

    [Fact]
    public void UpdateLine_when_not_Draft_fails()
    {
        var scope = New().Value;
        var line = AddKnownCatalogItemLine(scope).Value;
        scope.Submit(DateTime.UtcNow);

        var result = scope.UpdateLine(line.Id, 2m, false, null, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.NotDraft, result.Error);
    }

    // --- RemoveLine ---

    [Fact]
    public void RemoveLine_while_Draft_removes_the_line()
    {
        var scope = New().Value;
        var line = AddKnownCatalogItemLine(scope).Value;

        var result = scope.RemoveLine(line.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(scope.Lines);
    }

    [Fact]
    public void RemoveLine_for_an_unknown_line_id_fails()
    {
        var scope = New().Value;

        var result = scope.RemoveLine(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.LineNotFound, result.Error);
    }

    [Fact]
    public void RemoveLine_when_not_Draft_fails()
    {
        var scope = New().Value;
        var line = AddKnownCatalogItemLine(scope).Value;
        scope.Submit(DateTime.UtcNow);

        var result = scope.RemoveLine(line.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.NotDraft, result.Error);
    }

    // --- Submit ---

    [Fact]
    public void Submit_a_Draft_scope_transitions_to_SubmittedToOffice()
    {
        var scope = New().Value;
        AddKnownCatalogItemLine(scope);
        var priorVersion = scope.ConcurrencyVersion;
        var now = DateTime.UtcNow;

        var result = scope.Submit(now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProposedScopeStatus.SubmittedToOffice, scope.Status);
        Assert.Equal(now, scope.SubmittedAtUtc);
        Assert.NotEqual(priorVersion, scope.ConcurrencyVersion);
    }

    [Fact]
    public void Submit_an_already_submitted_scope_fails()
    {
        var scope = New().Value;
        AddKnownCatalogItemLine(scope);
        scope.Submit(DateTime.UtcNow);

        var result = scope.Submit(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.NotDraft, result.Error);
    }

    [Fact]
    public void Submit_a_scope_with_zero_lines_fails()
    {
        var scope = New().Value;
        var priorVersion = scope.ConcurrencyVersion;

        var result = scope.Submit(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProposedScopeErrors.EmptySubmit, result.Error);
        Assert.Equal(ProposedScopeStatus.Draft, scope.Status);
        Assert.Equal(priorVersion, scope.ConcurrencyVersion);
    }
}
