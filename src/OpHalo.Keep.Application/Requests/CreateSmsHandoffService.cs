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

public sealed record CreateSmsHandoffCommand(Guid RequestId, string MessageBody);

public sealed record CreateSmsHandoffResult(string RawToken, DateTime ExpiresAtUtc);

public sealed class CreateSmsHandoffService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepSmsHandoffPersistence handoffPersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private const int HandoffExpiryMinutes = 15;
    private const int MaxMessageLength = 2000;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error ViewerBlocked =
        Error.Create("KeepRequest.SmsHandoffViewerBlocked", "Viewers cannot create SMS handoff tokens.");

    private static readonly Error OffSeasonBlocked =
        Error.Create("KeepRequest.SmsHandoffOffSeasonBlocked", "SMS handoff is not available while the account is off-season.");

    private static readonly Error MessageBodyRequired =
        Error.Create("KeepRequest.SmsHandoffMessageRequired", "A message body is required.");

    private static readonly Error MessageBodyTooLong =
        Error.Create("KeepRequest.SmsHandoffMessageTooLong", $"Message body must not exceed {MaxMessageLength} characters.");

    private static readonly Error CustomerPhoneMissing =
        Error.Create("KeepRequest.SmsHandoffCustomerPhoneMissing", "This request does not have a customer phone number. SMS handoff requires a phone number.");

    public async Task<Result<CreateSmsHandoffResult>> ExecuteAsync(
        CreateSmsHandoffCommand command, CancellationToken ct = default)
    {
        // --- Auth stack ---
        if (!currentUser.IsAuthenticated)
            return Result<CreateSmsHandoffResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<CreateSmsHandoffResult>.Failure(Forbidden);

        if (userSnapshot.Role is AccountUserRole.Viewer)
            return Result<CreateSmsHandoffResult>.Failure(ViewerBlocked);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<CreateSmsHandoffResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result<CreateSmsHandoffResult>.Failure(Forbidden);

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
            return Result<CreateSmsHandoffResult>.Failure(OffSeasonBlocked);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<CreateSmsHandoffResult>.Failure(Forbidden);

        // --- Input validation (before row load to fail fast on bad input) ---
        var trimmedMessage = command.MessageBody?.Trim() ?? string.Empty;
        if (trimmedMessage.Length == 0)
            return Result<CreateSmsHandoffResult>.Failure(MessageBodyRequired);

        if (trimmedMessage.Length > MaxMessageLength)
            return Result<CreateSmsHandoffResult>.Failure(MessageBodyTooLong);

        // --- Row authorization ---
        var scope = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<CreateSmsHandoffResult>.Failure(KeepRequestErrors.NotFound);

        if (string.IsNullOrWhiteSpace(request.CustomerPhone))
            return Result<CreateSmsHandoffResult>.Failure(CustomerPhoneMissing);

        // --- Token generation --- raw token is 32 random bytes as a lowercase hex string.
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToHexString(rawTokenBytes).ToLowerInvariant();
        var tokenHash = KeepSmsHandoff.HashToken(rawToken);

        var nowUtc = clock.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(HandoffExpiryMinutes);

        var handoff = KeepSmsHandoff.Create(
            tokenHash,
            request.Id,
            request.AccountId,
            request.CustomerPhone,
            trimmedMessage,
            currentUser.UserId,
            expiresAtUtc);

        // Does NOT write request history (Decision 20 — only Mark as Shared creates history).
        await handoffPersistence.CreateAsync(handoff, ct);

        return Result<CreateSmsHandoffResult>.Success(new CreateSmsHandoffResult(rawToken, expiresAtUtc));
    }
}
