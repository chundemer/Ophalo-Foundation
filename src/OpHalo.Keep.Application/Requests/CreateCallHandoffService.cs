using System.Security.Cryptography;
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

public sealed record CreateCallHandoffCommand(Guid RequestId);

public sealed record CreateCallHandoffResult(string RawToken, DateTime ExpiresAtUtc);

public sealed class CreateCallHandoffService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepCallHandoffPersistence handoffPersistence,
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

    private static readonly Error ViewerBlocked =
        Error.Create("KeepRequest.CallHandoffViewerBlocked", "Viewers cannot create call handoff tokens.");

    private static readonly Error OffSeasonBlocked =
        Error.Create("KeepRequest.CallHandoffOffSeasonBlocked", "Call handoff is not available while the account is off-season.");

    private static readonly Error CustomerPhoneMissing =
        Error.Create("KeepRequest.CallHandoffCustomerPhoneMissing", "This request does not have a customer phone number. Call handoff requires a phone number.");

    public async Task<Result<CreateCallHandoffResult>> ExecuteAsync(
        CreateCallHandoffCommand command, CancellationToken ct = default)
    {
        // --- Auth stack ---
        if (!currentUser.IsAuthenticated)
            return Result<CreateCallHandoffResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<CreateCallHandoffResult>.Failure(Forbidden);

        if (userSnapshot.Role is AccountUserRole.Viewer)
            return Result<CreateCallHandoffResult>.Failure(ViewerBlocked);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<CreateCallHandoffResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result<CreateCallHandoffResult>.Failure(Forbidden);

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
            return Result<CreateCallHandoffResult>.Failure(OffSeasonBlocked);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<CreateCallHandoffResult>.Failure(Forbidden);

        // --- Row authorization ---
        var scope = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<CreateCallHandoffResult>.Failure(KeepRequestErrors.NotFound);

        if (string.IsNullOrWhiteSpace(request.CustomerPhone))
            return Result<CreateCallHandoffResult>.Failure(CustomerPhoneMissing);

        // --- Token generation --- raw token is 32 random bytes as a lowercase hex string.
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToHexString(rawTokenBytes).ToLowerInvariant();
        var tokenHash = KeepCallHandoff.HashToken(rawToken);

        var nowUtc = clock.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(HandoffExpiryMinutes);

        var handoff = KeepCallHandoff.Create(
            tokenHash,
            request.Id,
            request.AccountId,
            request.CustomerPhone,
            currentUser.UserId,
            expiresAtUtc);

        // Does NOT write request history and does NOT log an external contact — scanning or
        // launching the call handoff is not proof a call happened (ADR-448).
        await handoffPersistence.CreateAsync(handoff, ct);

        return Result<CreateCallHandoffResult>.Success(new CreateCallHandoffResult(rawToken, expiresAtUtc));
    }
}
