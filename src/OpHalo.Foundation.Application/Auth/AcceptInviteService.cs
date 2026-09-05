using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Handles POST /accounts/invite/accept — validates and activates an invite token.
///
/// Browser-only this phase: clientType = Browser, deviceName = null (D9/ADR-076).
/// A name-blank User (invited before ever signing in) routes through a PostAuthContinuation
/// with TargetAccountUserId already set — the membership is known, only the name gate remains
/// (ADR-497 Slice 3). Session creation (either path) runs outside the activation transaction —
/// failure returns 503 and leaves AccountUser Active so the member can sign in via
/// /auth/signin afterward (D12).
///
/// Logging: session creation failure only, with safe IDs — no token, email, or link (ADR-076).
/// </summary>
public sealed class AcceptInviteService(
    IInvitePersistence persistence,
    IAccountSessionService sessionService,
    IPostAuthContinuationPersistence continuationPersistence,
    IClock clock,
    ILogger<AcceptInviteService> logger)
{
    private static readonly TimeSpan ContinuationLifetime = TimeSpan.FromMinutes(10);

    private static readonly Error TokenRequired =
        Error.Create("Validation.TokenRequired", "A token is required.");

    public async Task<Result<AcceptInviteOutcome>> HandleAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        // HashToken throws on whitespace — guard here so the caller never sees a 500.
        if (string.IsNullOrWhiteSpace(rawToken))
            return Result<AcceptInviteOutcome>.Failure(TokenRequired);

        var tokenHash = InviteTokenGenerator.HashToken(rawToken.Trim());
        var nowUtc = clock.UtcNow;

        var acceptResult = await persistence.CommitAcceptInviteAsync(tokenHash, nowUtc, cancellationToken);

        if (acceptResult.IsFailure)
            return Result<AcceptInviteOutcome>.Failure(acceptResult.Error);

        var accepted = acceptResult.Value;

        if (accepted.IsNameBlank)
        {
            var rawContinuationToken = MagicLinkCodeGenerator.GenerateRawCode();
            var continuationHash = MagicLinkCodeGenerator.HashCode(rawContinuationToken);
            var expiresAtUtc = nowUtc.Add(ContinuationLifetime);

            var continuation = PostAuthContinuation.Create(
                continuationHash, accepted.UserId, accepted.AccountUserId,
                SessionClientType.Browser, deviceName: null, nowUtc, expiresAtUtc);

            await continuationPersistence.CreateAsync(continuation, cancellationToken);

            return Result<AcceptInviteOutcome>.Success(
                AcceptInviteOutcome.Continuation(rawContinuationToken, expiresAtUtc));
        }

        // Session creation is outside the transaction. Failure leaves membership Active;
        // the member can sign in via /auth/signin afterward (D12).
        try
        {
            var session = await sessionService.CreateSession(
                accepted.AccountId,
                accepted.AccountUserId,
                SessionClientType.Browser,
                deviceName: null,
                cancellationToken);

            return Result<AcceptInviteOutcome>.Success(
                AcceptInviteOutcome.Session(session.RawToken, session.ExpiresAtUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Session creation failed after invite accept. AccountId={AccountId} AccountUserId={AccountUserId}",
                accepted.AccountId, accepted.AccountUserId);

            return Result<AcceptInviteOutcome>.Failure(AccountErrors.SessionCreationFailed);
        }
    }
}

/// <summary>
/// Returned on successful invite acceptance — either an immediate session (already-named User)
/// or a post-auth continuation (name-blank User, ADR-497 Slice 3). Exactly one of the two shapes
/// is populated; the raw token/continuation token is for cookie issuance only — never log or
/// return it in a response body field other than the cookie itself.
/// </summary>
public sealed record AcceptInviteOutcome(
    string? SessionRawToken,
    DateTime? SessionExpiresAtUtc,
    string? ContinuationRawToken,
    DateTime? ContinuationExpiresAtUtc)
{
    public bool RequiresContinuation => ContinuationRawToken is not null;

    public static AcceptInviteOutcome Session(string rawToken, DateTime expiresAtUtc) =>
        new(rawToken, expiresAtUtc, null, null);

    public static AcceptInviteOutcome Continuation(string rawContinuationToken, DateTime expiresAtUtc) =>
        new(null, null, rawContinuationToken, expiresAtUtc);
}
