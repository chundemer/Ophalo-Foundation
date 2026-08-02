using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks PriceBookImportValidationService (build-log/108, build-log/110, Session 2c.1b): the
/// per-field validation rule engine, the account-scoped/not-found-on-cross-account persistence
/// contract, the Pending-count gate for Staged -&gt; Validated, the exception-resolution policy
/// (Warning-only Accepted, Warning/Error Skipped, revalidated Corrected), and the parent-import
/// lifecycle gate (only a Staged import accepts validation; only Staged/Validated accept
/// resolution/correction — Discarded/Published/PublishFailed reject both). The fake persistence
/// implementations below prove row-scale safety structurally: <see cref="IPriceBookImportRowPersistence"/>
/// has no method that can return more than one row or a full import's row set, and
/// <see cref="FakeImportPersistence"/> tracks that <see cref="PriceBookImportValidationService.TryMarkValidatedAsync"/>
/// never reads <see cref="PriceBookImport.Rows"/> even when it is empty on the loaded root (as a
/// real EF root-only load, with no <c>.Include</c>, would leave it), and that every row-scoped
/// lifecycle check loads the parent import exactly once (never the row set).
/// </summary>
public class PriceBookImportValidationServiceTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid OtherAccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();

    static PriceBookImport NewImport(Guid? accountId = null) =>
        PriceBookImport.Create(accountId ?? AccountId, "imports/opaque-key.csv", Actor, DateTime.UtcNow).Value;

    static PriceBookImportRow AddRow(
        PriceBookImport import,
        int rowNumber = 1,
        string? type = "Material",
        string? displayName = "1/2\" Copper Pipe",
        string? externalKey = null,
        string? unitOfMeasure = "ft",
        decimal? sellPrice = 3.00m,
        string? currency = "USD",
        Guid? mappedCatalogItemId = null) =>
        import.AddRow(
            rowNumber, "Materials", mappedCatalogItemId, type, displayName, externalKey, "Plumbing",
            unitOfMeasure, 1.25m, sellPrice, currency, null, null, null, Actor).Value;

    /// <summary>Builds fakes, a service, and a <c>Staged</c> import already registered with the
    /// fake import persistence (the common case — most tests need a mutable parent).</summary>
    static (FakeImportPersistence Imports, FakeRowPersistence Rows, FakeCatalogItemPersistence CatalogItems, PriceBookImportValidationService Sut, PriceBookImport Import)
        Harness()
    {
        var imports = new FakeImportPersistence();
        var rows = new FakeRowPersistence();
        var catalogItems = new FakeCatalogItemPersistence();
        var sut = new PriceBookImportValidationService(imports, rows, catalogItems);
        var import = NewImport();
        imports.Imports.Add(import);
        return (imports, rows, catalogItems, sut, import);
    }

    // --- ValidateRowAsync: field rules ---

    [Fact]
    public async Task ValidateRowAsync_with_all_valid_fields_marks_Valid()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
        Assert.Equal(PriceBookImportRowValidationStatus.Valid, row.ValidationStatus);
        Assert.Empty(row.ValidationMessages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Widget")]
    public async Task ValidateRowAsync_blank_or_unrecognized_type_fails(string? type)
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, type: type);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowTypeInvalid.Message, row.ValidationMessages);
    }

    [Theory]
    [InlineData("material")]
    [InlineData("SERVICE")]
    public async Task ValidateRowAsync_type_matches_case_insensitively(string type)
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, type: type);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ValidateRowAsync_blank_display_name_fails(string? displayName)
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, displayName: displayName);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowDisplayNameInvalid.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_display_name_over_200_chars_fails()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, displayName: new string('x', 201));
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowDisplayNameInvalid.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_display_name_at_200_chars_succeeds()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, displayName: new string('x', 200));
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ValidateRowAsync_blank_unit_of_measure_fails(string? unit)
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, unitOfMeasure: unit);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowUnitOfMeasureInvalid.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_unit_of_measure_over_50_chars_fails()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, unitOfMeasure: new string('x', 51));
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
    }

    [Fact]
    public async Task ValidateRowAsync_negative_sell_price_fails()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, sellPrice: -0.01m);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowSellPriceNegative.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_null_sell_price_succeeds()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, sellPrice: null);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    [Fact]
    public async Task ValidateRowAsync_zero_sell_price_succeeds()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, sellPrice: 0m);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    [Fact]
    public async Task ValidateRowAsync_positive_sell_price_succeeds()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, sellPrice: 1.5m);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("12D")]
    public async Task ValidateRowAsync_invalid_currency_fails(string? currency)
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, currency: currency);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowCurrencyInvalid.Message, row.ValidationMessages);
    }

    // --- ValidateRowAsync: external key ---

    [Fact]
    public async Task ValidateRowAsync_duplicate_external_key_within_import_fails()
    {
        var (_, rows, _, sut, import) = Harness();
        var first = AddRow(import, rowNumber: 1, externalKey: "CU-12");
        var second = AddRow(import, rowNumber: 2, externalKey: "CU-12");
        rows.Rows.Add(first);
        rows.Rows.Add(second);

        var result = await sut.ValidateRowAsync(AccountId, second.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowExternalKeyDuplicate.Message, second.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_blank_external_key_never_flagged_as_duplicate()
    {
        var (_, rows, _, sut, import) = Harness();
        var first = AddRow(import, rowNumber: 1, externalKey: null);
        var second = AddRow(import, rowNumber: 2, externalKey: null);
        rows.Rows.Add(first);
        rows.Rows.Add(second);

        var result = await sut.ValidateRowAsync(AccountId, second.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    [Fact]
    public async Task ValidateRowAsync_external_key_colliding_with_existing_catalog_item_fails()
    {
        var (_, rows, catalogItems, sut, import) = Harness();
        catalogItems.Items.Add(ExistingCatalogItem("CU-12", CatalogItemActiveState.Active));
        var row = AddRow(import, externalKey: "CU-12");
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowExternalKeyDuplicate.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_external_key_colliding_with_Inactive_catalog_item_still_fails()
    {
        var (_, rows, catalogItems, sut, import) = Harness();
        catalogItems.Items.Add(ExistingCatalogItem("CU-12", CatalogItemActiveState.Inactive));
        var row = AddRow(import, externalKey: "CU-12");
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
    }

    [Fact]
    public async Task ValidateRowAsync_external_key_colliding_but_explicitly_mapped_to_same_item_succeeds()
    {
        var (_, rows, catalogItems, sut, import) = Harness();
        var existing = ExistingCatalogItem("CU-12", CatalogItemActiveState.Active);
        catalogItems.Items.Add(existing);
        var row = AddRow(import, externalKey: "CU-12", mappedCatalogItemId: existing.Id);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    static CatalogItem ExistingCatalogItem(string externalKey, CatalogItemActiveState state, Guid? accountId = null)
    {
        var item = CatalogItem.CreateDraft(
            accountId ?? AccountId, CatalogItemType.Material, "Existing Item", "ft", "USD",
            externalKey, null, false, Actor).Value;
        if (state == CatalogItemActiveState.Active) item.Activate();
        if (state == CatalogItemActiveState.Inactive) { item.Activate(); item.Inactivate(); }
        return item;
    }

    // --- ValidateRowAsync: MappedCatalogItemId ---

    [Fact]
    public async Task ValidateRowAsync_mapped_item_not_found_fails()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, mappedCatalogItemId: Guid.CreateVersion7());
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowMappedCatalogItemNotFound.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_mapped_item_in_different_account_fails_as_not_found()
    {
        var (_, rows, catalogItems, sut, import) = Harness();
        var otherAccountItem = ExistingCatalogItem("SKU-X", CatalogItemActiveState.Active, OtherAccountId);
        catalogItems.Items.Add(otherAccountItem);
        var row = AddRow(import, mappedCatalogItemId: otherAccountItem.Id);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowMappedCatalogItemNotFound.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_mapped_item_Draft_fails_NotActive()
    {
        var (_, rows, catalogItems, sut, import) = Harness();
        var draftItem = ExistingCatalogItem("SKU-D", CatalogItemActiveState.Draft);
        catalogItems.Items.Add(draftItem);
        var row = AddRow(import, mappedCatalogItemId: draftItem.Id);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowMappedCatalogItemNotActive.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_mapped_item_Inactive_fails_NotActive()
    {
        var (_, rows, catalogItems, sut, import) = Harness();
        var inactiveItem = ExistingCatalogItem("SKU-I", CatalogItemActiveState.Inactive);
        catalogItems.Items.Add(inactiveItem);
        var row = AddRow(import, mappedCatalogItemId: inactiveItem.Id);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowMappedCatalogItemNotActive.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_mapped_item_Active_succeeds()
    {
        var (_, rows, catalogItems, sut, import) = Harness();
        var activeItem = ExistingCatalogItem("SKU-A", CatalogItemActiveState.Active);
        catalogItems.Items.Add(activeItem);
        var row = AddRow(import, mappedCatalogItemId: activeItem.Id);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
    }

    [Fact]
    public async Task ValidateRowAsync_combines_multiple_errors_into_one_Error_status()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, type: null, displayName: null, sellPrice: -1m);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.Equal(PriceBookImportRowValidationStatus.Error, result.Value);
        Assert.Contains(PriceBookImportErrors.RowTypeInvalid.Message, row.ValidationMessages);
        Assert.Contains(PriceBookImportErrors.RowDisplayNameInvalid.Message, row.ValidationMessages);
        Assert.Contains(PriceBookImportErrors.RowSellPriceNegative.Message, row.ValidationMessages);
    }

    [Fact]
    public async Task ValidateRowAsync_unknown_row_fails_RowNotFound()
    {
        var (_, _, _, sut, _) = Harness();

        var result = await sut.ValidateRowAsync(AccountId, Guid.CreateVersion7(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowNotFound, result.Error);
    }

    [Fact]
    public async Task ValidateRowAsync_row_in_different_account_fails_RowNotFound()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        rows.Rows.Add(row);

        var result = await sut.ValidateRowAsync(OtherAccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowNotFound, result.Error);
    }

    // --- Parent import lifecycle gate ---

    [Fact]
    public async Task ValidateRowAsync_on_Discarded_import_fails_ImportNotMutable_and_leaves_row_unchanged()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        rows.Rows.Add(row);
        import.Discard();

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.ImportNotMutable, result.Error);
        Assert.Equal(PriceBookImportRowValidationStatus.Pending, row.ValidationStatus);
    }

    [Fact]
    public async Task ValidateRowAsync_on_Validated_import_fails_ImportNotMutable()
    {
        // Validation only runs against a Staged import — Validated means "every row already
        // evaluated," not "open for re-validation."
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        var second = AddRow(import, rowNumber: 2);
        rows.Rows.Add(row);
        rows.Rows.Add(second);
        row.MarkValid();
        // Domain-level MarkValidated() has no row-state check by design (that gate is the
        // service's TryMarkValidatedAsync) — a Pending sibling can still be present here.
        import.MarkValidated();

        var result = await sut.ValidateRowAsync(AccountId, second.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.ImportNotMutable, result.Error);
    }

    [Fact]
    public async Task ResolveAcceptedAsync_on_Discarded_import_fails_ImportNotMutable_and_leaves_row_unchanged()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        row.MarkWarning(["needs review"]);
        rows.Rows.Add(row);
        import.Discard();

        var result = await sut.ResolveAcceptedAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.ImportNotMutable, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
    }

    [Fact]
    public async Task ResolveSkippedAsync_on_Discarded_import_fails_ImportNotMutable()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        row.MarkError(["bad value"]);
        rows.Rows.Add(row);
        import.Discard();

        var result = await sut.ResolveSkippedAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.ImportNotMutable, result.Error);
    }

    [Fact]
    public async Task ResolveCorrectedAsync_on_Discarded_import_fails_ImportNotMutable_and_leaves_row_unchanged()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, type: null);
        row.MarkError(["blank type"]);
        rows.Rows.Add(row);
        import.Discard();

        var result = await sut.ResolveCorrectedAsync(AccountId, row.Id, ValidCorrection(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.ImportNotMutable, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
        Assert.NotEqual("Corrected Copper Pipe", row.ProposedDisplayName);
    }

    [Fact]
    public async Task ResolveAcceptedAsync_on_Validated_import_succeeds()
    {
        // Resolution/correction stays open through Validated — that's exactly when an office user
        // works the exception queue, after validation has already run once.
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        row.MarkWarning(["needs review"]);
        rows.Rows.Add(row);
        import.MarkValidated();

        var result = await sut.ResolveAcceptedAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowExceptionResolution.Accepted, row.ExceptionResolution);
    }

    [Fact]
    public async Task ValidateRowAsync_for_unknown_parent_import_fails_NotFound()
    {
        // Structurally unreachable through the domain (a row's PriceBookImportId always references
        // a real import via the composite FK), but the service must not assume that and instead
        // surfaces a clear failure rather than a null-reference.
        var imports = new FakeImportPersistence();
        var rows = new FakeRowPersistence();
        var catalogItems = new FakeCatalogItemPersistence();
        var sut = new PriceBookImportValidationService(imports, rows, catalogItems);
        var orphanImport = NewImport();
        var row = AddRow(orphanImport);
        rows.Rows.Add(row);
        // Deliberately not registering orphanImport with `imports`.

        var result = await sut.ValidateRowAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.NotFound, result.Error);
    }

    // --- TryMarkValidatedAsync ---

    [Fact]
    public async Task TryMarkValidatedAsync_with_pending_rows_fails_RowsPending()
    {
        var (_, rows, _, sut, import) = Harness();
        rows.PendingCountOverride = 3;

        var result = await sut.TryMarkValidatedAsync(AccountId, import.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowsPending, result.Error);
        Assert.Equal(PriceBookImportStatus.Staged, import.Status);
    }

    [Fact]
    public async Task TryMarkValidatedAsync_with_zero_pending_transitions_import()
    {
        var (_, rows, _, sut, import) = Harness();
        rows.PendingCountOverride = 0;

        var result = await sut.TryMarkValidatedAsync(AccountId, import.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportStatus.Validated, import.Status);
    }

    [Fact]
    public async Task TryMarkValidatedAsync_unknown_import_fails_NotFound()
    {
        var (_, rows, _, sut, _) = Harness();
        rows.PendingCountOverride = 0;

        var result = await sut.TryMarkValidatedAsync(AccountId, Guid.CreateVersion7(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task TryMarkValidatedAsync_transitions_using_only_the_row_persistence_count_never_import_Rows()
    {
        // The loaded import's in-memory Rows is empty here (never populated via AddRow), exactly
        // as a real EF root-only load (no .Include) would leave it. If the service read
        // import.Rows instead of trusting rowPersistence.CountByStatusAsync, this would either
        // throw or silently treat the import as having no rows at all in a way that masks a bug;
        // succeeding here proves the transition is driven entirely by the row-persistence count.
        var (_, rows, _, sut, import) = Harness();
        rows.PendingCountOverride = 0;

        var result = await sut.TryMarkValidatedAsync(AccountId, import.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(import.Rows);
    }

    // --- Row-scale safety ---

    [Fact]
    public async Task ValidateRowAsync_loads_exactly_the_target_row_and_the_parent_import_exactly_once()
    {
        var (imports, rows, _, sut, import) = Harness();
        var target = AddRow(import, rowNumber: 1);
        var sibling = AddRow(import, rowNumber: 2);
        rows.Rows.Add(target);
        rows.Rows.Add(sibling);

        await sut.ValidateRowAsync(AccountId, target.Id, CancellationToken.None);

        Assert.Equal([target.Id], rows.GetByIdCalledWith);
        Assert.Equal(1, imports.GetByIdCallCount);
    }

    [Fact]
    public async Task ResolveAcceptedAsync_loads_the_parent_import_exactly_once_for_the_lifecycle_check()
    {
        var (imports, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        row.MarkWarning(["needs review"]);
        rows.Rows.Add(row);

        var result = await sut.ResolveAcceptedAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowExceptionResolution.Accepted, row.ExceptionResolution);
        Assert.Equal(1, imports.GetByIdCallCount);
    }

    // --- ResolveAcceptedAsync / ResolveSkippedAsync policy ---

    [Fact]
    public async Task ResolveAcceptedAsync_on_Error_row_fails_RowErrorCannotBeAccepted()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        row.MarkError(["bad value"]);
        rows.Rows.Add(row);

        var result = await sut.ResolveAcceptedAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowErrorCannotBeAccepted, result.Error);
    }

    [Fact]
    public async Task ResolveAcceptedAsync_unknown_row_fails_RowNotFound()
    {
        var (_, _, _, sut, _) = Harness();

        var result = await sut.ResolveAcceptedAsync(AccountId, Guid.CreateVersion7(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowNotFound, result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveSkippedAsync_on_Warning_or_Error_succeeds(bool isWarning)
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        if (isWarning) row.MarkWarning(["needs review"]); else row.MarkError(["bad value"]);
        rows.Rows.Add(row);

        var result = await sut.ResolveSkippedAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowExceptionResolution.Skipped, row.ExceptionResolution);
    }

    [Fact]
    public async Task ResolveSkippedAsync_on_Pending_row_fails_RowHasNoException()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import);
        rows.Rows.Add(row);

        var result = await sut.ResolveSkippedAsync(AccountId, row.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowHasNoException, result.Error);
    }

    // --- ResolveCorrectedAsync ---

    static CorrectPriceBookImportRowCommand ValidCorrection() =>
        new("Material", "Corrected Copper Pipe", "CU-99", "Plumbing", "ft", 1.25m, 3.00m, "USD", null, null, null, null);

    [Fact]
    public async Task ResolveCorrectedAsync_with_valid_values_succeeds_and_persists()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, type: null);
        row.MarkError(["blank type"]);
        rows.Rows.Add(row);

        var result = await sut.ResolveCorrectedAsync(AccountId, row.Id, ValidCorrection(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookImportRowValidationStatus.Valid, result.Value);
        Assert.Equal(PriceBookImportRowValidationStatus.Valid, row.ValidationStatus);
        Assert.Equal(PriceBookImportRowExceptionResolution.Corrected, row.ExceptionResolution);
        Assert.Equal("Corrected Copper Pipe", row.ProposedDisplayName);
    }

    [Fact]
    public async Task ResolveCorrectedAsync_with_still_invalid_values_fails_and_leaves_row_unchanged()
    {
        var (_, rows, _, sut, import) = Harness();
        var row = AddRow(import, type: null);
        row.MarkError(["blank type"]);
        rows.Rows.Add(row);
        var stillBadCorrection = ValidCorrection() with { ProposedType = "NotAType" };

        var result = await sut.ResolveCorrectedAsync(AccountId, row.Id, stillBadCorrection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowCorrectionStillInvalid, result.Error);
        Assert.Equal(PriceBookImportRowExceptionResolution.Unresolved, row.ExceptionResolution);
        Assert.Equal(PriceBookImportRowValidationStatus.Error, row.ValidationStatus);
        Assert.NotEqual("Corrected Copper Pipe", row.ProposedDisplayName);
        Assert.Contains("blank type", row.ValidationMessages);
    }

    [Fact]
    public async Task ResolveCorrectedAsync_unknown_row_fails_RowNotFound()
    {
        var (_, _, _, sut, _) = Harness();

        var result = await sut.ResolveCorrectedAsync(AccountId, Guid.CreateVersion7(), ValidCorrection(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookImportErrors.RowNotFound, result.Error);
    }

    sealed class FakeImportPersistence : IPriceBookImportPersistence
    {
        public List<PriceBookImport> Imports { get; } = [];
        public int GetByIdCallCount { get; private set; }

        public Task<PriceBookImport?> GetByIdAsync(Guid accountId, Guid importId, CancellationToken ct)
        {
            GetByIdCallCount++;
            return Task.FromResult(Imports.FirstOrDefault(x => x.AccountId == accountId && x.Id == importId));
        }

        public Task CommitAsync(PriceBookImport import, CancellationToken ct) => Task.CompletedTask;
    }

    sealed class FakeRowPersistence : IPriceBookImportRowPersistence
    {
        public List<PriceBookImportRow> Rows { get; } = [];
        public List<Guid> GetByIdCalledWith { get; } = [];
        public int? PendingCountOverride { get; set; }

        public Task<PriceBookImportRow?> GetByIdAsync(Guid accountId, Guid rowId, CancellationToken ct)
        {
            GetByIdCalledWith.Add(rowId);
            return Task.FromResult(Rows.FirstOrDefault(x => x.AccountId == accountId && x.Id == rowId));
        }

        public Task CommitAsync(PriceBookImportRow row, CancellationToken ct) => Task.CompletedTask;

        public Task<int> CountByStatusAsync(
            Guid accountId, Guid importId, PriceBookImportRowValidationStatus status, CancellationToken ct) =>
            Task.FromResult(PendingCountOverride ?? Rows.Count(x =>
                x.AccountId == accountId && x.PriceBookImportId == importId && x.ValidationStatus == status));

        public Task<bool> ExternalKeyDuplicateInImportAsync(
            Guid accountId, Guid importId, string externalKey, Guid excludeRowId, CancellationToken ct) =>
            Task.FromResult(Rows.Any(x =>
                x.AccountId == accountId
                && x.PriceBookImportId == importId
                && x.Id != excludeRowId
                && x.ProposedExternalKey == externalKey));
    }

    sealed class FakeCatalogItemPersistence : ICatalogItemPersistence
    {
        public List<CatalogItem> Items { get; } = [];

        public Task<CatalogItem?> GetByIdAsync(Guid accountId, Guid catalogItemId, CancellationToken ct) =>
            Task.FromResult(Items.FirstOrDefault(x => x.AccountId == accountId && x.Id == catalogItemId));

        public Task<bool> ExternalKeyExistsAsync(Guid accountId, string externalKey, CancellationToken ct) =>
            Task.FromResult(Items.Any(x => x.AccountId == accountId && x.ExternalKey == externalKey));

        public Task<CatalogItemCommitResult> AddAsync(CatalogItem item, CancellationToken ct)
        {
            Items.Add(item);
            return Task.FromResult(CatalogItemCommitResult.Committed);
        }

        public Task<CatalogItemCommitResult> CommitAsync(CatalogItem item, CancellationToken ct) =>
            Task.FromResult(CatalogItemCommitResult.Committed);
    }
}
