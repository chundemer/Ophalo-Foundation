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

    private static readonly Error SourceRequired =
        Error.Create("KeepRequest.SourceRequired", "Source is required for business-created requests.");

    private static readonly Error InvalidSource =
        Error.Create("KeepRequest.InvalidSource", "The provided source is not valid.");

    private static readonly Error SourceCannotBePublicIntake =
        Error.Create("KeepRequest.SourceCannotBePublicIntake", "Staff cannot select Public Intake as a source.");

    private static readonly Error ServiceAddressIncomplete =
        Error.Create("KeepRequest.ServiceAddressIncomplete",
            "Address line 1, city, and state are required when any address field is provided.");

    private static readonly Error ServiceStateInvalid =
        Error.Create("KeepRequest.ServiceStateInvalid", "State must be a valid two-letter US state code.");

    private static readonly HashSet<string> ValidUsStateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA",
        "HI","ID","IL","IN","IA","KS","KY","LA","ME","MD",
        "MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
        "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC",
        "SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC"
    };

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

        if (string.IsNullOrWhiteSpace(command.Source))
            return Result<KeepRequestDetailResult>.Failure(SourceRequired);
        if (!TryParseSourceSlug(command.Source, out var parsedSource))
            return Result<KeepRequestDetailResult>.Failure(
                command.Source == "public_intake" ? SourceCannotBePublicIntake : InvalidSource);

        // --- Optional service address: if any field supplied, line1 + city + state are required ---
        string? normalizedState = null;
        var anyAddress = !string.IsNullOrWhiteSpace(command.ServiceAddressLine1)
            || !string.IsNullOrWhiteSpace(command.ServiceAddressLine2)
            || !string.IsNullOrWhiteSpace(command.ServiceCity)
            || !string.IsNullOrWhiteSpace(command.ServiceState)
            || !string.IsNullOrWhiteSpace(command.ServiceZip);

        if (anyAddress)
        {
            if (string.IsNullOrWhiteSpace(command.ServiceAddressLine1)
                || string.IsNullOrWhiteSpace(command.ServiceCity)
                || string.IsNullOrWhiteSpace(command.ServiceState))
                return Result<KeepRequestDetailResult>.Failure(ServiceAddressIncomplete);

            normalizedState = command.ServiceState.Trim().ToUpperInvariant();
            if (!ValidUsStateCodes.Contains(normalizedState))
                return Result<KeepRequestDetailResult>.Failure(ServiceStateInvalid);
        }

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
                nowUtc,
                parsedSource,
                serviceAddressLine1: command.ServiceAddressLine1,
                serviceAddressLine2: command.ServiceAddressLine2,
                serviceCity:         command.ServiceCity,
                serviceState:        normalizedState,
                serviceZip:          command.ServiceZip);

            var @event = KeepRequestEvent.CreateRequestCreated(
                request.Id, accountId, currentUser.UserId, actorDisplayName, nowUtc);

            var commitResult = await businessRequestPersistence.CommitBusinessRequestAsync(
                customer, request, @event, ct);

            switch (commitResult)
            {
                case BusinessRequestCommitResult.Committed:
                    return Result<KeepRequestDetailResult>.Success(
                        await BuildDetailResultAsync(request, userSnapshot.Role, nowUtc, ct));

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
        KeepRequest request, AccountUserRole role, DateTime nowUtc, CancellationToken ct)
    {
        var events       = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        // OffSeason and Viewer are rejected upstream; canWrite is true at this point.
        // The creator has no participation yet on the newly committed request.
        var actorContext = new KeepRequestActionContext(
            Role:                role,
            CanWrite:            true,
            ActiveParticipation: null,
            NotificationsEnabled: null);

        var actionDecision   = KeepRequestActionPolicy.Evaluate(request, actorContext);
        var availableActions = KeepRequestDetailMapper.ToAvailableActionsMetadata(actionDecision);

        return KeepRequestDetailMapper.ToDetailResult(
            request, businessName ?? string.Empty, participants, events, availableActions,
            role, canOperate: true, currentUser.UserId, nowUtc);
    }

    // Accepts only the API slug forms that staff may supply. public_intake is excluded
    // so the caller can distinguish it from an unknown slug and return the right error.
    private static bool TryParseSourceSlug(string slug, out KeepRequestSource result)
    {
        result = slug switch
        {
            "phone"        => KeepRequestSource.Phone,
            "voicemail"    => KeepRequestSource.Voicemail,
            "text"         => KeepRequestSource.Text,
            "email"        => KeepRequestSource.Email,
            "walk_in"      => KeepRequestSource.WalkIn,
            "referral"     => KeepRequestSource.Referral,
            "other"        => KeepRequestSource.Other,
            _              => (KeepRequestSource)0
        };
        return result != (KeepRequestSource)0;
    }
}
