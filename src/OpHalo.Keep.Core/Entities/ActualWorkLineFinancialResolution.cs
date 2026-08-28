using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Immutable, append-only record of an office actor supplying a missing per-line financial
/// component on a submitted <see cref="ActualWork"/> visit (ADR-493 / build-log/129,
/// build-log/135 §4 Batch 1). One or both of <see cref="ResolvedUnitSellPrice"/> and
/// <see cref="ResolvedUnitStandardExpectedDirectCost"/> is set; <see cref="Basis"/> and a required
/// <see cref="Reason"/> record why. Never updated or removed — supersession is a newer row, and the
/// effective value per component is the most-recent supplying row (build-log/135 §5 proof 2).
/// This entity cannot see snapshot or review state; "fills only a missing component, only before
/// review" is enforced against the loaded visit in Batch 3a-ii.
/// </summary>
public sealed class ActualWorkLineFinancialResolution : BaseEntity
{
    public const int MaxReasonLength = 2000;

    public Guid AccountId { get; private set; }

    public Guid ActualWorkId { get; private set; }

    public Guid ActualWorkLineId { get; private set; }

    public decimal? ResolvedUnitSellPrice { get; private set; }

    public decimal? ResolvedUnitStandardExpectedDirectCost { get; private set; }

    public FinancialResolutionBasis Basis { get; private set; }

    public string Reason { get; private set; } = null!;

    public Guid ResolvedByAccountUserId { get; private set; }

    public DateTime ResolvedAtUtc { get; private set; }

    private ActualWorkLineFinancialResolution()
    {
    }

    public static Result<ActualWorkLineFinancialResolution> Create(
        Guid accountId,
        Guid actualWorkId,
        Guid actualWorkLineId,
        decimal? resolvedUnitSellPrice,
        decimal? resolvedUnitStandardExpectedDirectCost,
        FinancialResolutionBasis basis,
        string reason,
        Guid resolvedByAccountUserId,
        DateTime resolvedAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (actualWorkId == Guid.Empty)
            throw new ArgumentException("ActualWorkId must not be empty.", nameof(actualWorkId));
        if (actualWorkLineId == Guid.Empty)
            throw new ArgumentException("ActualWorkLineId must not be empty.", nameof(actualWorkLineId));
        if (resolvedByAccountUserId == Guid.Empty)
            throw new ArgumentException("ResolvedByAccountUserId must not be empty.", nameof(resolvedByAccountUserId));

        if (resolvedUnitSellPrice is null && resolvedUnitStandardExpectedDirectCost is null)
            return Result<ActualWorkLineFinancialResolution>.Failure(
                ActualWorkFinancialResolutionErrors.FinancialResolutionValueRequired);

        if (resolvedUnitSellPrice is < 0m || resolvedUnitStandardExpectedDirectCost is < 0m)
            return Result<ActualWorkLineFinancialResolution>.Failure(
                ActualWorkFinancialResolutionErrors.FinancialResolutionValueNegative);

        if (!Enum.IsDefined(basis))
            return Result<ActualWorkLineFinancialResolution>.Failure(
                ActualWorkFinancialResolutionErrors.FinancialResolutionInvalidBasis);

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
            return Result<ActualWorkLineFinancialResolution>.Failure(
                ActualWorkFinancialResolutionErrors.FinancialResolutionReasonRequired);
        if (trimmedReason.Length > MaxReasonLength)
            return Result<ActualWorkLineFinancialResolution>.Failure(
                ActualWorkFinancialResolutionErrors.FinancialResolutionReasonTooLong);

        return Result<ActualWorkLineFinancialResolution>.Success(new ActualWorkLineFinancialResolution
        {
            AccountId = accountId,
            ActualWorkId = actualWorkId,
            ActualWorkLineId = actualWorkLineId,
            ResolvedUnitSellPrice = resolvedUnitSellPrice,
            ResolvedUnitStandardExpectedDirectCost = resolvedUnitStandardExpectedDirectCost,
            Basis = basis,
            Reason = trimmedReason,
            ResolvedByAccountUserId = resolvedByAccountUserId,
            ResolvedAtUtc = resolvedAtUtc,
            CreatedByUserId = resolvedByAccountUserId,
        });
    }
}
