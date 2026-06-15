using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Exchanges a raw magic link code for a session.
///
/// Phase 5B handles ExistingMember only. Unknown EntryContext values return
/// InconsistentState so that Phase 5C additions fail closed at compile time.
///
/// Code consumption uses a persistence-level atomic ExecuteUpdateAsync (race guard).
/// Session creation runs outside any transaction — failure is a distinct 503 outcome
/// that the frontend maps to a directed recovery UX with a /signin link.
///
/// Logging: session creation failure is the only unexpected server-side failure —
/// log with AccountId, AccountUserId, and AccountAuthCodeId. Do not log raw codes,
/// tokens, magic link URLs, token hashes, or email addresses.
/// </summary>
public sealed class ExchangeAuthService(
    IAuthCodePersistence persistence,
    IAccountSessionService sessionService,
    IClock clock,
    ILogger<ExchangeAuthService> logger)
{
    public async Task<ExchangeResult> HandleAsync(
        string rawCode,
        SessionClientType clientType,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var codeHash = MagicLinkCodeGenerator.HashCode(rawCode.Trim());

        var code = await persistence.FindCodeByHashAsync(codeHash, cancellationToken);

        if (code is null)
            return Fail(AccountAuthCodeErrors.NotFound, null);

        if (code.IsExpired(nowUtc))
            return Fail(AccountAuthCodeErrors.Expired, code.EntryContext);

        if (code.IsConsumed)
            return Fail(AccountAuthCodeErrors.AlreadyConsumed, code.EntryContext);

        if (code.IsInvalidated)
            return Fail(AccountAuthCodeErrors.CannotConsumeInvalidated, code.EntryContext);

        if (code.EntryContext is null)
            return Fail(AccountErrors.InconsistentState, null);

        return code.EntryContext switch
        {
            EntryContext.ExistingMember =>
                Wrap(await HandleExistingMemberAsync(code, clientType, deviceName, nowUtc, cancellationToken)),

            // Exhaustive — any new EntryContext value added by Phase 5C must be handled here.
            _ => Fail(AccountErrors.InconsistentState, null)
        };

        static ExchangeResult Fail(Error error, EntryContext? context) =>
            new(Result<ExchangeTokenResult>.Failure(error), context);

        static ExchangeResult Wrap(Result<ExchangeTokenResult> result) =>
            new(result, null);
    }

    private async Task<Result<ExchangeTokenResult>> HandleExistingMemberAsync(
        AccountAuthCode code,
        SessionClientType clientType,
        string? deviceName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Guard: ExistingMember codes always have AccountId and TargetAccountUserId.
        if (code.AccountId is null || code.TargetAccountUserId is null)
            return Result<ExchangeTokenResult>.Failure(AccountErrors.InconsistentState);

        // Atomic consume — returns false if another concurrent request won the race.
        var consumed = await persistence.ConsumeCodeAsync(code.Id, nowUtc, cancellationToken);
        if (!consumed)
            return Result<ExchangeTokenResult>.Failure(AccountAuthCodeErrors.AlreadyConsumed);

        // Session creation is intentionally outside any transaction. Failure here leaves
        // the code consumed but sessionless — frontend shows directed recovery UX with /signin.
        try
        {
            var session = await sessionService.CreateSession(
                code.AccountId.Value,
                code.TargetAccountUserId.Value,
                clientType,
                deviceName,
                cancellationToken);

            return Result<ExchangeTokenResult>.Success(
                new ExchangeTokenResult(session.RawToken, session.ExpiresAtUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Session creation failed after code exchange. AccountId={AccountId} AccountUserId={AccountUserId} AccountAuthCodeId={AccountAuthCodeId}",
                code.AccountId,
                code.TargetAccountUserId,
                code.Id);

            return Result<ExchangeTokenResult>.Failure(AccountErrors.SessionCreationFailed);
        }
    }
}

public sealed record ExchangeResult(
    Result<ExchangeTokenResult> Result,
    EntryContext? EntryContext);

public sealed record ExchangeTokenResult(string RawToken, DateTime ExpiresAtUtc);
