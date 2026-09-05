using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Users.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Redeems a PostAuthContinuation (ADR-497): resolves a name-blank sign-in, an ambiguous
/// multi-membership sign-in, or a name-blank invite acceptance into a session.
///
/// The raw continuation token is supplied by the caller (read from the ophalo.continuation
/// cookie at the Api layer) — never trusted from the request body. clientType/deviceName always
/// come from the continuation's own stored snapshot, never from this call's caller.
/// </summary>
public sealed class CompleteAuthContinuationService(
    IPostAuthContinuationPersistence continuationPersistence,
    IAuthCodePersistence authCodePersistence,
    AuthSessionIssuer sessionIssuer,
    IClock clock)
{
    public async Task<CompleteContinuationResult> HandleAsync(
        string rawContinuationToken,
        string? name,
        Guid? accountUserId,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var tokenHash = MagicLinkCodeGenerator.HashCode(rawContinuationToken.Trim());

        var continuation = await continuationPersistence.FindByHashAsync(tokenHash, cancellationToken);
        if (continuation is null)
            return CompleteContinuationResult.Terminal(PostAuthContinuationErrors.Invalid);

        if (continuation.IsExpired(nowUtc) || continuation.IsConsumed)
        {
            await continuationPersistence.DeleteAsync(continuation.Id, cancellationToken);
            return CompleteContinuationResult.Terminal(PostAuthContinuationErrors.Invalid);
        }

        var userId = continuation.UserId;

        // Step 3: name completion. Retryable — a bad/missing name does not consume or clear the
        // continuation, so the client can resubmit while it is still otherwise valid.
        var currentName = await authCodePersistence.GetUserNameAsync(userId, cancellationToken);
        if (currentName is null)
            return CompleteContinuationResult.Terminal(AccountErrors.InconsistentState);

        if (string.IsNullOrWhiteSpace(currentName))
        {
            if (string.IsNullOrWhiteSpace(name))
                return CompleteContinuationResult.Retryable(UserErrors.NameRequired);

            var setNameResult = await authCodePersistence.SetUserNameAsync(userId, name.Trim(), cancellationToken);
            if (setNameResult.IsFailure)
                return CompleteContinuationResult.Retryable(setNameResult.Error);
        }

        // Step 4: resolve the target membership.
        Guid targetAccountUserId;
        if (continuation.TargetAccountUserId.HasValue)
        {
            targetAccountUserId = continuation.TargetAccountUserId.Value;
        }
        else
        {
            if (accountUserId is null)
                return CompleteContinuationResult.Retryable(PostAuthContinuationErrors.SelectionRequired);

            targetAccountUserId = accountUserId.Value;
        }

        var membership = await authCodePersistence.VerifyActiveMembershipAsync(
            targetAccountUserId, userId, cancellationToken);
        if (membership is null)
        {
            // Cross-user/suspended/removed/unknown selection — terminal per ADR-497.
            await continuationPersistence.DeleteAsync(continuation.Id, cancellationToken);
            return CompleteContinuationResult.Terminal(PostAuthContinuationErrors.Invalid);
        }

        // Step 5: atomic consume — race guard.
        var consumed = await continuationPersistence.ConsumeAsync(continuation.Id, nowUtc, cancellationToken);
        if (!consumed)
            return CompleteContinuationResult.Terminal(PostAuthContinuationErrors.Invalid);

        await continuationPersistence.DeleteAsync(continuation.Id, cancellationToken);

        // Step 6: create the session using the continuation's stored ClientType/DeviceName only.
        var sessionResult = continuation.ClientType == SessionClientType.MobileApp
            ? await sessionIssuer.CreateMobileHandoffAsync(
                membership.AccountId, targetAccountUserId, nowUtc, cancellationToken)
            : await sessionIssuer.CreateSessionAsync(
                membership.AccountId, targetAccountUserId,
                continuation.ClientType, continuation.DeviceName, continuation.Id, cancellationToken);

        return sessionResult.IsFailure
            ? CompleteContinuationResult.Terminal(sessionResult.Error)
            : CompleteContinuationResult.Success(sessionResult.Value);
    }
}

public sealed record CompleteContinuationResult(
    Result<ExchangeSuccessResult>? Result,
    Error? Error,
    bool ClearCookie)
{
    public bool IsSuccess => Result is { IsSuccess: true };

    public static CompleteContinuationResult Success(ExchangeSuccessResult value) =>
        new(Result<ExchangeSuccessResult>.Success(value), null, ClearCookie: true);

    /// <summary>Continuation stays alive, cookie stays set — the client may resubmit.</summary>
    public static CompleteContinuationResult Retryable(Error error) =>
        new(null, error, ClearCookie: false);

    /// <summary>Continuation is invalidated — cookie is cleared regardless of outcome.</summary>
    public static CompleteContinuationResult Terminal(Error error) =>
        new(null, error, ClearCookie: true);
}
