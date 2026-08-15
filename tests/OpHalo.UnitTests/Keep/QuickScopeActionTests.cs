using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks <see cref="QuickScopeAction.Create"/>'s in-domain invariants (build-log/119): exactly one
/// of the two polymorphic targets, and Order within [1, 6]. Set-level invariants (max six slots,
/// distinct order, distinct target across an account's rows) are
/// <see cref="QuickScopeActionSetValidatorTests"/>, not here — a single row cannot know about its
/// siblings.
/// </summary>
public class QuickScopeActionTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();
    static readonly Guid OfferingAssemblyId = Guid.CreateVersion7();

    [Fact]
    public void Create_with_only_a_catalog_item_target_succeeds()
    {
        var result = QuickScopeAction.Create(AccountId, 1, CatalogItemId, null, Actor);

        Assert.True(result.IsSuccess);
        var action = result.Value;
        Assert.Equal(AccountId, action.AccountId);
        Assert.Equal(1, action.Order);
        Assert.Equal(CatalogItemId, action.CatalogItemId);
        Assert.Null(action.OfferingAssemblyId);
        Assert.Equal(Actor, action.CreatedByUserId);
    }

    [Fact]
    public void Create_with_only_an_offering_assembly_target_succeeds()
    {
        var result = QuickScopeAction.Create(AccountId, 6, null, OfferingAssemblyId, Actor);

        Assert.True(result.IsSuccess);
        var action = result.Value;
        Assert.Equal(6, action.Order);
        Assert.Null(action.CatalogItemId);
        Assert.Equal(OfferingAssemblyId, action.OfferingAssemblyId);
    }

    [Fact]
    public void Create_with_neither_target_fails()
    {
        var result = QuickScopeAction.Create(AccountId, 1, null, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(QuickScopeActionErrors.TargetRequired, result.Error);
    }

    [Fact]
    public void Create_with_both_targets_fails()
    {
        var result = QuickScopeAction.Create(AccountId, 1, CatalogItemId, OfferingAssemblyId, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(QuickScopeActionErrors.TargetMustBeExclusive, result.Error);
    }

    [Fact]
    public void Create_with_an_empty_guid_target_is_treated_as_absent()
    {
        var result = QuickScopeAction.Create(AccountId, 1, Guid.Empty, OfferingAssemblyId, Actor);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CatalogItemId);
        Assert.Equal(OfferingAssemblyId, result.Value.OfferingAssemblyId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void Create_with_order_out_of_range_fails(int order)
    {
        var result = QuickScopeAction.Create(AccountId, order, CatalogItemId, null, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(QuickScopeActionErrors.OrderOutOfRange, result.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Create_with_order_at_the_boundary_succeeds(int order)
    {
        var result = QuickScopeAction.Create(AccountId, order, CatalogItemId, null, Actor);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => QuickScopeAction.Create(Guid.Empty, 1, CatalogItemId, null, Actor));
    }

    [Fact]
    public void Create_with_empty_created_by_user_id_throws()
    {
        Assert.Throws<ArgumentException>(() => QuickScopeAction.Create(AccountId, 1, CatalogItemId, null, Guid.Empty));
    }
}

/// <summary>Locks <see cref="QuickScopeActionSetValidator"/>'s cross-row invariants
/// (build-log/119): at most six slots, distinct order, and distinct target per account.</summary>
public class QuickScopeActionSetValidatorTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();

    static QuickScopeAction CatalogSlot(int order, Guid? catalogItemId = null) =>
        QuickScopeAction.Create(AccountId, order, catalogItemId ?? Guid.CreateVersion7(), null, Actor).Value;

    static QuickScopeAction AssemblySlot(int order, Guid? offeringAssemblyId = null) =>
        QuickScopeAction.Create(AccountId, order, null, offeringAssemblyId ?? Guid.CreateVersion7(), Actor).Value;

    [Fact]
    public void Empty_set_is_valid()
    {
        var result = QuickScopeActionSetValidator.Validate([]);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Six_distinct_slots_are_valid()
    {
        var set = Enumerable.Range(1, 6).Select(order => CatalogSlot(order)).ToList();

        var result = QuickScopeActionSetValidator.Validate(set);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void More_than_six_slots_fails()
    {
        var set = Enumerable.Range(1, 6).Select(order => CatalogSlot(order)).ToList();
        set.Add(QuickScopeAction.Create(AccountId, 6, Guid.CreateVersion7(), null, Actor).Value);

        var result = QuickScopeActionSetValidator.Validate(set);

        Assert.True(result.IsFailure);
        Assert.Equal(QuickScopeActionErrors.TooManySlots, result.Error);
    }

    [Fact]
    public void Duplicate_order_fails()
    {
        var set = new List<QuickScopeAction> { CatalogSlot(1), CatalogSlot(1) };

        var result = QuickScopeActionSetValidator.Validate(set);

        Assert.True(result.IsFailure);
        Assert.Equal(QuickScopeActionErrors.DuplicateOrder, result.Error);
    }

    [Fact]
    public void Duplicate_catalog_item_target_fails()
    {
        var sharedCatalogItemId = Guid.CreateVersion7();
        var set = new List<QuickScopeAction> { CatalogSlot(1, sharedCatalogItemId), CatalogSlot(2, sharedCatalogItemId) };

        var result = QuickScopeActionSetValidator.Validate(set);

        Assert.True(result.IsFailure);
        Assert.Equal(QuickScopeActionErrors.DuplicateTarget, result.Error);
    }

    [Fact]
    public void Duplicate_offering_assembly_target_fails()
    {
        var sharedAssemblyId = Guid.CreateVersion7();
        var set = new List<QuickScopeAction> { AssemblySlot(1, sharedAssemblyId), AssemblySlot(2, sharedAssemblyId) };

        var result = QuickScopeActionSetValidator.Validate(set);

        Assert.True(result.IsFailure);
        Assert.Equal(QuickScopeActionErrors.DuplicateTarget, result.Error);
    }

    [Fact]
    public void A_catalog_item_slot_and_an_assembly_slot_with_the_same_underlying_id_do_not_collide()
    {
        var sharedId = Guid.CreateVersion7();
        var set = new List<QuickScopeAction> { CatalogSlot(1, sharedId), AssemblySlot(2, sharedId) };

        var result = QuickScopeActionSetValidator.Validate(set);

        Assert.True(result.IsSuccess);
    }
}
