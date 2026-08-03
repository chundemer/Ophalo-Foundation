using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks <see cref="ManualPriceOverride"/> (Build 107/ADR-458, build-log/108, build-log/111):
/// Create validation and the always-<c>CatalogItem</c> target shape for this batch.
/// </summary>
public class ManualPriceOverrideTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly DateTime OccurredAtUtc = DateTime.UtcNow;

    static Result<ManualPriceOverride> Override(
        string reason = "Vendor price increase",
        decimal? oldSellPrice = 100m,
        decimal? newSellPrice = 120m,
        decimal? oldCost = 60m,
        decimal? newCost = 70m) =>
        ManualPriceOverride.Create(
            AccountId, CatalogItemId, Actor, OccurredAtUtc, reason, oldSellPrice, newSellPrice, oldCost, newCost);

    // --- Create ---

    [Fact]
    public void Create_with_valid_fields_succeeds_with_CatalogItem_target()
    {
        var result = Override(reason: " Vendor price increase ");

        Assert.True(result.IsSuccess);
        var entry = result.Value;
        Assert.Equal(AccountId, entry.AccountId);
        Assert.Equal(ManualPriceOverrideTargetType.CatalogItem, entry.TargetType);
        Assert.Equal(CatalogItemId, entry.CatalogItemId);
        Assert.Equal(Actor, entry.ActorAccountUserId);
        Assert.Equal(OccurredAtUtc, entry.OccurredAtUtc);
        Assert.Equal("Vendor price increase", entry.Reason);
        Assert.Equal(100m, entry.OldSellPrice);
        Assert.Equal(120m, entry.NewSellPrice);
        Assert.Equal(60m, entry.OldCost);
        Assert.Equal(70m, entry.NewCost);
    }

    [Fact]
    public void Create_allows_null_old_values_for_a_first_time_price()
    {
        var result = Override(oldSellPrice: null, oldCost: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.OldSellPrice);
        Assert.Null(result.Value.OldCost);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_reason_fails(string reason)
    {
        var result = Override(reason: reason);

        Assert.True(result.IsFailure);
        Assert.Equal(ManualPriceOverrideErrors.ReasonRequired, result.Error);
    }

    [Fact]
    public void Create_with_reason_over_500_chars_fails()
    {
        var result = Override(reason: new string('x', 501));

        Assert.True(result.IsFailure);
        Assert.Equal(ManualPriceOverrideErrors.ReasonTooLong, result.Error);
    }

    [Fact]
    public void Create_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ManualPriceOverride.Create(Guid.Empty, CatalogItemId, Actor, OccurredAtUtc, "x", null, null, null, null));
    }

    [Fact]
    public void Create_with_empty_catalog_item_id_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ManualPriceOverride.Create(AccountId, Guid.Empty, Actor, OccurredAtUtc, "x", null, null, null, null));
    }

    [Fact]
    public void Create_with_empty_actor_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ManualPriceOverride.Create(AccountId, CatalogItemId, Guid.Empty, OccurredAtUtc, "x", null, null, null, null));
    }
}
