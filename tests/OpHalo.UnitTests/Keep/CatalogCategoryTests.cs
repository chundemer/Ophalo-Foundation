using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the CatalogCategory lifecycle (build-log/108, build-log/110, Session 2b.1): Create
/// validation, Active/Inactive transitions (no Draft state), NormalizedName computation, and
/// concurrency-token rotation.
/// </summary>
public class CatalogCategoryTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();

    static Result<CatalogCategory> Category(string name = "Water Heaters", int displayOrder = 0) =>
        CatalogCategory.Create(AccountId, name, displayOrder, Actor);

    // --- Create ---

    [Fact]
    public void Create_with_valid_fields_succeeds_as_Active()
    {
        var result = Category(name: " Water Heaters ", displayOrder: 3);

        Assert.True(result.IsSuccess);
        var category = result.Value;
        Assert.Equal(AccountId, category.AccountId);
        Assert.Equal("Water Heaters", category.Name);
        Assert.Equal("water heaters", category.NormalizedName);
        Assert.Equal(3, category.DisplayOrder);
        Assert.Equal(CatalogActiveState.Active, category.ActiveState);
        Assert.Equal(Actor, category.CreatedByUserId);
        Assert.NotEqual(Guid.Empty, category.ConcurrencyVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_name_fails(string name)
    {
        var result = Category(name: name);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NameRequired, result.Error);
    }

    [Fact]
    public void Create_with_name_over_100_chars_fails()
    {
        var result = Category(name: new string('x', 101));

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NameTooLong, result.Error);
    }

    [Fact]
    public void Create_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => CatalogCategory.Create(Guid.Empty, "x", 0, Actor));
    }

    [Fact]
    public void Create_with_empty_actor_throws()
    {
        Assert.Throws<ArgumentException>(() => CatalogCategory.Create(AccountId, "x", 0, Guid.Empty));
    }

    // --- Activate / Inactivate ---

    [Fact]
    public void Activate_when_already_Active_fails()
    {
        var category = Category().Value;

        var result = category.Activate();

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.AlreadyActive, result.Error);
    }

    [Fact]
    public void Inactivate_from_Active_succeeds_and_rotates_concurrency_version()
    {
        var category = Category().Value;
        var before = category.ConcurrencyVersion;

        var result = category.Inactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Inactive, category.ActiveState);
        Assert.NotEqual(before, category.ConcurrencyVersion);
    }

    [Fact]
    public void Activate_from_Inactive_succeeds_and_rotates_concurrency_version()
    {
        var category = Category().Value;
        category.Inactivate();
        var before = category.ConcurrencyVersion;

        var result = category.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Active, category.ActiveState);
        Assert.NotEqual(before, category.ConcurrencyVersion);
    }

    [Fact]
    public void Inactivate_when_already_Inactive_fails_NotActive()
    {
        var category = Category().Value;
        category.Inactivate();

        var result = category.Inactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NotActive, result.Error);
    }
}
