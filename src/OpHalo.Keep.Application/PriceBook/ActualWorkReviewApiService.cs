using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// API-facing orchestration for marking a submitted Actual Work visit reviewed (Batch 6,
/// build-log/129). Owner/Admin-only office capability: <c>RequestsOperate</c> + the Price Book
/// entitlement (ADR-462) + an explicit Owner/Admin role check — deliberately not
/// <c>ActualWorkCapture</c>, which is the field-recorder's own capability and unrelated to office
/// review authority (build-log/129, "6 preflight — locked decisions"). Owns no transaction —
/// <see cref="IActualWorkReviewPersistence"/> owns the entire atomic mark-reviewed/signal-resolve
/// operation; this service only composes the auth stack and maps its outcome to a
/// <see cref="Result{TValue}"/>.
/// </summary>
public sealed class ActualWorkReviewApiService(
    IActualWorkReviewPersistence reviewPersistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<Guid>> MarkReviewedAsync(
        Guid actualWorkId, string? reviewNote, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        var outcome = await reviewPersistence.MarkReviewedAsync(
            currentUser.AccountId, actualWorkId, expectedVersion, currentUser.UserId, reviewNote, clock.UtcNow, ct);

        return outcome.Result switch
        {
            ActualWorkReviewResult.Committed => Result<Guid>.Success(outcome.ConcurrencyVersion!.Value),
            ActualWorkReviewResult.NotFound => Result<Guid>.Failure(ActualWorkErrors.NotFound),
            ActualWorkReviewResult.NotSubmitted => Result<Guid>.Failure(ActualWorkErrors.NotSubmitted),
            ActualWorkReviewResult.AlreadyReviewed => Result<Guid>.Failure(ActualWorkErrors.AlreadyReviewed),
            ActualWorkReviewResult.ReviewNoteTooLong => Result<Guid>.Failure(ActualWorkErrors.ReviewNoteTooLong),
            ActualWorkReviewResult.BlockedIncompleteFinancials =>
                Result<Guid>.Failure(ActualWorkErrors.ReviewBlockedIncompleteFinancials),
            ActualWorkReviewResult.BlockedZeroLineDisposition =>
                Result<Guid>.Failure(ActualWorkErrors.ReviewBlockedZeroLineDispositionRequired),
            _ => Result<Guid>.Failure(ActualWorkErrors.VersionMismatch),
        };
    }

    /// <summary>Owner/Admin office-review gate: authenticated, non-blocked/read-only account access,
    /// the Price Book entitlement, <c>RequestsOperate</c>, the <c>AccountingManage</c> office-financial
    /// permission (ADR-493 / BL135), and an explicit Owner/Admin role check for defense-in-depth —
    /// no <c>ActualWorkCapture</c>. Distinct from <c>ActualWorkDraftApiService.AuthorizeAsync</c>,
    /// which is the field-recorder's three-gate composition and would wrongly admit a non-office
    /// Operator here.</summary>
    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result.Failure(Forbidden);

        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result.Failure(Forbidden);

        if (roleSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.AccountingManage))
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
