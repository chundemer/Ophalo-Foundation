using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Application.Validation;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PublicIntake;

public sealed class CreateKeepPublicIntakeService(
    IKeepIntakePersistence persistence,
    KeepTokenService tokenService,
    IAccountAccessPolicy accessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock,
    ICurrentUser currentUser)
{
    private const int MaxAttempts = 5;

    private static readonly Error Unavailable =
        Error.Create("keep.public_intake.unavailable", "This intake form is not currently available.");

    private static readonly Error StaffNotPermitted =
        Error.Create("keep.public_intake.staff_not_permitted",
            "Staff members must use the app to submit requests, not the public intake form.");

    private static readonly HashSet<string> ValidUsStateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA",
        "HI","ID","IL","IN","IA","KS","KY","LA","ME","MD",
        "MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
        "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC",
        "SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC"
    };

    public async Task<Result<CreateKeepPublicIntakeResult>> ExecuteAsync(
        CreateKeepPublicIntakeCommand command, CancellationToken ct = default)
    {
        var validation = KeepRequestInputValidator.Validate(
            command.CustomerName, command.CustomerPhone, command.CustomerEmail, command.Description);
        if (!validation.IsSuccess)
            return Result<CreateKeepPublicIntakeResult>.Failure(validation.Error);

        var locationResult = ValidateServiceLocation(command);
        if (!locationResult.IsSuccess)
            return Result<CreateKeepPublicIntakeResult>.Failure(locationResult.Error);

        var v = validation.Value;

        if (string.IsNullOrWhiteSpace(command.PublicIntakeToken))
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        var tokenHash = tokenService.HashPublicIntakeToken(command.PublicIntakeToken);
        var link = await persistence.FindActivePublicIntakeLinkByTokenHashAsync(tokenHash, ct);
        if (link is null || !link.IsActive)
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        return await ExecuteWithLinkAsync(link, v, command, ct);
    }

    public async Task<Result<CreateKeepPublicIntakeResult>> ExecuteBySlugAsync(
        string slug, CreateKeepPublicIntakeCommand command, CancellationToken ct = default)
    {
        var validation = KeepRequestInputValidator.Validate(
            command.CustomerName, command.CustomerPhone, command.CustomerEmail, command.Description);
        if (!validation.IsSuccess)
            return Result<CreateKeepPublicIntakeResult>.Failure(validation.Error);

        var locationResult = ValidateServiceLocation(command);
        if (!locationResult.IsSuccess)
            return Result<CreateKeepPublicIntakeResult>.Failure(locationResult.Error);

        var v = validation.Value;

        if (string.IsNullOrWhiteSpace(slug))
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        var link = await persistence.FindActivePublicIntakeLinkBySlugAsync(slug, ct);
        if (link is null || !link.IsActive)
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        return await ExecuteWithLinkAsync(link, v, command, ct);
    }

    public async Task<string?> GetInfoByTokenAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var tokenHash = tokenService.HashPublicIntakeToken(rawToken);
        return await persistence.GetBusinessNameByTokenHashAsync(tokenHash, ct);
    }

    public async Task<string?> GetInfoBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        return await persistence.GetBusinessNameBySlugAsync(slug, ct);
    }

    private static Result<bool> ValidateServiceLocation(CreateKeepPublicIntakeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ServiceAddressLine1))
            return Result<bool>.Failure(KeepRequestErrors.ServiceAddressLine1Required);
        if (string.IsNullOrWhiteSpace(command.ServiceCity))
            return Result<bool>.Failure(KeepRequestErrors.ServiceCityRequired);
        if (string.IsNullOrWhiteSpace(command.ServiceState))
            return Result<bool>.Failure(KeepRequestErrors.ServiceStateRequired);
        if (!ValidUsStateCodes.Contains(command.ServiceState))
            return Result<bool>.Failure(KeepRequestErrors.ServiceStateInvalid);
        return Result<bool>.Success(true);
    }

    private async Task<Result<CreateKeepPublicIntakeResult>> ExecuteWithLinkAsync(
        KeepPublicIntakeLink link,
        ValidatedKeepRequestInput v,
        CreateKeepPublicIntakeCommand command,
        CancellationToken ct)
    {
        var accountId = link.AccountId;

        // Block same-account staff: public intake is a customer channel only.
        if (currentUser.IsAuthenticated && currentUser.AccountId == accountId)
            return Result<CreateKeepPublicIntakeResult>.Failure(StaffNotPermitted);

        var snapshot = await persistence.GetAccountAccessSnapshotAsync(accountId, ct);
        if (snapshot is null)
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        var nowUtc = clock.UtcNow;

        var accessContext = new AccountAccessContext(
            snapshot.LifecycleState,
            snapshot.Purpose,
            snapshot.CommercialState,
            snapshot.TrialEndsAtUtc,
            snapshot.PastDueGraceEndsAtUtc,
            snapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            nowUtc);

        var decision = accessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        if (!featurePolicy.IsEnabled(snapshot.Plan, FeatureKeys.Keep.PublicIntake))
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        var policy = await persistence.GetResponsePolicyAsync(accountId, ct);
        var firstResponseTargetMinutes = policy?.FirstResponseTargetMinutes ?? 60;

        var customer = await persistence.FindCustomerByCanonicalPhoneAsync(accountId, v.CanonicalPhone, ct);
        if (customer is null)
            customer = KeepCustomer.Create(accountId, v.TrimmedName, v.TrimmedPhone, v.TrimmedEmail);
        else
            customer.UpdateContactInfo(v.TrimmedName, v.TrimmedEmail);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var pageToken = tokenService.GeneratePageToken();
            var referenceCode = tokenService.GenerateReferenceCode();

            if (await persistence.PageTokenExistsAsync(pageToken, ct)) continue;
            if (await persistence.ReferenceCodeExistsAsync(accountId, referenceCode, ct)) continue;

            var request = KeepRequest.CreateFromCustomerIntake(
                accountId, customer.Id,
                v.TrimmedName, v.TrimmedPhone, v.TrimmedEmail,
                v.TrimmedDescription, referenceCode, pageToken, nowUtc, firstResponseTargetMinutes,
                command.ServiceAddressLine1, command.ServiceAddressLine2,
                command.ServiceCity, command.ServiceState, command.ServiceZip,
                command.IntakeUrgency, command.ContactPreference);
            var @event = KeepRequestEvent.CreateRequestCreated(request.Id, accountId, nowUtc);

            var commitResult = await persistence.CommitPublicIntakeAsync(customer, request, @event, ct);
            switch (commitResult)
            {
                case PublicIntakeCommitResult.Committed:
                    return Result<CreateKeepPublicIntakeResult>.Success(
                        new CreateKeepPublicIntakeResult(request.Id, referenceCode, pageToken));

                case PublicIntakeCommitResult.UniqueTokenCollision:
                    continue;

                case PublicIntakeCommitResult.CustomerCanonicalPhoneCollision:
                    customer = await persistence.FindCustomerByCanonicalPhoneAsync(accountId, v.CanonicalPhone, ct)
                        ?? throw new InvalidOperationException(
                            "Expected a customer after canonical-phone collision but none was found.");
                    customer.UpdateContactInfo(v.TrimmedName, v.TrimmedEmail);
                    continue;

                default:
                    throw new InvalidOperationException(
                        $"Unexpected PublicIntakeCommitResult value: {commitResult}");
            }
        }

        throw new InvalidOperationException(
            $"Failed to commit public intake after {MaxAttempts} attempts.");
    }
}
