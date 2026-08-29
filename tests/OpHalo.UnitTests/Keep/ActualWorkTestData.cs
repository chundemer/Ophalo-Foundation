using OpHalo.Keep.Core.Entities;
using OpHalo.SharedKernel.Results;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Single construction seam for <see cref="ActualWork"/> aggregates in unit tests (BL136 4c-i-0a).
/// Every happy-path <c>Create</c> and every <c>AddLine</c> call site routes through here so that
/// slice 4c-i-a-1 — which adds a required line performer (ADR-494 D1) and an optional ticket default
/// (D2) — changes one file instead of every test.
///
/// Fixture-setup default, not a domain behaviour: an omitted <c>performedByAccountUserId</c> on
/// <see cref="AddLine"/> resolves to the line's <c>createdByUserId</c> so existing call sites stay
/// valid. This is test-fixture convenience only — the server never derives a performer. Tests that
/// assert the <c>PerformerRequired</c> gate must call <see cref="ActualWork.AddLine"/> directly with
/// an explicit <c>performedByAccountUserId: null</c>.
/// Tests that exercise <see cref="ActualWork.Create"/> argument validation call the domain directly.
/// </summary>
internal static class ActualWorkTestData
{
    public static Result<ActualWork> CreateDraft(
        Guid accountId,
        Guid requestId,
        Guid createdByUserId,
        Guid? defaultPerformedByAccountUserId = null)
        => ActualWork.Create(accountId, requestId, createdByUserId, defaultPerformedByAccountUserId);

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
        Guid createdByUserId,
        Guid? performedByAccountUserId = null)
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
            createdByUserId,
            performedByAccountUserId ?? createdByUserId);
}
