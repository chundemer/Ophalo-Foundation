using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Handles POST /auth/start: new-account registration with existing-member fallback.
///
/// Classification (D5/D6):
/// - Exactly one active member → issue ExistingMember code (same as /auth/signin).
/// - 2+ active members across accounts → issue MultipleMembers code (workspace selection
///   deferred to /exchange, a later slice).
/// - No existing identity → issue NewAccount code with business-name/time-zone snapshots.
/// - Any other state (invited, suspended, removed, existing User without active
///   membership) → neutral 200, no code issued (enumeration protection).
///
/// Pilot cap (D3): when Classification=Pilot and MaxPilotAccounts is set, check capacity
/// before issuing a NewAccount code. Pilot-full returns a non-neutral 409 — the caller
/// may prompt the user to join a waitlist.
///
/// Email delivery (D8): enqueued via IMagicLinkDispatchQueue, sent out of band by
/// MagicLinkDispatchBackgroundService (GAP-095 095-1) — never awaited on the request path, so
/// provider latency cannot distinguish issuance outcomes. Best-effort: a dropped or failed
/// send never changes the public response.
///
/// Logging (D9): log only safe IDs. Do not log email, business name, name, raw codes,
/// or magic-link URLs.
/// </summary>
public sealed class StartAuthService(
    IAuthCodePersistence persistence,
    IAuthIssuanceThrottle issuanceThrottle,
    IMagicLinkDispatchQueue dispatchQueue,
    IClock clock,
    IOptions<MagicLinkSettings> magicLinkSettings,
    IOptions<SignupDefaultsSettings> signupDefaults)
{
    // Shared with SignInAuthService (GAP-094/BL154): one recipient allowance across both
    // issuance endpoints, so a caller can't bypass the cap by alternating /start and /signin.
    private const int RecipientPermitLimit = 3;
    private static readonly TimeSpan RecipientWindow = TimeSpan.FromMinutes(15);

    public async Task<Result> HandleAsync(
        string email,
        string businessName,
        string? name,
        string timeZone,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(magicLinkSettings.Value.PublicBaseUrl))
            return Result.Failure(Error.Create("App.NotConfigured", "PublicBaseUrl is not configured."));

        var nowUtc = clock.UtcNow;
        var normalizedEmail = EmailNormalizer.Normalize(email);
        var defaults = signupDefaults.Value;

        // Acquired before classification/code issuance/email dispatch (BL154) — a denied
        // recipient never reaches persistence or IEmailSender, and below-cap neutral outcomes
        // (unknown/ineligible email) are unaffected, preserving the D8 enumeration contract.
        var allowed = await issuanceThrottle.TryAcquireAsync(
            [new AuthIssuanceThrottleRequest(
                AuthIssuanceThrottleScopes.Recipient, normalizedEmail, RecipientPermitLimit, RecipientWindow)],
            cancellationToken);
        if (!allowed)
            return Result.Failure(Error.Create("Auth.IssuanceRateLimited", "Too many attempts. Try again later."));

        var classification = await persistence.ClassifyStartRequestAsync(normalizedEmail, cancellationToken);

        switch (classification)
        {
            case StartAsNeutral:
                return Result.Success();

            case StartAsExistingMember existing:
                return await IssueExistingMemberCodeAsync(
                    existing.AccountId, existing.AccountUserId,
                    normalizedEmail, nowUtc, cancellationToken);

            case StartAsMultipleMembers:
                return await IssueMultipleMembersCodeAsync(normalizedEmail, nowUtc, cancellationToken);

            case StartAsNewAccount:
                return await IssueNewAccountCodeAsync(
                    normalizedEmail, businessName, name, timeZone,
                    nowUtc, defaults, cancellationToken);

            default:
                return Result.Success();
        }
    }

    private async Task<Result> IssueExistingMemberCodeAsync(
        Guid accountId,
        Guid accountUserId,
        string normalizedEmail,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var rawCode = MagicLinkCodeGenerator.GenerateRawCode();
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode);

        var code = AccountAuthCode.Create(
            accountId: accountId,
            targetAccountUserId: accountUserId,
            codeHash: codeHash,
            issuedAtUtc: nowUtc,
            expiresAtUtc: nowUtc.AddHours(24),
            deliveryEmailSnapshot: normalizedEmail,
            entryContext: EntryContext.ExistingMember);

        await persistence.CommitStartCodeAsync(code, cancellationToken);

        var magicLink = $"{magicLinkSettings.Value.PublicBaseUrl}/auth/exchange?code={rawCode}";

        dispatchQueue.Enqueue(new MagicLinkDispatchItem(
            code.Id,
            normalizedEmail,
            MagicLinkEmailTemplate.Subject,
            MagicLinkEmailTemplate.BuildHtmlBody(magicLink),
            MagicLinkEmailTemplate.BuildTextBody(magicLink),
            LogContext: "Magic link"));

        return Result.Success();
    }

    private async Task<Result> IssueMultipleMembersCodeAsync(
        string normalizedEmail,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var rawCode = MagicLinkCodeGenerator.GenerateRawCode();
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode);

        var code = AccountAuthCode.CreateForMultipleMembers(
            codeHash: codeHash,
            issuedAtUtc: nowUtc,
            expiresAtUtc: nowUtc.AddHours(24),
            deliveryEmailSnapshot: normalizedEmail);

        await persistence.CommitStartCodeAsync(code, cancellationToken);

        var magicLink = $"{magicLinkSettings.Value.PublicBaseUrl}/auth/exchange?code={rawCode}";

        dispatchQueue.Enqueue(new MagicLinkDispatchItem(
            code.Id,
            normalizedEmail,
            MagicLinkEmailTemplate.Subject,
            MagicLinkEmailTemplate.BuildHtmlBody(magicLink),
            MagicLinkEmailTemplate.BuildTextBody(magicLink),
            LogContext: "Magic link"));

        return Result.Success();
    }

    private async Task<Result> IssueNewAccountCodeAsync(
        string normalizedEmail,
        string businessName,
        string? name,
        string timeZone,
        DateTime nowUtc,
        SignupDefaultsSettings defaults,
        CancellationToken cancellationToken)
    {
        // Pilot capacity gate — check before issuing a code (ADR-365).
        if (defaults.Classification == AccountClassification.Pilot && defaults.MaxPilotAccounts.HasValue)
        {
            var pilotCount = await persistence.CountPilotClassifiedAccountsAsync(cancellationToken);
            if (pilotCount >= defaults.MaxPilotAccounts.Value)
                return Result.Failure(AccountErrors.PilotFull);
        }

        var rawCode = MagicLinkCodeGenerator.GenerateRawCode();
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode);

        var code = AccountAuthCode.CreateForNewAccount(
            codeHash: codeHash,
            issuedAtUtc: nowUtc,
            expiresAtUtc: nowUtc.AddHours(24),
            deliveryEmailSnapshot: normalizedEmail,
            businessName: businessName,
            name: name,
            timeZone: timeZone);

        await persistence.CommitStartCodeAsync(code, cancellationToken);

        var magicLink = $"{magicLinkSettings.Value.PublicBaseUrl}/auth/exchange?code={rawCode}";

        dispatchQueue.Enqueue(new MagicLinkDispatchItem(
            code.Id,
            normalizedEmail,
            MagicLinkEmailTemplate.NewAccountSubject,
            MagicLinkEmailTemplate.BuildNewAccountHtmlBody(magicLink),
            MagicLinkEmailTemplate.BuildNewAccountTextBody(magicLink),
            LogContext: "New-account magic link"));

        return Result.Success();
    }
}
