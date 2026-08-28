using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// API-facing orchestration for an Owner/Admin office actor supplying a missing per-line financial
/// component on a submitted Actual Work visit (ADR-493 / BL135 §4 Batch 3a-ii). One mutation
/// family: append one immutable <see cref="ActualWorkLineFinancialResolution"/>. Gate is identical
/// to <see cref="ActualWorkFinancialReadApiService"/> / <see cref="ActualWorkReviewApiService"/> —
/// authenticated, non-blocked/read-only account access, the Price Book entitlement,
/// <c>RequestsOperate</c>, the <c>AccountingManage</c> permission, and an explicit Owner/Admin role
/// check for defense-in-depth. Owns no transaction — <see cref="IActualWorkFinancialResolutionPersistence.CreateResolutionAsync"/>
/// is the atomic load / version / review-state / append boundary; this service composes the auth
/// stack, runs domain value validation via <see cref="ActualWorkLineFinancialResolution.Create"/>,
/// and maps the outcome. The read-projection fold that surfaces the resolved values is Batch 3a-iii.
/// </summary>
public sealed class ActualWorkFinancialResolutionApiService(
    IActualWorkFinancialResolutionPersistence resolutionPersistence,
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

    /// <summary>Appends one financial resolution and returns the post-append visit
    /// <c>ConcurrencyVersion</c> — the value a subsequent <c>POST .../review</c> must echo. A review
    /// command holding the pre-resolution version is rejected as a conflict (the whole point of the
    /// token rotation in <see cref="ActualWork.RefreshConcurrencyVersionForFinancialResolution"/>).</summary>
    public async Task<Result<Guid>> CreateResolutionAsync(
        Guid actualWorkId, Guid lineId, ActualWorkFinancialResolutionCommand command,
        Guid expectedVisitVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        if (actualWorkId == Guid.Empty)
            return Result<Guid>.Failure(ActualWorkErrors.NotFound);
        if (lineId == Guid.Empty)
            return Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.FinancialResolutionLineNotFound);

        if (!Enum.TryParse<FinancialResolutionBasis>(command.Basis, ignoreCase: true, out var basis)
            || !Enum.IsDefined(basis))
            return Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.FinancialResolutionInvalidBasis);

        var build = ActualWorkLineFinancialResolution.Create(
            currentUser.AccountId, actualWorkId, lineId,
            command.ResolvedUnitSellPrice, command.ResolvedUnitStandardExpectedDirectCost,
            basis, command.Reason ?? string.Empty, currentUser.UserId, clock.UtcNow);
        if (build.IsFailure)
            return Result<Guid>.Failure(build.Error);

        var outcome = await resolutionPersistence.CreateResolutionAsync(build.Value, expectedVisitVersion, ct);

        return outcome.Result switch
        {
            ActualWorkResolutionResult.Committed =>
                Result<Guid>.Success(outcome.NewVisitConcurrencyVersion!.Value),
            ActualWorkResolutionResult.VisitNotFound =>
                Result<Guid>.Failure(ActualWorkErrors.NotFound),
            ActualWorkResolutionResult.VisitNotSubmitted =>
                Result<Guid>.Failure(ActualWorkErrors.NotSubmitted),
            ActualWorkResolutionResult.VisitAlreadyReviewed =>
                Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.FinancialResolutionVisitAlreadyReviewed),
            ActualWorkResolutionResult.LineNotFoundOnVisit =>
                Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.FinancialResolutionLineNotFound),
            ActualWorkResolutionResult.SnapshotComponentAlreadyValid =>
                Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.FinancialResolutionSnapshotComponentAlreadyValid),
            _ => Result<Guid>.Failure(ActualWorkErrors.VersionMismatch),
        };
    }

    /// <summary>Owner/Admin office-financial gate — identical composition to
    /// <see cref="ActualWorkFinancialReadApiService.AuthorizeAsync"/>: authenticated,
    /// non-blocked/read-only account access, the Price Book entitlement, <c>RequestsOperate</c>, the
    /// <c>AccountingManage</c> permission (ADR-493 / BL135), and an explicit Owner/Admin role check
    /// for defense-in-depth — no <c>ActualWorkCapture</c>.</summary>
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
