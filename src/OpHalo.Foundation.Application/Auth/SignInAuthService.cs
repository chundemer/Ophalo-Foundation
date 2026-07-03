using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Issues a magic link for existing active members.
///
/// Behavior (D8): unknown email, invited-only, suspended/removed membership, or any
/// other ineligible state all return Result.Success with no code issued and no email
/// sent — enumeration protection. These are expected outcomes, not errors.
///
/// Email delivery (D4): direct IEmailSender, best-effort. Provider failure must not
/// change the public response — the code is already persisted and the member can retry.
/// </summary>
public sealed class SignInAuthService(
    IAuthCodePersistence persistence,
    IEmailSender emailSender,
    IClock clock,
    IOptions<MagicLinkSettings> settings,
    ILogger<SignInAuthService> logger)
{
    public async Task<Result> HandleAsync(string email, string? clientHint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Value.PublicBaseUrl))
            return Result.Failure(Error.Create("App.NotConfigured", "PublicBaseUrl is not configured."));

        var nowUtc = clock.UtcNow;
        var normalizedEmail = EmailNormalizer.Normalize(email);

        var member = await persistence.FindEligibleSignInMemberByEmailAsync(normalizedEmail, cancellationToken);

        // Unknown/ineligible email — neutral success, no code issued (D8).
        if (member is null)
            return Result.Success();

        var rawCode = MagicLinkCodeGenerator.GenerateRawCode();
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode);

        var code = AccountAuthCode.Create(
            accountId: member.AccountId,
            targetAccountUserId: member.AccountUserId,
            codeHash: codeHash,
            issuedAtUtc: nowUtc,
            expiresAtUtc: nowUtc.AddHours(24),
            deliveryEmailSnapshot: normalizedEmail,
            entryContext: EntryContext.ExistingMember);

        // Atomic: invalidates prior codes for this AccountUser + persists the new code.
        await persistence.CommitSignInCodeAsync(code, cancellationToken);

        var mobileSuffix = string.Equals(clientHint, "mobile", StringComparison.OrdinalIgnoreCase)
            ? "&from=mobile"
            : string.Empty;
        var magicLink = $"{settings.Value.PublicBaseUrl}/auth/exchange?code={rawCode}{mobileSuffix}";

        // Best-effort — delivery failure must not change the public response (D4).
        // Non-cancellation provider exceptions are caught so enumeration protection
        // is preserved even when the email transport throws.
        try
        {
            await emailSender.SendAsync(
                normalizedEmail,
                MagicLinkEmailTemplate.Subject,
                MagicLinkEmailTemplate.BuildHtmlBody(magicLink),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Magic link email delivery failed for code {CodeId}.", code.Id);
        }

        return Result.Success();
    }
}
