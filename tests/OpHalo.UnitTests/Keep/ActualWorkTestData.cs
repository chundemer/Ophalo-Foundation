using OpHalo.Keep.Core.Entities;
using OpHalo.SharedKernel.Results;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Single construction seam for <see cref="ActualWork"/> aggregates in unit tests (BL136 4c-i-0a).
/// Every happy-path <c>Create</c> and every <c>AddLine</c> call site routes through here so that
/// slice 4c-i-a-1 — which adds a required line performer and an optional ticket default — changes
/// one file instead of every test. This commit is a pure passthrough: no behaviour change.
/// Tests that exercise <see cref="ActualWork.Create"/> argument validation call the domain directly.
/// </summary>
internal static class ActualWorkTestData
{
    public static Result<ActualWork> CreateDraft(Guid accountId, Guid requestId, Guid createdByUserId)
        => ActualWork.Create(accountId, requestId, createdByUserId);

    public static Result<ActualWorkLine> AddLine(
        ActualWork work,
        Guid? catalogItemId,
        Guid? priceBookVersionLineId,
        string displayNameSnapshot,
        string? unitOfMeasureSnapshot,
        decimal actualQuantity,
        decimal? sellPriceSnapshot,
        decimal? standardExpectedDirectCostSnapshot,
        string? note,
        Guid? commercialBaselineSourceLineId,
        Guid createdByUserId)
        => work.AddLine(
            catalogItemId,
            priceBookVersionLineId,
            displayNameSnapshot,
            unitOfMeasureSnapshot,
            actualQuantity,
            sellPriceSnapshot,
            standardExpectedDirectCostSnapshot,
            note,
            commercialBaselineSourceLineId,
            createdByUserId);
}
