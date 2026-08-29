using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
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
    static readonly Guid WorkId = Guid.CreateVersion7();

    static readonly IReadOnlyList<ActualWorkLineFinancialResolution> NoResolutions =
        Array.Empty<ActualWorkLineFinancialResolution>();

    static readonly DateTime T0 = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    static ActualWorkLineFinancialResolution Resolution(
        Guid lineId, decimal? sell, decimal? cost, DateTime resolvedAt,
        FinancialResolutionBasis basis = FinancialResolutionBasis.OwnerSetPrice) =>
        ActualWorkLineFinancialResolution.Create(
            AccountId, WorkId, lineId, sell, cost, basis, "resolved for test", Actor, resolvedAt).Value;

    static Result<ActualWorkLine> AddCompleteCatalogLine(ActualWork work, decimal quantity, decimal sellPrice, decimal cost) =>
        ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", quantity,
            sellPriceSnapshot: sellPrice, standardExpectedDirectCostSnapshot: cost,
            note: null, commercialBaselineSourceLineId: null, Actor);

    static Result<ActualWorkLine> AddCustomLine(ActualWork work, decimal quantity = 1m) =>
        ActualWorkTestData.AddLine(
            work,
            catalogItemId: null, priceBookVersionLineId: null, "3/4 inch copper elbow", null, quantity,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor);

    [Fact]
    public void All_complete_lines_compute_correct_totals_and_margin()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        AddCompleteCatalogLine(work, quantity: 2m, sellPrice: 10m, cost: 4m);
        AddCompleteCatalogLine(work, quantity: 1m, sellPrice: 50m, cost: 20m);

        var totals = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Totals;

        Assert.False(totals.HasIncompleteFinancialData);
        Assert.Equal(0, totals.IncompleteLineCount);
        Assert.Equal(70m, totals.TotalSalesPrice);
        Assert.Equal(28m, totals.TotalStandardExpectedDirectCost);
        Assert.Equal(42m, totals.TotalMargin);

        var lineEntries = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Lines;
        Assert.Equal(20m, lineEntries[0].LineSalesTotal);
        Assert.Equal(8m, lineEntries[0].LineStandardExpectedDirectCostTotal);
        Assert.Equal(12m, lineEntries[0].LineMargin);
        Assert.True(lineEntries[0].IsFinancialDataComplete);
    }

    [Fact]
    public void Null_sell_price_snapshot_makes_line_and_visit_incomplete_with_null_totals()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        // A catalog line whose price-book snapshot did not carry a sell price, but did carry a cost —
        // constructible because ActualWorkLine.Create only requires both null when PriceBookVersionLineId
        // is null, not that both be set together when it IS set.
        var addResult = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", 1m,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: 18m,
            note: null, commercialBaselineSourceLineId: null, Actor);
        Assert.True(addResult.IsSuccess);

        var totals = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Totals;

        Assert.True(totals.HasIncompleteFinancialData);
        Assert.Equal(1, totals.IncompleteLineCount);
        Assert.Null(totals.TotalSalesPrice);
        Assert.Null(totals.TotalStandardExpectedDirectCost);
        Assert.Null(totals.TotalMargin);

        var lineEntry = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Lines.Single();
        Assert.False(lineEntry.IsFinancialDataComplete);
        Assert.Null(lineEntry.LineSalesTotal);
        Assert.Null(lineEntry.LineStandardExpectedDirectCostTotal);
        Assert.Null(lineEntry.LineMargin);
    }

    [Fact]
    public void Null_cost_snapshot_makes_line_and_visit_incomplete()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        var addResult = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", 1m,
            sellPriceSnapshot: 42.50m, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor);
        Assert.True(addResult.IsSuccess);

        var totals = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Totals;

        Assert.True(totals.HasIncompleteFinancialData);
        Assert.Equal(1, totals.IncompleteLineCount);
        Assert.Null(totals.TotalSalesPrice);
    }

    [Fact]
    public void Zero_line_submitted_visit_is_complete_with_zero_totals()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;

        var totals = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Totals;

        Assert.False(totals.HasIncompleteFinancialData);
        Assert.Equal(0, totals.IncompleteLineCount);
        Assert.Equal(0.00m, totals.TotalSalesPrice);
        Assert.Equal(0.00m, totals.TotalStandardExpectedDirectCost);
        Assert.Equal(0.00m, totals.TotalMargin);
    }

    [Fact]
    public void Custom_off_catalog_line_is_incomplete()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        var addResult = AddCustomLine(work);
        Assert.True(addResult.IsSuccess);

        var lineEntry = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Lines.Single();

        Assert.False(lineEntry.IsFinancialDataComplete);
        Assert.Null(lineEntry.SellPriceSnapshot);
        Assert.Null(lineEntry.StandardExpectedDirectCostSnapshot);
    }

    [Fact]
    public void Catalog_item_without_price_book_snapshot_is_incomplete()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        // State 2: CatalogItemId set, PriceBookVersionLineId null — the item currently carries no
        // price-book entry.
        var addResult = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, priceBookVersionLineId: null, "Drain Pan", "each", 1m,
            sellPriceSnapshot: null, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor);
        Assert.True(addResult.IsSuccess);

        var lineEntry = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Lines.Single();

        Assert.False(lineEntry.IsFinancialDataComplete);
    }

    // --- BL135 Batch 3a-iii: ADR-467 rounding + effective per-component resolution folding ---

    [Fact]
    public void Line_totals_round_half_up_and_the_three_visit_totals_reconcile()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        // 10.005 * 1 -> 10.005: round-half-up = 10.01 (banker's/ToEven would give 10.00).
        AddCompleteCatalogLine(work, quantity: 1m, sellPrice: 10.005m, cost: 3.334m);
        // 2.225 * 1 -> 2.225: round-half-up = 2.23.
        AddCompleteCatalogLine(work, quantity: 1m, sellPrice: 2.225m, cost: 1.111m);

        var lines = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Lines;
        Assert.Equal(10.01m, lines[0].LineSalesTotal);
        Assert.Equal(3.33m, lines[0].LineStandardExpectedDirectCostTotal);
        Assert.Equal(2.23m, lines[1].LineSalesTotal);
        Assert.Equal(1.11m, lines[1].LineStandardExpectedDirectCostTotal);

        var totals = ActualWorkFinancialProjection.ProjectVisit(work.Lines, NoResolutions).Totals;
        Assert.Equal(12.24m, totals.TotalSalesPrice);
        Assert.Equal(4.44m, totals.TotalStandardExpectedDirectCost);
        Assert.Equal(7.80m, totals.TotalMargin);
        // The grand total is the exact sum of the already-rounded line totals (ADR-467).
        Assert.Equal(totals.TotalSalesPrice - totals.TotalStandardExpectedDirectCost, totals.TotalMargin);
        Assert.Equal(lines.Sum(l => l.LineSalesTotal), totals.TotalSalesPrice);
        Assert.Equal(lines.Sum(l => l.LineMargin), totals.TotalMargin);
    }

    [Fact]
    public void Resolution_supplies_a_missing_component_only_a_snapshot_always_wins()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        // Snapshot carries sell price only; direct cost is missing.
        var line = ActualWorkTestData.AddLine(
            work,
            CatalogItemId, PriceBookVersionLineId, "Drain Pan", "each", 2m,
            sellPriceSnapshot: 10m, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, Actor).Value;

        var resolutions = new[]
        {
            // Also supplies a sell price — must be ignored because the snapshot is present.
            Resolution(line.Id, sell: 99m, cost: 4m, T0),
        };

        var entry = ActualWorkFinancialProjection.ProjectVisit(work.Lines, resolutions).Lines.Single();
        Assert.True(entry.IsFinancialDataComplete);
        Assert.False(entry.SellPriceResolved);
        Assert.Null(entry.ResolvedSellPrice);
        Assert.True(entry.DirectCostResolved);
        Assert.Equal(4m, entry.ResolvedStandardExpectedDirectCost);
        Assert.Equal("OwnerSetPrice", entry.ResolvedStandardExpectedDirectCostBasis);
        Assert.Equal(20m, entry.LineSalesTotal);   // 10 (snapshot) * 2
        Assert.Equal(8m, entry.LineStandardExpectedDirectCostTotal); // 4 (resolved) * 2
        Assert.Empty(ActualWorkFinancialProjection.ProjectVisit(work.Lines, resolutions).Blockers);
    }

    [Fact]
    public void Newest_supplying_row_wins_per_component()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        var line = AddCustomLine(work).Value;

        var resolutions = new[]
        {
            Resolution(line.Id, sell: 5m, cost: 3m, T0),
            Resolution(line.Id, sell: 8m, cost: null, T0.AddHours(1)),
        };

        var entry = ActualWorkFinancialProjection.ProjectVisit(work.Lines, resolutions).Lines.Single();
        Assert.True(entry.IsFinancialDataComplete);
        Assert.True(entry.SellPriceResolved);
        Assert.Equal(8m, entry.ResolvedSellPrice);           // newer sell-price row
        Assert.Equal(3m, entry.ResolvedStandardExpectedDirectCost); // older row still supplies cost
    }

    [Fact]
    public void Each_component_keeps_its_own_provenance_in_the_mixed_case()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        var line = AddCustomLine(work).Value;

        var resolutions = new[]
        {
            // Older: sell price only, SupplierReceipt.
            Resolution(line.Id, sell: 5m, cost: null, T0, FinancialResolutionBasis.SupplierReceipt),
            // Newer: direct cost only, FixedAgreement — must not erase the earlier resolved sell price.
            Resolution(line.Id, sell: null, cost: 2m, T0.AddHours(1), FinancialResolutionBasis.FixedAgreement),
        };

        var entry = ActualWorkFinancialProjection.ProjectVisit(work.Lines, resolutions).Lines.Single();
        Assert.True(entry.IsFinancialDataComplete);
        Assert.Equal(5m, entry.ResolvedSellPrice);
        Assert.Equal("SupplierReceipt", entry.ResolvedSellPriceBasis);
        Assert.Equal(2m, entry.ResolvedStandardExpectedDirectCost);
        Assert.Equal("FixedAgreement", entry.ResolvedStandardExpectedDirectCostBasis);
    }

    [Fact]
    public void Blockers_name_only_the_still_missing_component_after_a_partial_resolution()
    {
        var work = ActualWorkTestData.CreateDraft(AccountId, RequestId, Actor).Value;
        var line = AddCustomLine(work).Value; // both components missing

        // Only the sell price is resolved; direct cost is still a blocker.
        var resolutions = new[] { Resolution(line.Id, sell: 5m, cost: null, T0) };

        var totals = ActualWorkFinancialProjection.ProjectVisit(work.Lines, resolutions).Totals;
        Assert.True(totals.HasIncompleteFinancialData);
        Assert.Equal(1, totals.IncompleteLineCount);
        Assert.Null(totals.TotalSalesPrice);

        var blocker = Assert.Single(ActualWorkFinancialProjection.ProjectVisit(work.Lines, resolutions).Blockers);
        Assert.Equal(line.Id, blocker.LineId);
        Assert.False(blocker.SellPriceMissing);
        Assert.True(blocker.StandardExpectedDirectCostMissing);
    }
}
