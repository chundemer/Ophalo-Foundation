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
/// API-facing orchestration for an Owner/Admin office actor financially disposing of a zero-line
/// submitted Actual Work visit as <see cref="OfficeFinancialDispositionKind.NoCharge"/> (ADR-493 /
/// BL135 §4 Batch 3b-i). A dedicated class — not a method on
/// <see cref="ActualWorkFinancialResolutionApiService"/> — keeps each closeout auth copy small and
/// matches the one-service-per-Actual-Work-action pattern. One mutation family: append one immutable
/// <see cref="ActualWorkOfficeFinancialDisposition"/>. Gate is identical to
/// <see cref="ActualWorkFinancialResolutionApiService"/> — authenticated, non-blocked/read-only
/// account access, the Price Book entitlement, <c>RequestsOperate</c>, the <c>AccountingManage</c>
/// permission, and an explicit Owner/Admin role check for defense-in-depth. Owns no transaction —
/// <see cref="IActualWorkFinancialResolutionPersistence.RecordDispositionAsync"/> is the atomic
/// load / version / review-state / zero-line / append boundary; this service composes the auth
/// stack, parses <c>Kind</c>, runs domain reason validation via
/// <see cref="ActualWorkOfficeFinancialDisposition.Create"/>, and maps the outcome.
/// </summary>
public sealed class ActualWorkOfficeFinancialDispositionApiService(
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

    /// <summary>Appends one office financial disposition and returns the post-append visit
    /// <c>ConcurrencyVersion</c> — the value a subsequent <c>POST .../review</c> must echo. A review
    /// command holding the pre-disposition version is rejected as a conflict (the token rotation in
    /// <see cref="ActualWork.RefreshConcurrencyVersionForOfficeFinancialDisposition"/>).</summary>
    public async Task<Result<Guid>> RecordDispositionAsync(
        Guid actualWorkId, ActualWorkDispositionCommand command, Guid expectedVisitVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        if (actualWorkId == Guid.Empty)
            return Result<Guid>.Failure(ActualWorkErrors.NotFound);

        if (!Enum.TryParse<OfficeFinancialDispositionKind>(command.Kind?.Trim(), ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind))
            return Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.DispositionInvalidKind);

        var build = ActualWorkOfficeFinancialDisposition.Create(
            currentUser.AccountId, actualWorkId, kind, command.Reason ?? string.Empty,
            currentUser.UserId, clock.UtcNow);
        if (build.IsFailure)
            return Result<Guid>.Failure(build.Error);

        var outcome = await resolutionPersistence.RecordDispositionAsync(build.Value, expectedVisitVersion, ct);

        return outcome.Result switch
        {
            ActualWorkDispositionResult.Committed =>
                Result<Guid>.Success(outcome.NewVisitConcurrencyVersion!.Value),
            ActualWorkDispositionResult.VisitNotFound =>
                Result<Guid>.Failure(ActualWorkErrors.NotFound),
            ActualWorkDispositionResult.Superseded =>
                Result<Guid>.Failure(ActualWorkErrors.Superseded),
            ActualWorkDispositionResult.VisitNotSubmitted =>
                Result<Guid>.Failure(ActualWorkErrors.NotSubmitted),
            ActualWorkDispositionResult.VisitAlreadyReviewed =>
                Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.DispositionVisitAlreadyReviewed),
            ActualWorkDispositionResult.VisitHasLines =>
                Result<Guid>.Failure(ActualWorkFinancialResolutionErrors.DispositionVisitHasLines),
            _ => Result<Guid>.Failure(ActualWorkErrors.VersionMismatch),
        };
    }

    /// <summary>Owner/Admin office-financial gate — identical composition to
    /// <see cref="ActualWorkFinancialResolutionApiService"/>: authenticated, non-blocked/read-only
    /// account access, the Price Book entitlement, <c>RequestsOperate</c>, the <c>AccountingManage</c>
    /// permission (ADR-493 / BL135), and an explicit Owner/Admin role check for defense-in-depth —
    /// no <c>ActualWorkCapture</c>.</summary>
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
