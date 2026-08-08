using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the CatalogItem lifecycle (build-log/108, Session 2a.1): CreateDraft validation,
/// Draft/Inactive -&gt; Active -&gt; Inactive transitions, and concurrency-token rotation.
/// </summary>
public class CatalogItemTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();

    static Result<CatalogItem> Draft(
        string displayName = "Water Heater Install",
        string unitOfMeasure = "each",
        string currency = "USD",
        string? externalKey = null,
        Guid? categoryId = null,
        bool isCommonItem = false) =>
        CatalogItem.CreateDraft(
            AccountId, CatalogItemType.Service, displayName, unitOfMeasure, currency,
            externalKey, categoryId, isCommonItem, Actor);

    // --- CreateDraft ---

    [Fact]
    public void CreateDraft_with_valid_fields_succeeds_as_Draft()
    {
        var result = Draft(externalKey: " SKU-1 ", isCommonItem: true);

        Assert.True(result.IsSuccess);
        var item = result.Value;
        Assert.Equal(AccountId, item.AccountId);
        Assert.Equal(CatalogItemType.Service, item.Type);
        Assert.Equal("Water Heater Install", item.DisplayName);
        Assert.Equal("each", item.UnitOfMeasure);
        Assert.Equal("USD", item.Currency);
        Assert.Equal("SKU-1", item.ExternalKey);
        Assert.True(item.IsCommonItem);
        Assert.Equal(CatalogItemActiveState.Draft, item.ActiveState);
        Assert.Equal(Actor, item.CreatedByUserId);
        Assert.NotEqual(Guid.Empty, item.ConcurrencyVersion);
    }

    [Fact]
    public void CreateDraft_with_no_external_key_leaves_it_null()
    {
        var result = Draft();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ExternalKey);
        Assert.Null(result.Value.NormalizedExternalKey);
    }

    [Theory]
    [InlineData("cop34")]
    [InlineData("COP-34")]
    [InlineData("cop 34")]
    [InlineData(" COP_34 ")]
    public void CreateDraft_normalizes_equivalent_external_keys_to_the_same_canonical_form(string externalKey)
    {
        var result = Draft(externalKey: externalKey);

        Assert.True(result.IsSuccess);
        Assert.Equal("cop34", result.Value.NormalizedExternalKey);
    }

    [Fact]
    public void CreateDraft_preserves_the_raw_external_key_for_display()
    {
        var result = Draft(externalKey: " COP-34 ");

        Assert.True(result.IsSuccess);
        Assert.Equal("COP-34", result.Value.ExternalKey);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("___")]
    [InlineData("!!!")]
    public void CreateDraft_with_external_key_that_normalizes_to_empty_fails(string externalKey)
    {
        var result = Draft(externalKey: externalKey);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.InvalidExternalKey, result.Error);
    }

    [Fact]
    public void CreateDraft_lowercases_currency_to_upper_invariant()
    {
        var result = Draft(currency: "usd");

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDraft_with_blank_display_name_fails(string displayName)
    {
        var result = Draft(displayName: displayName);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.DisplayNameRequired, result.Error);
    }

    [Fact]
    public void CreateDraft_with_display_name_over_200_chars_fails()
    {
        var result = Draft(displayName: new string('x', 201));

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.DisplayNameTooLong, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDraft_with_blank_unit_of_measure_fails(string unitOfMeasure)
    {
        var result = Draft(unitOfMeasure: unitOfMeasure);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.UnitOfMeasureRequired, result.Error);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("12D")]
    [InlineData("")]
    public void CreateDraft_with_invalid_currency_fails(string currency)
    {
        var result = Draft(currency: currency);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.InvalidCurrency, result.Error);
    }

    [Fact]
    public void CreateDraft_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => CatalogItem.CreateDraft(
            Guid.Empty, CatalogItemType.Service, "x", "each", "USD", null, null, false, Actor));
    }

    [Fact]
    public void CreateDraft_with_empty_actor_throws()
    {
        Assert.Throws<ArgumentException>(() => CatalogItem.CreateDraft(
            AccountId, CatalogItemType.Service, "x", "each", "USD", null, null, false, Guid.Empty));
    }

    // --- UpdateHeader ---

    [Fact]
    public void UpdateHeader_with_valid_fields_updates_and_rotates_concurrency_version()
    {
        var item = Draft(externalKey: "SKU-1").Value;
        var originalVersion = item.ConcurrencyVersion;
        var categoryId = Guid.CreateVersion7();

        var result = item.UpdateHeader("New Name", " SKU-2 ", categoryId, true);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", item.DisplayName);
        Assert.Equal("SKU-2", item.ExternalKey);
        Assert.Equal(categoryId, item.CategoryId);
        Assert.True(item.IsCommonItem);
        Assert.NotEqual(originalVersion, item.ConcurrencyVersion);
    }

    [Fact]
    public void UpdateHeader_does_not_change_Type_UnitOfMeasure_or_Currency()
    {
        var item = Draft().Value;

        item.UpdateHeader("New Name", null, null, false);

        Assert.Equal(CatalogItemType.Service, item.Type);
        Assert.Equal("each", item.UnitOfMeasure);
        Assert.Equal("USD", item.Currency);
    }

    [Fact]
    public void UpdateHeader_clearing_the_external_key_succeeds()
    {
        var item = Draft(externalKey: "SKU-1").Value;

        var result = item.UpdateHeader("Water Heater Install", null, null, false);

        Assert.True(result.IsSuccess);
        Assert.Null(item.ExternalKey);
        Assert.Null(item.NormalizedExternalKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateHeader_with_blank_display_name_fails(string displayName)
    {
        var item = Draft().Value;

        var result = item.UpdateHeader(displayName, null, null, false);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.DisplayNameRequired, result.Error);
    }

    [Fact]
    public void UpdateHeader_with_display_name_over_200_chars_fails()
    {
        var item = Draft().Value;

        var result = item.UpdateHeader(new string('a', 201), null, null, false);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.DisplayNameTooLong, result.Error);
    }

    [Fact]
    public void UpdateHeader_with_external_key_that_normalizes_to_empty_fails()
    {
        var item = Draft().Value;

        var result = item.UpdateHeader("Water Heater Install", "---", null, false);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.InvalidExternalKey, result.Error);
    }

    // --- Activate / Inactivate ---

    [Fact]
    public void Activate_from_Draft_succeeds_and_rotates_concurrency_version()
    {
        var item = Draft().Value;
        var before = item.ConcurrencyVersion;

        var result = item.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogItemActiveState.Active, item.ActiveState);
        Assert.NotEqual(before, item.ConcurrencyVersion);
    }

    [Fact]
    public void Activate_when_already_Active_fails()
    {
        var item = Draft().Value;
        item.Activate();

        var result = item.Activate();

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AlreadyActive, result.Error);
    }

    [Fact]
    public void Activate_from_Inactive_succeeds()
    {
        var item = Draft().Value;
        item.Activate();
        item.Inactivate();

        var result = item.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogItemActiveState.Active, item.ActiveState);
    }

    [Fact]
    public void Inactivate_from_Active_succeeds_and_rotates_concurrency_version()
    {
        var item = Draft().Value;
        item.Activate();
        var before = item.ConcurrencyVersion;

        var result = item.Inactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogItemActiveState.Inactive, item.ActiveState);
        Assert.NotEqual(before, item.ConcurrencyVersion);
    }

    [Fact]
    public void Inactivate_from_Draft_fails_NotActive()
    {
        var item = Draft().Value;

        var result = item.Inactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotActive, result.Error);
    }

    [Fact]
    public void Inactivate_when_already_Inactive_fails_NotActive()
    {
        var item = Draft().Value;
        item.Activate();
        item.Inactivate();

        var result = item.Inactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotActive, result.Error);
    }

    // --- Aliases (Session 2b.2) ---

    [Fact]
    public void AddAlias_with_valid_text_succeeds_as_Active_and_rotates_concurrency_version()
    {
        var item = Draft().Value;
        var before = item.ConcurrencyVersion;

        var result = item.AddAlias(" Hot Water Tank ", Actor);

        Assert.True(result.IsSuccess);
        var alias = result.Value;
        Assert.Equal("Hot Water Tank", alias.AliasText);
        Assert.Equal("hot water tank", alias.NormalizedAliasText);
        Assert.Equal(CatalogActiveState.Active, alias.ActiveState);
        Assert.Equal(item.AccountId, alias.AccountId);
        Assert.Equal(item.Id, alias.CatalogItemId);
        Assert.Contains(alias, item.Aliases);
        Assert.NotEqual(before, item.ConcurrencyVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddAlias_with_blank_text_fails(string aliasText)
    {
        var item = Draft().Value;

        var result = item.AddAlias(aliasText, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasTextRequired, result.Error);
    }

    [Fact]
    public void AddAlias_with_empty_actor_throws()
    {
        var item = Draft().Value;

        Assert.Throws<ArgumentException>(() => item.AddAlias("Hot Water Tank", Guid.Empty));
    }

    [Fact]
    public void AddAlias_with_text_over_200_chars_fails()
    {
        var item = Draft().Value;

        var result = item.AddAlias(new string('x', 201), Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasTextTooLong, result.Error);
    }

    [Fact]
    public void AddAlias_with_duplicate_text_case_insensitive_fails()
    {
        var item = Draft().Value;
        item.AddAlias("Hot Water Tank", Actor);

        var result = item.AddAlias(" hot water tank ", Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasAlreadyExists, result.Error);
    }

    [Fact]
    public void InactivateAlias_from_Active_succeeds_and_rotates_item_concurrency_version()
    {
        var item = Draft().Value;
        var alias = item.AddAlias("Hot Water Tank", Actor).Value;
        var before = item.ConcurrencyVersion;

        var result = item.InactivateAlias(alias.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Inactive, alias.ActiveState);
        Assert.NotEqual(before, item.ConcurrencyVersion);
    }

    [Fact]
    public void InactivateAlias_when_already_Inactive_fails()
    {
        var item = Draft().Value;
        var alias = item.AddAlias("Hot Water Tank", Actor).Value;
        item.InactivateAlias(alias.Id);

        var result = item.InactivateAlias(alias.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasNotActive, result.Error);
    }

    [Fact]
    public void ActivateAlias_from_Inactive_succeeds_and_rotates_item_concurrency_version()
    {
        var item = Draft().Value;
        var alias = item.AddAlias("Hot Water Tank", Actor).Value;
        item.InactivateAlias(alias.Id);
        var before = item.ConcurrencyVersion;

        var result = item.ActivateAlias(alias.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Active, alias.ActiveState);
        Assert.NotEqual(before, item.ConcurrencyVersion);
    }

    [Fact]
    public void ActivateAlias_when_already_Active_fails()
    {
        var item = Draft().Value;
        var alias = item.AddAlias("Hot Water Tank", Actor).Value;

        var result = item.ActivateAlias(alias.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasAlreadyActive, result.Error);
    }

    [Fact]
    public void ActivateAlias_with_unknown_id_fails_AliasNotFound()
    {
        var item = Draft().Value;

        var result = item.ActivateAlias(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasNotFound, result.Error);
    }

    // --- ApplyPublishedPrice ---

    [Fact]
    public void ApplyPublishedPrice_sets_pointer_and_leaves_concurrency_version_unchanged()
    {
        var item = Draft().Value;
        var before = item.ConcurrencyVersion;
        var lineId = Guid.CreateVersion7();

        item.ApplyPublishedPrice(lineId);

        Assert.Equal(lineId, item.CurrentPriceBookVersionLineId);
        Assert.Equal(before, item.ConcurrencyVersion);
    }

    [Fact]
    public void ApplyPublishedPrice_with_empty_id_throws()
    {
        var item = Draft().Value;

        Assert.Throws<ArgumentException>(() => item.ApplyPublishedPrice(Guid.Empty));
    }
}
