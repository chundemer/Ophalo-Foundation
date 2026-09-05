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
    IPostAuthContinuationPersistence continuationPersistence,
    AuthSessionIssuer sessionIssuer,
    AccountProvisioningService provisioning,
    IClock clock,
    IOptions<SignupDefaultsSettings> signupDefaults)
{
    private static readonly TimeSpan ContinuationLifetime = TimeSpan.FromMinutes(10);

    private async Task<Result<ExchangeSuccessResult>> CreateContinuationAsync(
        Guid userId,
        Guid? targetAccountUserId,
        bool requiresName,
        IReadOnlyList<ActiveMembershipOption>? workspaces,
        SessionClientType clientType,
        string? deviceName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var rawToken = MagicLinkCodeGenerator.GenerateRawCode();
        var tokenHash = MagicLinkCodeGenerator.HashCode(rawToken);
        var expiresAtUtc = nowUtc.Add(ContinuationLifetime);

        var continuation = PostAuthContinuation.Create(
            tokenHash, userId, targetAccountUserId, clientType, deviceName, nowUtc, expiresAtUtc);

        await continuationPersistence.CreateAsync(continuation, cancellationToken);

        return Result<ExchangeSuccessResult>.Success(
            new ExchangeContinuationResult(rawToken, requiresName, workspaces, expiresAtUtc));
    }

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

            EntryContext.MultipleMembers =>
                Wrap(await HandleMultipleMembersAsync(code, clientType, deviceName, nowUtc, cancellationToken)),

            _ => Fail(AccountErrors.InconsistentState, null)
        };

        static ExchangeResult Fail(Error error, EntryContext? context) =>
            new(Result<ExchangeSuccessResult>.Failure(error), context);

        static ExchangeResult Wrap(Result<ExchangeSuccessResult> result) =>
            new(result, null);
    }

    private async Task<Result<ExchangeSuccessResult>> HandleExistingMemberAsync(
        AccountAuthCode code,
        SessionClientType clientType,
        string? deviceName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Guard: ExistingMember codes always have AccountId and TargetAccountUserId.
        if (code.AccountId is null || code.TargetAccountUserId is null)
            return Result<ExchangeSuccessResult>.Failure(AccountErrors.InconsistentState);

        // Atomic consume — returns false if another concurrent request won the race.
        var consumed = await persistence.ConsumeCodeAsync(code.Id, nowUtc, cancellationToken);
        if (!consumed)
            return Result<ExchangeSuccessResult>.Failure(AccountAuthCodeErrors.AlreadyConsumed);

        var nameCheck = await persistence.GetExistingMemberNameCheckAsync(
            code.TargetAccountUserId.Value, cancellationToken);
        if (nameCheck is null)
            return Result<ExchangeSuccessResult>.Failure(AccountErrors.InconsistentState);

        if (string.IsNullOrWhiteSpace(nameCheck.Name))
        {
            return await CreateContinuationAsync(
                nameCheck.UserId, code.TargetAccountUserId, requiresName: true, workspaces: null,
                clientType, deviceName, nowUtc, cancellationToken);
        }

        if (clientType == SessionClientType.MobileApp)
            return await sessionIssuer.CreateMobileHandoffAsync(
                code.AccountId.Value, code.TargetAccountUserId.Value, nowUtc, cancellationToken);

        return await sessionIssuer.CreateSessionAsync(
            code.AccountId.Value, code.TargetAccountUserId.Value,
            clientType, deviceName, code.Id, cancellationToken);
    }

    private async Task<Result<ExchangeSuccessResult>> HandleMultipleMembersAsync(
        AccountAuthCode code,
        SessionClientType clientType,
        string? deviceName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Atomic consume — returns false if another concurrent request won the race.
        var consumed = await persistence.ConsumeCodeAsync(code.Id, nowUtc, cancellationToken);
        if (!consumed)
            return Result<ExchangeSuccessResult>.Failure(AccountAuthCodeErrors.AlreadyConsumed);

        var resolution = await persistence.GetMultipleMembersResolutionAsync(
            code.DeliveryEmailSnapshot, cancellationToken);
        if (resolution is null)
            return Result<ExchangeSuccessResult>.Failure(AccountErrors.InconsistentState);

        return await CreateContinuationAsync(
            resolution.UserId, targetAccountUserId: null,
            requiresName: string.IsNullOrWhiteSpace(resolution.Name),
            workspaces: resolution.Memberships,
            clientType, deviceName, nowUtc, cancellationToken);
    }

    private async Task<Result<ExchangeSuccessResult>> HandleNewAccountAsync(
        AccountAuthCode code,
        SessionClientType clientType,
        string? deviceName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (clientType == SessionClientType.MobileApp)
            return Result<ExchangeSuccessResult>.Failure(AccountAuthCodeErrors.MobileNewAccountUnsupported);

        // Guard: NewAccount codes always have snapshots.
        if (string.IsNullOrWhiteSpace(code.DeliveryEmailSnapshot) ||
            string.IsNullOrWhiteSpace(code.BusinessNameSnapshot) ||
            string.IsNullOrWhiteSpace(code.TimeZoneSnapshot))
        {
            return Result<ExchangeSuccessResult>.Failure(AccountErrors.InconsistentState);
        }

        // Re-check pilot capacity before consuming the code (ADR-365).
        var defaults = signupDefaults.Value;
        if (defaults.Classification == AccountClassification.Pilot && defaults.MaxPilotAccounts.HasValue)
        {
            var pilotCount = await persistence.CountPilotClassifiedAccountsAsync(cancellationToken);
            if (pilotCount >= defaults.MaxPilotAccounts.Value)
                return Result<ExchangeSuccessResult>.Failure(AccountErrors.PilotFull);
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
            return Result<ExchangeSuccessResult>.Failure(provisionResult.Error);

        var graph = provisionResult.Value;

        // Atomic: consume code + save graph in one transaction.
        // Returns AlreadyConsumed (race) or EmailAlreadyInUse (duplicate constraint).
        var commitResult = await persistence.CommitNewAccountExchangeAsync(
            code.Id, graph, nowUtc, cancellationToken);

        if (commitResult.IsFailure)
            return Result<ExchangeSuccessResult>.Failure(commitResult.Error);

        if (clientType == SessionClientType.MobileApp)
            return await sessionIssuer.CreateMobileHandoffAsync(
                graph.Account.Id, graph.Owner.Id, nowUtc, cancellationToken);

        // Session creation is outside the transaction — failure leaves the graph committed.
        return await sessionIssuer.CreateSessionAsync(
            graph.Account.Id, graph.Owner.Id,
            clientType, deviceName, code.Id, cancellationToken);
    }

}

public sealed record ExchangeResult(
    Result<ExchangeSuccessResult> Result,
    EntryContext? EntryContext);

public abstract record ExchangeSuccessResult(DateTime ExpiresAtUtc);
public sealed record ExchangeTokenResult(string RawToken, DateTime ExpiresAtUtc)
    : ExchangeSuccessResult(ExpiresAtUtc);
public sealed record ExchangeHandoffCodeResult(string HandoffCode, DateTime ExpiresAtUtc)
    : ExchangeSuccessResult(ExpiresAtUtc);

/// <summary>
/// A partial outcome (ADR-497): email proof succeeded but a session cannot yet be created — a
/// missing display name and/or an ambiguous membership must be resolved via POST /auth/continue.
/// RawContinuationToken travels only via the ophalo.continuation cookie — the endpoint layer
/// must never place it in a JSON response body.
/// </summary>
public sealed record ExchangeContinuationResult(
    string RawContinuationToken,
    bool RequiresName,
    IReadOnlyList<ActiveMembershipOption>? Workspaces,
    DateTime ExpiresAtUtc) : ExchangeSuccessResult(ExpiresAtUtc);
