using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Setup;

public sealed record KeepBusinessSetupResult(
    bool BusinessInfoComplete,
    bool AddFirstRequestComplete,
    bool ReviewCustomerPageComplete,
    bool CreateIntakePageComplete,
    bool ShareIntakePageComplete,
    bool BuildTeamComplete,
    bool UseMobileComplete,
    IReadOnlyList<KeepSetupStep> DeferredSteps,
    IntendedTeamSize? IntendedTeamSize);

public sealed class KeepBusinessSetupService(
    IKeepSetupDeferralPersistence deferralPersistence,
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
        Error.Create("setup.invalid_step", "Invalid setup step.");

    public async Task<Result<KeepBusinessSetupResult>> GetBusinessSetupAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepBusinessSetupResult>.Failure(auth.Error);

        var data = await deferralPersistence.GetBusinessSetupDataAsync(currentUser.AccountId, ct);

        // Steps that are complete are excluded from DeferredSteps even if a deferral row exists.
        var completedSteps = CompletedSteps(data);
        var activeDeferrals = data.DeferredSteps
            .Where(s => !completedSteps.Contains(s))
            .ToList();

        return Result<KeepBusinessSetupResult>.Success(new KeepBusinessSetupResult(
            BusinessInfoComplete: data.HasProfileSavedEvent,
            AddFirstRequestComplete: data.HasRequest,
            ReviewCustomerPageComplete: false,   // FirstCustomerPageView deferred to S22d
            CreateIntakePageComplete: data.IsIntakeLinkActive,
            ShareIntakePageComplete: false,      // IntakeLinkShared event deferred to S22d
            BuildTeamComplete: data.HasNonOwnerActiveMember,
            UseMobileComplete: data.HasDeviceRegistered,
            DeferredSteps: activeDeferrals,
            IntendedTeamSize: null));             // KeepAccountSetupPreferences deferred to S22c
    }

    public async Task<Result> DeferStepAsync(KeepSetupStep step, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(step))
            return Result.Failure(InvalidStep);

        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return auth;

        var deferral = KeepSetupDeferral.Create(
            currentUser.AccountId,
            step,
            clock.UtcNow,
            currentUser.UserId);

        await deferralPersistence.DeferStepAsync(deferral, ct);
        return Result.Success();
    }

    private static HashSet<KeepSetupStep> CompletedSteps(KeepBusinessSetupQueryData data)
    {
        var completed = new HashSet<KeepSetupStep>();
        if (data.HasProfileSavedEvent) completed.Add(KeepSetupStep.BusinessInfo);
        if (data.HasRequest) completed.Add(KeepSetupStep.AddFirstRequest);
        if (data.IsIntakeLinkActive) completed.Add(KeepSetupStep.CreateIntakePage);
        if (data.HasNonOwnerActiveMember) completed.Add(KeepSetupStep.BuildTeam);
        if (data.HasDeviceRegistered) completed.Add(KeepSetupStep.UseMobile);
        return completed;
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

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
