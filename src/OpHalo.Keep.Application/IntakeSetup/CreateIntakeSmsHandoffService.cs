using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.IntakeSetup;

public sealed record CreateIntakeSmsHandoffCommand(string CustomerPhone);

public sealed record CreateIntakeSmsHandoffResult(
    string RawToken,
    string CustomerPhone,
    string MessageBody,
    DateTime ExpiresAtUtc);

public sealed class CreateIntakeSmsHandoffService(
    IKeepIntakeSmsHandoffPersistence persistence,
    KeepTokenService tokenService,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IOptions<MagicLinkSettings> appSettings,
    IClock clock)
{
    private const int HandoffExpiryMinutes = 15;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly Error NoActiveLink = KeepPublicIntakeLinkErrors.NoActiveLink;

    private static readonly Error NotConfigured =
        Error.Create("App.NotConfigured", "PublicBaseUrl is not configured.");

    public async Task<Result<CreateIntakeSmsHandoffResult>> ExecuteAsync(
        CreateIntakeSmsHandoffCommand command, CancellationToken ct = default)
    {
        var publicBaseUrl = appSettings.Value.PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
            return Result<CreateIntakeSmsHandoffResult>.Failure(NotConfigured);

        var auth = await AuthorizeAsync(ct);
        if (auth.IsFailure)
            return Result<CreateIntakeSmsHandoffResult>.Failure(auth.Error);

        var phoneValidation = ValidatePhone(command.CustomerPhone);
        if (phoneValidation.IsFailure)
            return Result<CreateIntakeSmsHandoffResult>.Failure(phoneValidation.Error);
        var canonicalPhone = phoneValidation.Value;

        var link = await persistence.FindActiveLinkByAccountAsync(currentUser.AccountId, ct);
        if (link is null)
            return Result<CreateIntakeSmsHandoffResult>.Failure(NoActiveLink);

        var messageBody = $"Submit your request here: {publicBaseUrl.TrimEnd('/')}/keep/s/{link.PublicSlug}";

        var rawToken     = tokenService.GeneratePublicIntakeToken();
        var tokenHash    = tokenService.HashPublicIntakeToken(rawToken);
        var nowUtc       = clock.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(HandoffExpiryMinutes);

        var handoff = KeepIntakeSmsHandoff.Create(
            currentUser.AccountId,
            tokenHash,
            canonicalPhone,
            messageBody,
            currentUser.UserId,
            expiresAtUtc);

        await persistence.CreateAsync(handoff, ct);

        return Result<CreateIntakeSmsHandoffResult>.Success(
            new CreateIntakeSmsHandoffResult(rawToken, canonicalPhone, messageBody, expiresAtUtc));
    }

    private static Result<string> ValidatePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return Result<string>.Failure(KeepRequestErrors.CustomerPhoneRequired);

        var trimmed = rawPhone.Trim();
        if (!HasValidPhoneCharacters(trimmed))
            return Result<string>.Failure(KeepRequestErrors.CustomerPhoneInvalidCharacters);

        var canonical = PhoneNormalizer.Normalize(trimmed);
        if (!PhoneNormalizer.IsValidLength(canonical))
            return Result<string>.Failure(KeepRequestErrors.CustomerPhoneInvalidFormat);

        return Result<string>.Success(canonical);
    }

    private static bool HasValidPhoneCharacters(string trimmedPhone)
    {
        for (var i = 0; i < trimmedPhone.Length; i++)
        {
            var c = trimmedPhone[i];
            if (char.IsAsciiDigit(c) || c is ' ' or '-' or '(' or ')' or '.')
                continue;
            if (c == '+' && i == 0)
                continue;
            return false;
        }
        return true;
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
