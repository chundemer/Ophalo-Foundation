using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Services;
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
    IClock clock)
{
    private const int MaxAttempts = 5;

    private static readonly Error Unavailable =
        Error.Create("keep.public_intake.unavailable", "This intake form is not currently available.");

    public async Task<Result<CreateKeepPublicIntakeResult>> ExecuteAsync(
        CreateKeepPublicIntakeCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerName))
            return Result<CreateKeepPublicIntakeResult>.Failure(KeepRequestErrors.CustomerNameRequired);
        if (string.IsNullOrWhiteSpace(command.CustomerPhone))
            return Result<CreateKeepPublicIntakeResult>.Failure(KeepRequestErrors.CustomerPhoneRequired);
        if (string.IsNullOrWhiteSpace(command.Description))
            return Result<CreateKeepPublicIntakeResult>.Failure(KeepRequestErrors.DescriptionRequired);

        // HashPublicIntakeToken throws ArgumentException on null/whitespace; a blank token must
        // return the public-safe Unavailable error, not a 500.
        if (string.IsNullOrWhiteSpace(command.PublicIntakeToken))
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        var tokenHash = tokenService.HashPublicIntakeToken(command.PublicIntakeToken);
        var link = await persistence.FindActivePublicIntakeLinkByTokenHashAsync(tokenHash, ct);
        if (link is null || !link.IsActive)
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        var accountId = link.AccountId;
        var snapshot = await persistence.GetAccountAccessSnapshotAsync(accountId, ct);
        if (snapshot is null)
            return Result<CreateKeepPublicIntakeResult>.Failure(Unavailable);

        var nowUtc = clock.UtcNow;

        // Intake is a write — OffSeason (ReadOnly posture) must block it.
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

        var primaryPhone = command.CustomerPhone.Trim();
        var customer = await persistence.FindCustomerByPrimaryPhoneAsync(accountId, primaryPhone, ct);
        if (customer is null)
            customer = KeepCustomer.Create(accountId, command.CustomerName, primaryPhone, command.CustomerEmail?.Trim());
        else
            customer.UpdateContactInfo(command.CustomerName, command.CustomerEmail?.Trim());

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var pageToken = tokenService.GeneratePageToken();
            var referenceCode = tokenService.GenerateReferenceCode();

            if (await persistence.PageTokenExistsAsync(pageToken, ct)) continue;
            if (await persistence.ReferenceCodeExistsAsync(accountId, referenceCode, ct)) continue;

            var request = KeepRequest.Create(
                accountId,
                customer.Id,
                command.CustomerName,
                primaryPhone,
                command.CustomerEmail?.Trim(),
                command.Description,
                referenceCode,
                pageToken,
                nowUtc);
            var @event = KeepRequestEvent.CreateRequestCreated(request.Id, accountId, nowUtc);

            var commitResult = await persistence.CommitPublicIntakeAsync(customer, request, @event, ct);
            switch (commitResult)
            {
                case PublicIntakeCommitResult.Committed:
                    return Result<CreateKeepPublicIntakeResult>.Success(
                        new CreateKeepPublicIntakeResult(request.Id, referenceCode, pageToken));
                case PublicIntakeCommitResult.UniqueTokenCollision:
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
