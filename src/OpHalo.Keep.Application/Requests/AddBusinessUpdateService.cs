using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed record AddBusinessUpdateCommand(
    Guid RequestId,
    string Message,
    string? SetStatus);

public sealed class AddBusinessUpdateService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepRequestDetailPersistence readPersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<KeepRequestDetailResult>> ExecuteAsync(
        AddBusinessUpdateCommand command, CancellationToken ct = default)
    {
        // --- Auth stack ---
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestDetailResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

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
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Parse optional setStatus ---
        KeepRequestStatus? parsedSetStatus = null;
        if (command.SetStatus is not null)
        {
            parsedSetStatus = KeepRequestDetailMapper.ParseStatusSlug(command.SetStatus);
            if (parsedSetStatus is null)
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.InvalidStatus);
        }

        // --- Actor display name (denormalized onto the event) ---
        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Load request for mutation ---
        var request = await operatePersistence.GetRequestForUpdateAsync(command.RequestId, currentUser.AccountId, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        // --- Domain: apply business update ---
        // setStatus present → combined StatusChanged+message event (D3/D4, 4000-char limit).
        // setStatus absent  → standalone MessageAdded event (D4, 4000-char limit).
        // All validation (null/blank, length, terminal, transition) lives in the domain methods.
        var updateResult = parsedSetStatus.HasValue
            ? request.AddBusinessUpdateWithStatus(
                parsedSetStatus.Value, command.Message, currentUser.UserId, actorDisplayName, clock.UtcNow)
            : request.AddBusinessUpdate(
                command.Message, currentUser.UserId, actorDisplayName, clock.UtcNow);

        if (updateResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(updateResult.Error);

        await operatePersistence.CommitAsync(request, updateResult.Value, ct);

        // --- Load read data for the response ---
        var events = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        // canOperate confirmed true (passed the gate above).
        var availableActions = new AvailableActionsMetadata(
            CanChangeStatus: !request.IsTerminal,
            CanSendBusinessUpdate: !request.IsTerminal,
            CanAddInternalNote: true,
            CanAcknowledgeAttention: KeepRequestDetailMapper.CanAcknowledgeAttention(true, request),
            AllowedStatuses: !request.IsTerminal
                ? KeepRequestDetailMapper.ComputeAllowedStatuses(request.Status)
                : []);

        return Result<KeepRequestDetailResult>.Success(
            KeepRequestDetailMapper.ToDetailResult(
                request, businessName ?? string.Empty, participants, events, availableActions));
    }
}
