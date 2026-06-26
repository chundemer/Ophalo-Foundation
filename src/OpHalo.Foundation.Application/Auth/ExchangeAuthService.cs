using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Exchanges a raw magic link code for a session.
///
/// Phase 5B: ExistingMember path (atomic consume → session).
/// Phase 5C: NewAccount path (pilot cap re-check → email re-check → atomic consume + graph
///   creation in one transaction → session outside transaction).
///
/// Code consumption uses a persistence-level atomic ExecuteUpdateAsync (race guard).
/// Session creation always runs outside any transaction — failure is a distinct 503 outcome
/// that the frontend maps to a directed recovery UX with a /signin link.
///
/// Logging (D9): session creation failure only, with safe IDs. Do not log raw codes,
/// tokens, magic-link URLs, token hashes, or email/name/business-name.
/// </summary>
public sealed class ExchangeAuthService(
    IAuthCodePersistence persistence,
    IAccountSessionService sessionService,
    AccountProvisioningService provisioning,
    IClock clock,
    IOptions<SignupDefaultsSettings> signupDefaults,
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

            EntryContext.NewAccount =>
                Wrap(await HandleNewAccountAsync(code, clientType, deviceName, nowUtc, cancellationToken)),

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

        return await CreateSessionAsync(
            code.AccountId.Value, code.TargetAccountUserId.Value,
            clientType, deviceName, code.Id, cancellationToken);
    }

    private async Task<Result<ExchangeTokenResult>> HandleNewAccountAsync(
        AccountAuthCode code,
        SessionClientType clientType,
        string? deviceName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Guard: NewAccount codes always have snapshots.
        if (string.IsNullOrWhiteSpace(code.DeliveryEmailSnapshot) ||
            string.IsNullOrWhiteSpace(code.BusinessNameSnapshot) ||
            string.IsNullOrWhiteSpace(code.TimeZoneSnapshot))
        {
            return Result<ExchangeTokenResult>.Failure(AccountErrors.InconsistentState);
        }

        // Re-check pilot capacity before consuming the code (ADR-365).
        var defaults = signupDefaults.Value;
        if (defaults.Classification == AccountClassification.Pilot && defaults.MaxPilotAccounts.HasValue)
        {
            var pilotCount = await persistence.CountPilotClassifiedAccountsAsync(cancellationToken);
            if (pilotCount >= defaults.MaxPilotAccounts.Value)
                return Result<ExchangeTokenResult>.Failure(AccountErrors.PilotFull);
        }

        var trialEndsAtUtc = nowUtc.AddDays(defaults.TrialDurationDays);

        var provisionResult = provisioning.CreateVerified(
            email: code.DeliveryEmailSnapshot,
            name: code.NameSnapshot,
            businessName: code.BusinessNameSnapshot,
            purpose: Core.Entities.Accounts.Enums.AccountPurpose.Business,
            timeZone: code.TimeZoneSnapshot,
            plan: Core.Entities.Accounts.Enums.AccountPlan.Trial,
            classification: defaults.Classification,
            nowUtc: nowUtc,
            trialEndsAtUtc: trialEndsAtUtc);

        if (provisionResult.IsFailure)
            return Result<ExchangeTokenResult>.Failure(provisionResult.Error);

        var graph = provisionResult.Value;

        // Atomic: consume code + save graph in one transaction.
        // Returns AlreadyConsumed (race) or EmailAlreadyInUse (duplicate constraint).
        var commitResult = await persistence.CommitNewAccountExchangeAsync(
            code.Id, graph, nowUtc, cancellationToken);

        if (commitResult.IsFailure)
            return Result<ExchangeTokenResult>.Failure(commitResult.Error);

        // Session creation is outside the transaction — failure leaves the graph committed.
        return await CreateSessionAsync(
            graph.Account.Id, graph.Owner.Id,
            clientType, deviceName, code.Id, cancellationToken);
    }

    private async Task<Result<ExchangeTokenResult>> CreateSessionAsync(
        Guid accountId,
        Guid accountUserId,
        SessionClientType clientType,
        string? deviceName,
        Guid codeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessionService.CreateSession(
                accountId, accountUserId, clientType, deviceName, cancellationToken);

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
                accountId, accountUserId, codeId);

            return Result<ExchangeTokenResult>.Failure(AccountErrors.SessionCreationFailed);
        }
    }
}

public sealed record ExchangeResult(
    Result<ExchangeTokenResult> Result,
    EntryContext? EntryContext);

public sealed record ExchangeTokenResult(string RawToken, DateTime ExpiresAtUtc);
