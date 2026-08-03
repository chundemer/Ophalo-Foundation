using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks <see cref="PriceBookVersion"/> and its owned <see cref="PriceBookVersionLine"/>
/// (Build 107/ADR-458, ADR-467, ADR-470, build-log/108, build-log/111): publish-time creation of
/// the version and its single line snapshot, line validation, and the Published-&gt;Superseded
/// transition.
/// </summary>
public class PriceBookVersionTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly DateTime PublishedAtUtc = DateTime.UtcNow;

    static Result<PriceBookVersion> Version(
        int versionNumber = 1,
        string displayName = "1/2 HP Sump Pump",
        CatalogItemType type = CatalogItemType.Material,
        string unitOfMeasure = "each",
        string currency = "USD",
        decimal? cost = 60m,
        decimal? sellPrice = 120m) =>
        PriceBookVersion.CreatePublished(
            AccountId, versionNumber, Actor, PublishedAtUtc, CatalogItemId,
            displayName, type, unitOfMeasure, currency, cost, sellPrice);

    // --- CreatePublished: version ---

    [Fact]
    public void CreatePublished_with_valid_fields_succeeds_as_Published_with_one_line()
    {
        var result = Version();

        Assert.True(result.IsSuccess);
        var version = result.Value;
        Assert.Equal(AccountId, version.AccountId);
        Assert.Equal(1, version.VersionNumber);
        Assert.Null(version.SourceImportId);
        Assert.Equal(PublishedAtUtc, version.PublishedAtUtc);
        Assert.Equal(Actor, version.PublishedByAccountUserId);
        Assert.Equal(PriceBookVersionStatus.Published, version.Status);

        var line = Assert.Single(version.Lines);
        Assert.Equal(AccountId, line.AccountId);
        Assert.Equal(version.Id, line.PriceBookVersionId);
        Assert.Equal(CatalogItemId, line.CatalogItemId);
        Assert.Equal("1/2 HP Sump Pump", line.DisplayNameSnapshot);
        Assert.Equal(CatalogItemType.Material, line.TypeSnapshot);
        Assert.Equal("each", line.UnitOfMeasureSnapshot);
        Assert.Equal("USD", line.CurrencySnapshot);
        Assert.Equal(60m, line.CostSnapshot);
        Assert.Equal(120m, line.SellPriceSnapshot);
    }

    [Fact]
    public void CreatePublished_trims_display_name_and_unit_of_measure_and_uppercases_currency()
    {
        var result = PriceBookVersion.CreatePublished(
            AccountId, 1, Actor, PublishedAtUtc, CatalogItemId,
            " Sump Pump ", CatalogItemType.Material, " each ", "usd", 60m, 120m);

        Assert.True(result.IsSuccess);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal("Sump Pump", line.DisplayNameSnapshot);
        Assert.Equal("each", line.UnitOfMeasureSnapshot);
        Assert.Equal("USD", line.CurrencySnapshot);
    }

    [Fact]
    public void CreatePublished_allows_null_cost_and_sell_price_for_a_package_only_item()
    {
        var result = Version(cost: null, sellPrice: null);

        Assert.True(result.IsSuccess);
        var line = Assert.Single(result.Value.Lines);
        Assert.Null(line.CostSnapshot);
        Assert.Null(line.SellPriceSnapshot);
    }

    [Fact]
    public void CreatePublished_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PriceBookVersion.CreatePublished(
                Guid.Empty, 1, Actor, PublishedAtUtc, CatalogItemId, "x", CatalogItemType.Material, "each", "USD", null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreatePublished_with_non_positive_version_number_throws(int versionNumber)
    {
        Assert.Throws<ArgumentException>(() => Version(versionNumber: versionNumber));
    }

    [Fact]
    public void CreatePublished_with_empty_published_by_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PriceBookVersion.CreatePublished(
                AccountId, 1, Guid.Empty, PublishedAtUtc, CatalogItemId, "x", CatalogItemType.Material, "each", "USD", null, null));
    }

    // --- CreatePublished: line validation propagation ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePublished_with_blank_display_name_fails(string displayName)
    {
        var result = Version(displayName: displayName);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.DisplayNameSnapshotRequired, result.Error);
    }

    [Fact]
    public void CreatePublished_with_display_name_over_200_chars_fails()
    {
        var result = Version(displayName: new string('x', 201));

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.DisplayNameSnapshotTooLong, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePublished_with_blank_unit_of_measure_fails(string unitOfMeasure)
    {
        var result = Version(unitOfMeasure: unitOfMeasure);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.UnitOfMeasureSnapshotRequired, result.Error);
    }

    [Fact]
    public void CreatePublished_with_unit_of_measure_over_50_chars_fails()
    {
        var result = Version(unitOfMeasure: new string('x', 51));

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.UnitOfMeasureSnapshotTooLong, result.Error);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("12D")]
    public void CreatePublished_with_invalid_currency_fails(string currency)
    {
        var result = Version(currency: currency);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.InvalidCurrencySnapshot, result.Error);
    }

    [Fact]
    public void CreatePublished_with_negative_cost_fails()
    {
        var result = Version(cost: -0.01m);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.CostSnapshotMustNotBeNegative, result.Error);
    }

    [Fact]
    public void CreatePublished_with_negative_sell_price_fails()
    {
        var result = Version(sellPrice: -0.01m);

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.SellPriceSnapshotMustNotBeNegative, result.Error);
    }

    // --- Supersede ---

    [Fact]
    public void Supersede_from_Published_succeeds()
    {
        var version = Version().Value;

        var result = version.Supersede();

        Assert.True(result.IsSuccess);
        Assert.Equal(PriceBookVersionStatus.Superseded, version.Status);
    }

    [Fact]
    public void Supersede_when_already_Superseded_fails()
    {
        var version = Version().Value;
        version.Supersede();

        var result = version.Supersede();

        Assert.True(result.IsFailure);
        Assert.Equal(PriceBookVersionErrors.AlreadySuperseded, result.Error);
    }
}
