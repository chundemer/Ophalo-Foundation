using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Setup;

public sealed class KeepSetupService(
    IKeepSetupPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    // V1 defaults — returned when no policy row exists yet.
    private const int DefaultFirstResponseTargetMinutes = 15;
    private const int DefaultStandardResponseTargetMinutes = 240;
    private const int DefaultPriorityResponseTargetMinutes = 60;
    private const int DefaultStatusCheckThresholdDays = 5;

    public async Task<Result<KeepSetupResult>> GetSetupAsync(CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepSetupResult>.Failure(auth.Error);

        var (account, profile) = await persistence.GetProfileDataAsync(currentUser.AccountId, ct);
        var policy = await persistence.GetPolicyAsync(currentUser.AccountId, ct);

        return Result<KeepSetupResult>.Success(new KeepSetupResult(
            BusinessName: account.BusinessName,
            TimeZone: account.TimeZone,
            CustomerFacingPhone: profile?.CustomerFacingPhone,
            CustomerFacingEmail: profile?.CustomerFacingEmail,
            LogoUrl: profile?.LogoUrl,
            WebsiteUrl: profile?.WebsiteUrl,
            ResponsePolicy: ToPolicy(policy)));
    }

    public async Task<Result<KeepSetupResult>> UpdateProfileAsync(
        string businessName,
        string timeZone,
        string? customerFacingPhone,
        string? customerFacingEmail,
        string? logoUrl,
        string? websiteUrl,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepSetupResult>.Failure(auth.Error);

        var (account, existingProfile) = await persistence.GetProfileDataAsync(currentUser.AccountId, ct);

        var updateResult = account.UpdateProfile(businessName, timeZone);
        if (updateResult.IsFailure) return Result<KeepSetupResult>.Failure(updateResult.Error);

        var profile = existingProfile ?? KeepBusinessProfile.Create(currentUser.AccountId);
        profile.UpdateContact(customerFacingPhone, customerFacingEmail);

        var identityResult = profile.UpdatePublicIdentity(logoUrl, websiteUrl);
        if (identityResult.IsFailure) return Result<KeepSetupResult>.Failure(identityResult.Error);

        var profileEvent = KeepProductOpsEvent.Record(
            currentUser.AccountId, KeepProductOpsEventType.ProfileAndContactSaved, clock.UtcNow);
        await persistence.SaveProfileAsync(account, profile, profileEvent, ct);

        var policy = await persistence.GetPolicyAsync(currentUser.AccountId, ct);

        return Result<KeepSetupResult>.Success(new KeepSetupResult(
            BusinessName: account.BusinessName,
            TimeZone: account.TimeZone,
            CustomerFacingPhone: profile.CustomerFacingPhone,
            CustomerFacingEmail: profile.CustomerFacingEmail,
            LogoUrl: profile.LogoUrl,
            WebsiteUrl: profile.WebsiteUrl,
            ResponsePolicy: ToPolicy(policy)));
    }

    public async Task<Result<KeepSetupResult>> UpdatePolicyAsync(
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure) return Result<KeepSetupResult>.Failure(auth.Error);

        var existingPolicy = await persistence.GetPolicyAsync(currentUser.AccountId, ct);
        bool isNew;
        KeepResponsePolicy policy;

        if (existingPolicy is null)
        {
            policy = KeepResponsePolicy.Create(
                currentUser.AccountId,
                firstResponseTargetMinutes,
                standardResponseTargetMinutes,
                priorityResponseTargetMinutes,
                statusCheckThresholdDays);
            isNew = true;
        }
        else
        {
            existingPolicy.Update(
                firstResponseTargetMinutes,
                standardResponseTargetMinutes,
                priorityResponseTargetMinutes,
                statusCheckThresholdDays);
            policy = existingPolicy;
            isNew = false;
        }

        var policyEvent = KeepProductOpsEvent.Record(
            currentUser.AccountId, KeepProductOpsEventType.PolicySaved, clock.UtcNow);
        await persistence.SavePolicyAsync(policy, isNew, policyEvent, ct);

        var (account, profile) = await persistence.GetProfileDataAsync(currentUser.AccountId, ct);

        return Result<KeepSetupResult>.Success(new KeepSetupResult(
            BusinessName: account.BusinessName,
            TimeZone: account.TimeZone,
            CustomerFacingPhone: profile?.CustomerFacingPhone,
            CustomerFacingEmail: profile?.CustomerFacingEmail,
            LogoUrl: profile?.LogoUrl,
            WebsiteUrl: profile?.WebsiteUrl,
            ResponsePolicy: ToPolicy(policy)));
    }

    private static KeepSetupPolicyResult ToPolicy(KeepResponsePolicy? policy) =>
        policy is null
            ? new KeepSetupPolicyResult(
                DefaultFirstResponseTargetMinutes,
                DefaultStandardResponseTargetMinutes,
                DefaultPriorityResponseTargetMinutes,
                DefaultStatusCheckThresholdDays)
            : new KeepSetupPolicyResult(
                policy.FirstResponseTargetMinutes,
                policy.StandardResponseTargetMinutes,
                policy.PriorityResponseTargetMinutes,
                policy.StatusCheckThresholdDays);

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
