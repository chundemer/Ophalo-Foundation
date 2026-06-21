using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Application.Validation;
using OpHalo.Keep.Core.Entities;
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
        // --- Shared validation pipeline ---
        var validation = KeepRequestInputValidator.Validate(
            command.CustomerName, command.CustomerPhone, command.CustomerEmail, command.Description);
        if (!validation.IsSuccess)
            return Result<CreateKeepPublicIntakeResult>.Failure(validation.Error);

        var v = validation.Value;

        // --- Token and account gate (collapses to Unavailable; never exposes validation state) ---
        // HashPublicIntakeToken throws ArgumentException on null/whitespace; guard first.
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

        // Load existing customer or seed a new one. UpdateContactInfo preserves the existing email
        // when the incoming value is null/blank (anonymous omission is not a clear-email command).
        var customer = await persistence.FindCustomerByCanonicalPhoneAsync(accountId, v.CanonicalPhone, ct);
        if (customer is null)
            customer = KeepCustomer.Create(accountId, v.TrimmedName, v.TrimmedPhone, v.TrimmedEmail);
        else
            customer.UpdateContactInfo(v.TrimmedName, v.TrimmedEmail);

        // Retry loop: customer-identity and token collisions share the five-attempt ceiling.
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var pageToken = tokenService.GeneratePageToken();
            var referenceCode = tokenService.GenerateReferenceCode();

            if (await persistence.PageTokenExistsAsync(pageToken, ct)) continue;
            if (await persistence.ReferenceCodeExistsAsync(accountId, referenceCode, ct)) continue;

            var request = KeepRequest.CreateFromCustomerIntake(
                accountId, customer.Id,
                v.TrimmedName, v.TrimmedPhone, v.TrimmedEmail,
                v.TrimmedDescription, referenceCode, pageToken, nowUtc, firstResponseTargetMinutes);
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
                    // A concurrent submission won the customer insert. Re-read the winning customer,
                    // apply safe contact-update rules, and retry request/event persistence.
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
