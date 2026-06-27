using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class ClearShareIntentService(
    IKeepRequestOperatePersistence operatePersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly string[] ValidMethods = ["copy_link", "native_share", "manual_mark_shared"];

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error ViewerBlocked =
        Error.Create("KeepRequest.ShareIntentViewerBlocked", "Viewers cannot record share intent.");

    private static readonly Error OffSeasonBlocked =
        Error.Create("KeepRequest.ShareIntentOffSeasonBlocked", "Share intent is not available while the account is off-season.");

    private static readonly Error InvalidMethod =
        Error.Create("KeepRequest.ShareIntentInvalidMethod", "The provided share method is not valid. Allowed values: copy_link, native_share, manual_mark_shared.");

    public async Task<Result> ExecuteAsync(ClearShareIntentCommand command, CancellationToken ct = default)
    {
        // --- Auth stack ---
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result.Failure(Forbidden);

        if (userSnapshot.Role is AccountUserRole.Viewer)
            return Result.Failure(ViewerBlocked);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
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
            return Result.Failure(OffSeasonBlocked);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result.Failure(Forbidden);

        // --- Method validation (before row load to fail fast on bad input) ---
        if (!ValidMethods.Contains(command.Method))
            return Result.Failure(InvalidMethod);

        // --- Actor display name ---
        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result.Failure(Forbidden);

        // --- Row authorization scope ---
        var scope = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        // --- Load request for mutation ---
        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result.Failure(KeepRequestErrors.NotFound);

        // --- Idempotency: already cleared → 204 without re-write ---
        if (!request.NeedsShare)
            return Result.Success();

        // --- Apply ---
        var nowUtc = clock.UtcNow;
        request.ClearNeedsShare();

        var shareEvent = KeepRequestEvent.CreateShareIntentRecorded(
            request.Id,
            request.AccountId,
            currentUser.UserId,
            actorDisplayName,
            command.Method,
            nowUtc);

        var commitResult = await operatePersistence.CommitAsync(request, shareEvent, ct);
        return commitResult switch
        {
            KeepRequestCommitResult.Committed => Result.Success(),
            KeepRequestCommitResult.Conflict  => Result.Failure(KeepRequestErrors.RequestChanged),
            _ => throw new ArgumentOutOfRangeException(nameof(commitResult))
        };
    }
}
