using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the pure line/visit financial projection (Batch 7, build-log/129): completeness is decided
/// by the two required snapshot fields, never by <see cref="ActualWorkLine.PriceBookVersionLineId"/>
/// alone (corrected 2026-08-21), and an incomplete visit never returns a partial or fabricated
/// total.
/// </summary>
public class ActualWorkFinancialProjectionTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid RequestId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();
    static readonly Guid PriceBookVersionLineId = Guid.CreateVersion7();

    static Result<ActualWorkLine> AddCompleteCatalogLine(ActualWork work, decimal quantity, decimal sellPrice, decimal cost) =>
        work.AddLine(
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", quantity,
            sellPriceSnapshot: sellPrice, standardExpectedDirectCostSnapshot: cost,
            note: null, commercialBaselineSourceLineId: null, Actor);

    static Result<ActualWorkLine> AddCustomLine(ActualWork work, decimal quantity = 1m) =>
        work.AddLine(
            catalogItemId: null, priceBookVersionLineId: null, "3/4 inch copper elbow", null, quantity,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor);

    [Fact]
    public void All_complete_lines_compute_correct_totals_and_margin()
    {
        var work = ActualWork.Create(AccountId, RequestId, Actor).Value;
        AddCompleteCatalogLine(work, quantity: 2m, sellPrice: 10m, cost: 4m);
        AddCompleteCatalogLine(work, quantity: 1m, sellPrice: 50m, cost: 20m);

        var totals = ActualWorkFinancialProjection.ComputeVisitTotals(work.Lines);

        Assert.False(totals.HasIncompleteFinancialData);
        Assert.Equal(0, totals.IncompleteLineCount);
        Assert.Equal(70m, totals.TotalSalesPrice);
        Assert.Equal(28m, totals.TotalStandardExpectedDirectCost);
        Assert.Equal(42m, totals.TotalMargin);

        var lineEntries = work.Lines.Select(ActualWorkFinancialProjection.ToLineEntry).ToArray();
        Assert.Equal(20m, lineEntries[0].LineSalesTotal);
        Assert.Equal(8m, lineEntries[0].LineStandardExpectedDirectCostTotal);
        Assert.Equal(12m, lineEntries[0].LineMargin);
        Assert.True(lineEntries[0].IsFinancialDataComplete);
    }

    [Fact]
    public void Null_sell_price_snapshot_makes_line_and_visit_incomplete_with_null_totals()
    {
        var work = ActualWork.Create(AccountId, RequestId, Actor).Value;
        // A catalog line whose price-book snapshot did not carry a sell price, but did carry a cost —
        // constructible because ActualWorkLine.Create only requires both null when PriceBookVersionLineId
        // is null, not that both be set together when it IS set.
        var addResult = work.AddLine(
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", 1m,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: 18m,
            note: null, commercialBaselineSourceLineId: null, Actor);
        Assert.True(addResult.IsSuccess);

        var totals = ActualWorkFinancialProjection.ComputeVisitTotals(work.Lines);

        Assert.True(totals.HasIncompleteFinancialData);
        Assert.Equal(1, totals.IncompleteLineCount);
        Assert.Null(totals.TotalSalesPrice);
        Assert.Null(totals.TotalStandardExpectedDirectCost);
        Assert.Null(totals.TotalMargin);

        var lineEntry = ActualWorkFinancialProjection.ToLineEntry(work.Lines.Single());
        Assert.False(lineEntry.IsFinancialDataComplete);
        Assert.Null(lineEntry.LineSalesTotal);
        Assert.Null(lineEntry.LineStandardExpectedDirectCostTotal);
        Assert.Null(lineEntry.LineMargin);
    }

    [Fact]
    public void Null_cost_snapshot_makes_line_and_visit_incomplete()
    {
        var work = ActualWork.Create(AccountId, RequestId, Actor).Value;
        var addResult = work.AddLine(
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", 1m,
            sellPriceSnapshot: 42.50m, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor);
        Assert.True(addResult.IsSuccess);

        var totals = ActualWorkFinancialProjection.ComputeVisitTotals(work.Lines);

        Assert.True(totals.HasIncompleteFinancialData);
        Assert.Equal(1, totals.IncompleteLineCount);
        Assert.Null(totals.TotalSalesPrice);
    }

    [Fact]
    public void Zero_line_submitted_visit_is_complete_with_zero_totals()
    {
        var work = ActualWork.Create(AccountId, RequestId, Actor).Value;

        var totals = ActualWorkFinancialProjection.ComputeVisitTotals(work.Lines);

        Assert.False(totals.HasIncompleteFinancialData);
        Assert.Equal(0, totals.IncompleteLineCount);
        Assert.Equal(0.00m, totals.TotalSalesPrice);
        Assert.Equal(0.00m, totals.TotalStandardExpectedDirectCost);
        Assert.Equal(0.00m, totals.TotalMargin);
    }

    [Fact]
    public void Custom_off_catalog_line_is_incomplete()
    {
        var work = ActualWork.Create(AccountId, RequestId, Actor).Value;
        var addResult = AddCustomLine(work);
        Assert.True(addResult.IsSuccess);

        var lineEntry = ActualWorkFinancialProjection.ToLineEntry(work.Lines.Single());

        Assert.False(lineEntry.IsFinancialDataComplete);
        Assert.Null(lineEntry.SellPriceSnapshot);
        Assert.Null(lineEntry.StandardExpectedDirectCostSnapshot);
    }

    [Fact]
    public void Catalog_item_without_price_book_snapshot_is_incomplete()
    {
        var work = ActualWork.Create(AccountId, RequestId, Actor).Value;
        // State 2: CatalogItemId set, PriceBookVersionLineId null — the item currently carries no
        // price-book entry.
        var addResult = work.AddLine(
            CatalogItemId, priceBookVersionLineId: null, "Drain Pan", "each", 1m,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor);
        Assert.True(addResult.IsSuccess);

        var lineEntry = ActualWorkFinancialProjection.ToLineEntry(work.Lines.Single());

        Assert.False(lineEntry.IsFinancialDataComplete);
    }
}
