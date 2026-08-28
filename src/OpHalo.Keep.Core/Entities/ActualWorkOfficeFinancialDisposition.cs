using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Immutable, append-only <b>visit-level</b> record of the office financially disposing of a
/// zero-line <see cref="ActualWork"/> visit (ADR-493 / build-log/129, build-log/135 §4 Batch 1).
/// Attaches to the visit, not a line — the shape that lets a zero-line visit reach billing
/// eligibility (build-log/135 §5 proof 1). <see cref="OfficeFinancialDispositionKind.NoCharge"/>
/// is the only kind this phase. Never updated or removed; the effective disposition is the
/// most-recent row. Rejecting a disposition on a lined visit is enforced against the loaded visit
/// in Batch 3b-i, not here.
/// </summary>
public sealed class ActualWorkOfficeFinancialDisposition : BaseEntity
{
    public const int MaxReasonLength = 2000;

    public Guid AccountId { get; private set; }

    public Guid ActualWorkId { get; private set; }

    public OfficeFinancialDispositionKind Kind { get; private set; }

    public string Reason { get; private set; } = null!;

    public Guid DisposedByAccountUserId { get; private set; }

    public DateTime DisposedAtUtc { get; private set; }

    private ActualWorkOfficeFinancialDisposition()
    {
    }

    public static Result<ActualWorkOfficeFinancialDisposition> Create(
        Guid accountId,
        Guid actualWorkId,
        OfficeFinancialDispositionKind kind,
        string reason,
        Guid disposedByAccountUserId,
        DateTime disposedAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (actualWorkId == Guid.Empty)
            throw new ArgumentException("ActualWorkId must not be empty.", nameof(actualWorkId));
        if (disposedByAccountUserId == Guid.Empty)
            throw new ArgumentException("DisposedByAccountUserId must not be empty.", nameof(disposedByAccountUserId));

        if (!Enum.IsDefined(kind))
            return Result<ActualWorkOfficeFinancialDisposition>.Failure(
                ActualWorkFinancialResolutionErrors.DispositionInvalidKind);

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
            return Result<ActualWorkOfficeFinancialDisposition>.Failure(
                ActualWorkFinancialResolutionErrors.DispositionReasonRequired);
        if (trimmedReason.Length > MaxReasonLength)
            return Result<ActualWorkOfficeFinancialDisposition>.Failure(
                ActualWorkFinancialResolutionErrors.DispositionReasonTooLong);

        return Result<ActualWorkOfficeFinancialDisposition>.Success(new ActualWorkOfficeFinancialDisposition
        {
            AccountId = accountId,
            ActualWorkId = actualWorkId,
            Kind = kind,
            Reason = trimmedReason,
            DisposedByAccountUserId = disposedByAccountUserId,
            DisposedAtUtc = disposedAtUtc,
            CreatedByUserId = disposedByAccountUserId,
        });
    }
}
