using OpHalo.Keep.Core.Entities;
using OpHalo.SharedKernel.Results;

namespace OpHalo.IntegrationTests.Support;

/// <summary>
/// Single construction seam for <see cref="ActualWork"/> aggregates in integration tests
/// (BL136 4c-i-0b). Every in-process <c>AddLine</c> call site routes through here so that slice
/// 4c-i-a-1 — which adds a required line performer (ADR-494 D1) and an optional ticket default (D2),
/// backed by a non-null database column after 4c-i-mig — changes one file instead of every test.
///
/// The many integration tests that call <see cref="ActualWork.Create"/> directly (not through
/// <see cref="CreateDraft"/>) never set a ticket default, so <see cref="AddLine"/> supplies a
/// fixture-setup performer: an omitted <c>performedByAccountUserId</c> resolves to the line's
/// <c>createdByUserId</c>. This is test-fixture convenience only — the server never derives a
/// performer. Tests asserting the <c>PerformerRequired</c> gate call <see cref="ActualWork.AddLine"/>
/// directly with an explicit <c>performedByAccountUserId: null</c>.
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
