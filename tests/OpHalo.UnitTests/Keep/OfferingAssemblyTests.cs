using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the OfferingAssembly lifecycle (ADR-457, ADR-479, build-log/108, Session 3.1): Create,
/// live header/item edits while Active, Activate/Inactivate as a pure Owner/Admin toggle
/// independent of item validity, and concurrency-token rotation. Cross-aggregate operational
/// eligibility (ADR-479) is a persistence-layer read, not domain behavior, and is not covered here.
/// </summary>
public class OfferingAssemblyTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly Guid PrimaryCatalogItemId = Guid.CreateVersion7();

    static Result<OfferingAssembly> New(
        string name = "Control Board Replacement",
        PriceTreatment priceTreatment = PriceTreatment.Summed,
        Guid? primaryCatalogItemId = null) =>
        OfferingAssembly.Create(
            AccountId, primaryCatalogItemId ?? PrimaryCatalogItemId, name, priceTreatment, Actor);

    // --- Create ---

    [Fact]
    public void Create_with_valid_fields_succeeds_as_Active()
    {
        var result = New();

        Assert.True(result.IsSuccess);
        var assembly = result.Value;
        Assert.Equal(AccountId, assembly.AccountId);
        Assert.Equal(PrimaryCatalogItemId, assembly.PrimaryCatalogItemId);
        Assert.Equal("Control Board Replacement", assembly.Name);
        Assert.Equal(PriceTreatment.Summed, assembly.PriceTreatment);
        Assert.Equal(CatalogActiveState.Active, assembly.ActiveState);
        Assert.Equal(Actor, assembly.CreatedByUserId);
        Assert.Empty(assembly.Items);
        Assert.NotEqual(Guid.Empty, assembly.ConcurrencyVersion);
    }

    [Fact]
    public void Create_trims_the_name()
    {
        var result = New(name: "  Control Board Replacement  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Control Board Replacement", result.Value.Name);
    }

    [Fact]
    public void Create_with_blank_name_fails()
    {
        var result = New(name: "   ");

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Create_with_name_over_200_characters_fails()
    {
        var result = New(name: new string('a', 201));

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.NameTooLong, result.Error);
    }

    [Fact]
    public void Create_with_empty_primary_catalog_item_fails()
    {
        var result = New(primaryCatalogItemId: Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.PrimaryCatalogItemRequired, result.Error);
    }

    // --- Activate / Inactivate ---

    [Fact]
    public void Inactivate_an_Active_assembly_succeeds()
    {
        var assembly = New().Value;
        var priorVersion = assembly.ConcurrencyVersion;

        var result = assembly.Inactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Inactive, assembly.ActiveState);
        Assert.NotEqual(priorVersion, assembly.ConcurrencyVersion);
    }

    [Fact]
    public void Inactivate_an_already_Inactive_assembly_fails()
    {
        var assembly = New().Value;
        assembly.Inactivate();

        var result = assembly.Inactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.NotActive, result.Error);
    }

    [Fact]
    public void Activate_an_Inactive_assembly_succeeds()
    {
        var assembly = New().Value;
        assembly.Inactivate();
        var priorVersion = assembly.ConcurrencyVersion;

        var result = assembly.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Active, assembly.ActiveState);
        Assert.NotEqual(priorVersion, assembly.ConcurrencyVersion);
    }

    [Fact]
    public void Activate_an_already_Active_assembly_fails()
    {
        var assembly = New().Value;

        var result = assembly.Activate();

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.AlreadyActive, result.Error);
    }

    // --- UpdateHeader (ADR-479: live edit while Active, no ceremony) ---

    [Fact]
    public void UpdateHeader_on_an_Active_assembly_succeeds_directly()
    {
        var assembly = New().Value;
        var newPrimary = Guid.CreateVersion7();

        var result = assembly.UpdateHeader(newPrimary, "Split System Replacement", PriceTreatment.AllInclusive);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Active, assembly.ActiveState);
        Assert.Equal(newPrimary, assembly.PrimaryCatalogItemId);
        Assert.Equal("Split System Replacement", assembly.Name);
        Assert.Equal(PriceTreatment.AllInclusive, assembly.PriceTreatment);
    }

    [Fact]
    public void UpdateHeader_with_blank_name_fails()
    {
        var assembly = New().Value;

        var result = assembly.UpdateHeader(PrimaryCatalogItemId, " ", PriceTreatment.Summed);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.NameRequired, result.Error);
    }

    [Fact]
    public void UpdateHeader_to_an_existing_associated_item_fails()
    {
        var assembly = New().Value;
        var associatedItemId = Guid.CreateVersion7();
        assembly.AddItem(associatedItemId, 1, isOptional: false, displayOrder: 0, Actor);

        var result = assembly.UpdateHeader(associatedItemId, "Renamed", PriceTreatment.Summed);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyAssociated, result.Error);
        Assert.Equal(PrimaryCatalogItemId, assembly.PrimaryCatalogItemId);
    }

    [Fact]
    public void UpdateHeader_to_a_former_associated_item_succeeds_after_it_is_removed()
    {
        var assembly = New().Value;
        var associatedItemId = Guid.CreateVersion7();
        var itemId = assembly.AddItem(associatedItemId, 1, isOptional: false, displayOrder: 0, Actor).Value.Id;
        assembly.RemoveItem(itemId);

        var result = assembly.UpdateHeader(associatedItemId, "Renamed", PriceTreatment.Summed);

        Assert.True(result.IsSuccess);
        Assert.Equal(associatedItemId, assembly.PrimaryCatalogItemId);
    }

    // --- AddItem / UpdateItem / RemoveItem ---

    [Fact]
    public void AddItem_with_valid_fields_succeeds()
    {
        var assembly = New().Value;
        var catalogItemId = Guid.CreateVersion7();

        var result = assembly.AddItem(catalogItemId, 1.5m, isOptional: false, displayOrder: 0, Actor);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(assembly.Items);
        Assert.Equal(catalogItemId, item.CatalogItemId);
        Assert.Equal(1.5m, item.DefaultQuantity);
        Assert.False(item.IsOptional);
        Assert.Equal(0, item.DisplayOrder);
    }

    [Fact]
    public void AddItem_matching_the_primary_catalog_item_fails()
    {
        var assembly = New().Value;

        var result = assembly.AddItem(PrimaryCatalogItemId, 1, isOptional: false, displayOrder: 0, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.ItemCannotBePrimary, result.Error);
    }

    [Fact]
    public void AddItem_with_a_duplicate_catalog_item_fails()
    {
        var assembly = New().Value;
        var catalogItemId = Guid.CreateVersion7();
        assembly.AddItem(catalogItemId, 1, isOptional: false, displayOrder: 0, Actor);

        var result = assembly.AddItem(catalogItemId, 2, isOptional: true, displayOrder: 1, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.ItemAlreadyExists, result.Error);
    }

    [Fact]
    public void AddItem_with_non_positive_quantity_fails()
    {
        var assembly = New().Value;

        var result = assembly.AddItem(Guid.CreateVersion7(), 0, isOptional: false, displayOrder: 0, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.ItemQuantityMustBePositive, result.Error);
    }

    [Fact]
    public void UpdateItem_on_an_existing_item_succeeds_directly_while_Active()
    {
        var assembly = New().Value;
        var catalogItemId = Guid.CreateVersion7();
        var itemId = assembly.AddItem(catalogItemId, 1, isOptional: false, displayOrder: 0, Actor).Value.Id;

        var result = assembly.UpdateItem(itemId, 3, isOptional: true, displayOrder: 2);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(assembly.Items);
        Assert.Equal(3, item.DefaultQuantity);
        Assert.True(item.IsOptional);
        Assert.Equal(2, item.DisplayOrder);
    }

    [Fact]
    public void UpdateItem_for_an_unknown_item_fails()
    {
        var assembly = New().Value;

        var result = assembly.UpdateItem(Guid.CreateVersion7(), 1, isOptional: false, displayOrder: 0);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public void RemoveItem_removes_it_directly_while_Active()
    {
        var assembly = New().Value;
        var itemId = assembly.AddItem(Guid.CreateVersion7(), 1, isOptional: false, displayOrder: 0, Actor).Value.Id;

        var result = assembly.RemoveItem(itemId);

        Assert.True(result.IsSuccess);
        Assert.Empty(assembly.Items);
    }

    [Fact]
    public void RemoveItem_for_an_unknown_item_fails()
    {
        var assembly = New().Value;

        var result = assembly.RemoveItem(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public void AddItem_rotates_the_concurrency_version()
    {
        var assembly = New().Value;
        var priorVersion = assembly.ConcurrencyVersion;

        assembly.AddItem(Guid.CreateVersion7(), 1, isOptional: false, displayOrder: 0, Actor);

        Assert.NotEqual(priorVersion, assembly.ConcurrencyVersion);
    }
}
