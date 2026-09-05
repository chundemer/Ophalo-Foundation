using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// The two session-issuance primitives every successful auth completion reuses — a browser
/// session cookie or a mobile handoff code. Shared by ExchangeAuthService (/auth/exchange) and
/// CompleteAuthContinuationService (/auth/continue) so both paths create sessions identically.
/// </summary>
public sealed class AuthSessionIssuer(
    IAccountSessionService sessionService,
    IMobileHandoffCodePersistence mobileHandoffPersistence,
    ILogger<AuthSessionIssuer> logger)
{
    public async Task<Result<ExchangeSuccessResult>> CreateSessionAsync(
        Guid accountId,
        Guid accountUserId,
        SessionClientType clientType,
        string? deviceName,
        Guid contextId,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessionService.CreateSession(
                accountId, accountUserId, clientType, deviceName, cancellationToken);

            return Result<ExchangeSuccessResult>.Success(
                new ExchangeTokenResult(session.RawToken, session.ExpiresAtUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Session creation failed after auth completion. AccountId={AccountId} AccountUserId={AccountUserId} ContextId={ContextId}",
                accountId, accountUserId, contextId);

            return Result<ExchangeSuccessResult>.Failure(AccountErrors.SessionCreationFailed);
        }
    }

    public async Task<Result<ExchangeSuccessResult>> CreateMobileHandoffAsync(
        Guid accountId,
        Guid accountUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var rawCode = MagicLinkCodeGenerator.GenerateRawCode();
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode);
        var expiresAtUtc = nowUtc.AddMinutes(10);

        var handoffCode = Core.Entities.Accounts.MobileHandoffCode.Create(
            codeHash,
            accountId,
            accountUserId,
            nowUtc,
            expiresAtUtc);

        await mobileHandoffPersistence.CreateAsync(handoffCode, cancellationToken);

        return Result<ExchangeSuccessResult>.Success(
            new ExchangeHandoffCodeResult(rawCode, expiresAtUtc));
    }
}
