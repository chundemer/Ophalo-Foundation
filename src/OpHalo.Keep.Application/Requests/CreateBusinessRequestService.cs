using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Application.Validation;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class CreateBusinessRequestService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepRequestDetailPersistence readPersistence,
    IKeepBusinessRequestPersistence businessRequestPersistence,
    KeepTokenService tokenService,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private const int MaxAttempts = 5;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<KeepRequestDetailResult>> ExecuteAsync(
        CreateBusinessRequestCommand command, CancellationToken ct = default)
    {
        // --- Auth stack ---
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestDetailResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (userSnapshot.Role is AccountUserRole.Viewer)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var isOwnerOrAdmin = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin;

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var nowUtc = clock.UtcNow;
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            nowUtc);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Shared validation pipeline (before actor DB lookup to fail fast on bad input) ---
        var validation = KeepRequestInputValidator.Validate(
            command.CustomerName, command.CustomerPhone, command.CustomerEmail, command.Description);
        if (!validation.IsSuccess)
            return Result<KeepRequestDetailResult>.Failure(validation.Error);

        var v = validation.Value;

        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Customer find or create ---
        var accountId = currentUser.AccountId;
        var customer = await businessRequestPersistence.FindCustomerByCanonicalPhoneAsync(
            accountId, v.CanonicalPhone, ct);
        if (customer is null)
            customer = KeepCustomer.Create(accountId, v.TrimmedName, v.TrimmedPhone, v.TrimmedEmail);
        else
            customer.UpdateContactInfo(v.TrimmedName, v.TrimmedEmail);

        // --- Retry loop: customer-identity and token collisions share the five-attempt ceiling ---
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var pageToken     = tokenService.GeneratePageToken();
            var referenceCode = tokenService.GenerateReferenceCode();

            if (await businessRequestPersistence.PageTokenExistsAsync(pageToken, ct)) continue;
            if (await businessRequestPersistence.ReferenceCodeExistsAsync(accountId, referenceCode, ct)) continue;

            var request = KeepRequest.CreateByBusiness(
                accountId,
                customer.Id,
                v.TrimmedName,
                v.TrimmedPhone,
                v.TrimmedEmail,
                v.TrimmedDescription,
                referenceCode,
                pageToken,
                nowUtc);

            var @event = KeepRequestEvent.CreateRequestCreated(
                request.Id, accountId, currentUser.UserId, actorDisplayName, nowUtc);

            var commitResult = await businessRequestPersistence.CommitBusinessRequestAsync(
                customer, request, @event, ct);

            switch (commitResult)
            {
                case BusinessRequestCommitResult.Committed:
                    return Result<KeepRequestDetailResult>.Success(
                        await BuildDetailResultAsync(request, userSnapshot.Role, isOwnerOrAdmin, nowUtc, ct));

                case BusinessRequestCommitResult.UniqueTokenCollision:
                    continue;

                case BusinessRequestCommitResult.CustomerCanonicalPhoneCollision:
                    customer = await businessRequestPersistence.FindCustomerByCanonicalPhoneAsync(
                        accountId, v.CanonicalPhone, ct)
                        ?? throw new InvalidOperationException(
                            "Expected a customer after canonical-phone collision but none was found.");
                    customer.UpdateContactInfo(v.TrimmedName, v.TrimmedEmail);
                    continue;

                default:
                    throw new InvalidOperationException(
                        $"Unexpected BusinessRequestCommitResult value: {commitResult}");
            }
        }

        throw new InvalidOperationException(
            $"Failed to commit business request after {MaxAttempts} attempts.");
    }

    private async Task<KeepRequestDetailResult> BuildDetailResultAsync(
        KeepRequest request, AccountUserRole role, bool isOwnerOrAdmin, DateTime nowUtc, CancellationToken ct)
    {
        var events       = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        var availableActions = new AvailableActionsMetadata(
            CanChangeStatus:         true,
            CanSendBusinessUpdate:   true,
            CanAddInternalNote:      true,
            CanAcknowledgeAttention: KeepRequestDetailMapper.CanAcknowledgeAttention(canOperate: true, request),
            CanLogExternalContact:   true,
            CanAssignResponsible:    isOwnerOrAdmin,
            CanWatch:                true,
            CanUnwatch:              false,
            CanMute:                 false,
            CanUnmute:               false,
            CanMarkFeedbackReviewed: false,
            AllowedStatuses:         KeepRequestDetailMapper.ComputeAllowedStatuses(request.Status));

        return KeepRequestDetailMapper.ToDetailResult(
            request, businessName ?? string.Empty, participants, events, availableActions,
            role, canOperate: true, currentUser.UserId, nowUtc);
    }
}
