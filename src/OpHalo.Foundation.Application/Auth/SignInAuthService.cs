using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Issues a magic link for existing active members, including the 2+-active-membership case
/// (workspace selection deferred to /exchange, a later slice — the code itself carries no
/// membership-count signal).
///
/// Behavior (D8): unknown email, invited-only, suspended/removed membership, or any
/// other ineligible state all return Result.Success with no code issued and no email
/// sent — enumeration protection. These are expected outcomes, not errors.
///
/// Email delivery (D4): enqueued via IMagicLinkDispatchQueue, sent out of band by
/// MagicLinkDispatchBackgroundService (GAP-095 095-1). Best-effort — a dropped or failed send
/// must not change the public response; the code is already persisted and the member can retry.
/// </summary>
public sealed class SignInAuthService(
    IAuthCodePersistence persistence,
    IAuthIssuanceThrottle issuanceThrottle,
    IMagicLinkDispatchQueue dispatchQueue,
    IClock clock,
    IOptions<MagicLinkSettings> settings)
{
    // Shared with StartAuthService (GAP-094/BL154): one recipient allowance across both
    // issuance endpoints, so a caller can't bypass the cap by alternating /start and /signin.
    private const int RecipientPermitLimit = 3;
    private static readonly TimeSpan RecipientWindow = TimeSpan.FromMinutes(15);

    public async Task<Result> HandleAsync(string email, string? clientHint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Value.PublicBaseUrl))
            return Result.Failure(Error.Create("App.NotConfigured", "PublicBaseUrl is not configured."));

        var nowUtc = clock.UtcNow;
        var normalizedEmail = EmailNormalizer.Normalize(email);

        // Acquired before classification/code issuance/email dispatch (BL154) — a denied
        // recipient never reaches persistence or IEmailSender, and below-cap neutral outcomes
        // (unknown/ineligible email) are unaffected, preserving the D8 enumeration contract.
        var allowed = await issuanceThrottle.TryAcquireAsync(
            [new AuthIssuanceThrottleRequest(
                AuthIssuanceThrottleScopes.Recipient, normalizedEmail, RecipientPermitLimit, RecipientWindow)],
            cancellationToken);
        if (!allowed)
            return Result.Failure(Error.Create("Auth.IssuanceRateLimited", "Too many attempts. Try again later."));

        var classification = await persistence.FindEligibleSignInMemberByEmailAsync(normalizedEmail, cancellationToken);

        // Unknown/ineligible email — neutral success, no code issued (D8).
        if (classification is SignInAsNeutral)
            return Result.Success();

        var rawCode = MagicLinkCodeGenerator.GenerateRawCode();
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode);

        var code = classification switch
        {
            SignInAsExistingMember existing => AccountAuthCode.Create(
                accountId: existing.AccountId,
                targetAccountUserId: existing.AccountUserId,
                codeHash: codeHash,
                issuedAtUtc: nowUtc,
                expiresAtUtc: nowUtc.AddHours(24),
                deliveryEmailSnapshot: normalizedEmail,
                entryContext: EntryContext.ExistingMember),
            SignInAsMultipleMembers => AccountAuthCode.CreateForMultipleMembers(
                codeHash: codeHash,
                issuedAtUtc: nowUtc,
                expiresAtUtc: nowUtc.AddHours(24),
                deliveryEmailSnapshot: normalizedEmail),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(SignInClassification)}: {classification.GetType().Name}."),
        };

        // Atomic: invalidates prior codes for this classification + persists the new code.
        await persistence.CommitSignInCodeAsync(code, cancellationToken);

        var mobileSuffix = string.Equals(clientHint, "mobile", StringComparison.OrdinalIgnoreCase)
            ? "&from=mobile"
            : string.Empty;
        var magicLink = $"{settings.Value.PublicBaseUrl}/auth/exchange?code={rawCode}{mobileSuffix}";

        // Best-effort — delivery failure must not change the public response (D4). Enqueued
        // rather than awaited (GAP-095 095-1) so provider latency cannot distinguish issuance
        // outcomes; enumeration protection no longer depends on catching transport exceptions
        // here at all, since the send happens off the request path.
        dispatchQueue.Enqueue(new MagicLinkDispatchItem(
            code.Id,
            normalizedEmail,
            MagicLinkEmailTemplate.Subject,
            MagicLinkEmailTemplate.BuildHtmlBody(magicLink),
            MagicLinkEmailTemplate.BuildTextBody(magicLink),
            LogContext: "Magic link"));

        return Result.Success();
    }
}
