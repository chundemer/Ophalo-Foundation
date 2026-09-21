using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Setup;

/// <summary>
/// GAP-100/ADR-505 settings surface: three narrow operations, each independently authorized and
/// atomic. This service composes the <c>keep.settings.manage</c> authorization boundary (matching
/// <see cref="KeepSetupService"/>) and delegates the cross-aggregate validation and atomic write to
/// <see cref="IKeepResponsePolicyPersistence"/>, which owns the transaction.
/// </summary>
public sealed class KeepResponsePolicyService(
    IKeepResponsePolicyPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result> UpdatePolicyTargetsAsync(
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays,
        ResponseTimingBasis? firstResponseTimingBasis,
        ResponseTimingBasis? standardResponseTimingBasis,
        ResponseTimingBasis? priorityResponseTimingBasis,
        string? expectedSettingsVersion,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return auth;

        var actorDisplayName = await persistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result.Failure(Forbidden);

        return await persistence.UpdatePolicyTargetsAsync(
            currentUser.AccountId,
            currentUser.UserId,
            actorDisplayName,
            firstResponseTargetMinutes,
            standardResponseTargetMinutes,
            priorityResponseTargetMinutes,
            statusCheckThresholdDays,
            firstResponseTimingBasis,
            standardResponseTimingBasis,
            priorityResponseTimingBasis,
            expectedSettingsVersion,
            clock.UtcNow,
            ct);
    }

    public async Task<Result> UpdateCalendarAsync(
        IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyList<KeepCalendarClosureSnapshot> closuresToSet,
        IReadOnlyList<DateOnly> closureDatesToRemove,
        string? expectedSettingsVersion,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return auth;

        var actorDisplayName = await persistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result.Failure(Forbidden);

        return await persistence.UpdateCalendarAsync(
            currentUser.AccountId,
            currentUser.UserId,
            actorDisplayName,
            weeklyIntervals,
            closuresToSet,
            closureDatesToRemove,
            expectedSettingsVersion,
            clock.UtcNow,
            ct);
    }

    public async Task<Result> UpdateTimeZoneAsync(string timeZone, CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return auth;

        var actorDisplayName = await persistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result.Failure(Forbidden);

        return await persistence.UpdateTimeZoneAsync(
            currentUser.AccountId,
            currentUser.UserId,
            actorDisplayName,
            timeZone,
            clock.UtcNow,
            ct);
    }

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        var userSnapshot = await persistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result.Failure(Forbidden);

        var accountSnapshot = await persistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
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
