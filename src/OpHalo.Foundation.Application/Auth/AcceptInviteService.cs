using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Handles POST /accounts/invite/accept — validates and activates an invite token.
///
/// Browser-only this phase: clientType = Browser, deviceName = null (D9/ADR-076).
/// Session creation runs outside the activation transaction — failure returns 503 and
/// leaves AccountUser Active so the member can sign in via /auth/signin afterward (D12).
///
/// Logging: session creation failure only, with safe IDs — no token, email, or link (ADR-076).
/// </summary>
public sealed class AcceptInviteService(
    IInvitePersistence persistence,
    IAccountSessionService sessionService,
    IClock clock,
    ILogger<AcceptInviteService> logger)
{
    private static readonly Error TokenRequired =
        Error.Create("Validation.TokenRequired", "A token is required.");

    public async Task<Result<AcceptInviteResult>> HandleAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        // HashToken throws on whitespace — guard here so the caller never sees a 500.
        if (string.IsNullOrWhiteSpace(rawToken))
            return Result<AcceptInviteResult>.Failure(TokenRequired);

        var tokenHash = InviteTokenGenerator.HashToken(rawToken.Trim());
        var nowUtc = clock.UtcNow;

        var acceptResult = await persistence.CommitAcceptInviteAsync(tokenHash, nowUtc, cancellationToken);

        if (acceptResult.IsFailure)
            return Result<AcceptInviteResult>.Failure(acceptResult.Error);

        var accepted = acceptResult.Value;

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

            return Result<AcceptInviteResult>.Success(
                new AcceptInviteResult(session.RawToken, session.ExpiresAtUtc));
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

            return Result<AcceptInviteResult>.Failure(AccountErrors.SessionCreationFailed);
        }
    }
}

/// <summary>Returned on successful invite acceptance. Raw token is for cookie issuance — never log or return in body.</summary>
public sealed record AcceptInviteResult(string RawToken, DateTime ExpiresAtUtc);
