using Microsoft.Extensions.Logging;
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
/// - No existing identity → issue NewAccount code with business-name/time-zone snapshots.
/// - Any other state (ambiguous, invited, suspended, removed, existing User without active
///   membership) → neutral 200, no code issued (enumeration protection).
///
/// Pilot cap (D3): when Classification=Pilot and MaxPilotAccounts is set, check capacity
/// before issuing a NewAccount code. Pilot-full returns a non-neutral 409 — the caller
/// may prompt the user to join a waitlist.
///
/// Email delivery (D8): direct IEmailSender, best-effort. Delivery failure must not
/// change the public response.
///
/// Logging (D9): log only safe IDs. Do not log email, business name, name, raw codes,
/// or magic-link URLs.
/// </summary>
public sealed class StartAuthService(
    IAuthCodePersistence persistence,
    IEmailSender emailSender,
    IClock clock,
    IOptions<MagicLinkSettings> magicLinkSettings,
    IOptions<SignupDefaultsSettings> signupDefaults,
    ILogger<StartAuthService> logger)
{
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

        var classification = await persistence.ClassifyStartRequestAsync(normalizedEmail, cancellationToken);

        switch (classification)
        {
            case StartAsNeutral:
                return Result.Success();

            case StartAsExistingMember existing:
                return await IssueExistingMemberCodeAsync(
                    existing.AccountId, existing.AccountUserId,
                    normalizedEmail, nowUtc, cancellationToken);

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

        try
        {
            await emailSender.SendAsync(
                normalizedEmail,
                MagicLinkEmailTemplate.NewAccountSubject,
                MagicLinkEmailTemplate.BuildHtmlBody(magicLink),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "New-account magic link email delivery failed for code {CodeId}.", code.Id);
        }

        return Result.Success();
    }
}
