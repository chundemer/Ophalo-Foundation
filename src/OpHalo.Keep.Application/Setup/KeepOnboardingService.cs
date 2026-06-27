using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Setup;

public enum KeepOnboardingManualStep
{
    QuickCaptureExercise,
    TrackerReview,
    SpamClassification
}

public sealed record KeepOnboardingChecklistResult(
    bool ProfileAndContactSaved,
    bool TimezoneSaved,
    bool PolicySaved,
    bool IntakeLinkActive,
    bool OperatorInvited,
    bool MobileDeviceRegistered,
    bool FirstRequestCreated,
    bool QuickCaptureExerciseDone,
    bool TrackerReviewDone,
    bool SpamClassificationExplained);

public sealed class KeepOnboardingService(
    IKeepProductOpsPersistence productOpsPersistence,
    IKeepSetupPersistence setupPersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error InvalidStep =
        Error.Create("onboarding.invalid_step", "Invalid manual step value.");

    public async Task<Result<KeepOnboardingChecklistResult>> GetChecklistAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepOnboardingChecklistResult>.Failure(auth.Error);

        var data = await productOpsPersistence.GetOnboardingDataAsync(currentUser.AccountId, ct);

        return Result<KeepOnboardingChecklistResult>.Success(new KeepOnboardingChecklistResult(
            ProfileAndContactSaved: data.HasProfileSavedEvent,
            TimezoneSaved: data.HasProfileSavedEvent,
            PolicySaved: data.HasPolicySavedEvent,
            IntakeLinkActive: data.IsIntakeLinkActive,
            OperatorInvited: data.HasNonOwnerActiveMember,
            MobileDeviceRegistered: data.HasDeviceRegistered,
            FirstRequestCreated: data.HasRequest,
            QuickCaptureExerciseDone: data.HasQuickCaptureEvent,
            TrackerReviewDone: data.HasTrackerReviewEvent,
            SpamClassificationExplained: data.HasSpamExplainedEvent));
    }

    public async Task<Result> MarkStepCompleteAsync(KeepOnboardingManualStep step, CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return auth;

        var eventType = step switch
        {
            KeepOnboardingManualStep.QuickCaptureExercise => KeepProductOpsEventType.QuickCaptureExerciseDone,
            KeepOnboardingManualStep.TrackerReview => KeepProductOpsEventType.TrackerReviewDone,
            KeepOnboardingManualStep.SpamClassification => KeepProductOpsEventType.SpamClassificationExplained,
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };

        await productOpsPersistence.RecordEventIfFirstAsync(currentUser.AccountId, eventType, clock.UtcNow, ct);
        return Result.Success();
    }

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        var userSnapshot = await setupPersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result.Failure(Forbidden);

        var accountSnapshot = await setupPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.SettingsManage))
            return Result.Failure(Forbidden);

        var nowUtc = clock.UtcNow;
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            nowUtc);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
