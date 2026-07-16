using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.IntakeSetup;

public sealed record CreateIntakeSmsHandoffCommand(string AppBaseUrl);

public sealed record CreateIntakeSmsHandoffResult(string RawToken, DateTime ExpiresAtUtc);

public sealed class CreateIntakeSmsHandoffService(
    IKeepIntakeSmsHandoffPersistence persistence,
    KeepTokenService tokenService,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private const int HandoffExpiryMinutes = 15;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error NoActiveLink = KeepPublicIntakeLinkErrors.NoActiveLink;

    public async Task<Result<CreateIntakeSmsHandoffResult>> ExecuteAsync(
        CreateIntakeSmsHandoffCommand command, CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure)
            return Result<CreateIntakeSmsHandoffResult>.Failure(auth.Error);

        var link = await persistence.FindActiveLinkByAccountAsync(currentUser.AccountId, ct);
        if (link is null)
            return Result<CreateIntakeSmsHandoffResult>.Failure(NoActiveLink);

        var baseUrl = command.AppBaseUrl.TrimEnd('/');
        var messageBody = $"Submit your request here: {baseUrl}/keep/{link.PublicSlug}";

        var rawToken    = tokenService.GeneratePublicIntakeToken();
        var tokenHash   = tokenService.HashPublicIntakeToken(rawToken);
        var nowUtc      = clock.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(HandoffExpiryMinutes);

        var handoff = KeepIntakeSmsHandoff.Create(
            currentUser.AccountId,
            tokenHash,
            messageBody,
            currentUser.UserId,
            expiresAtUtc);

        await persistence.CreateAsync(handoff, ct);

        return Result<CreateIntakeSmsHandoffResult>.Success(new CreateIntakeSmsHandoffResult(rawToken, expiresAtUtc));
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

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.PublicIntake))
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
