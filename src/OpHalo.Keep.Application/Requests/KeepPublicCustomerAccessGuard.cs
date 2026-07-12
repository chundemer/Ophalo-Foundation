using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Centralized gate for all customer page reads and writes (ADR-119).
///
/// Resolves request/account context exclusively from the page token, enforces account
/// access and Keep feature gates, and applies terminal-only expiry semantics (ADR-120).
/// Returns a safe KeepPublicCustomerContext projection — no tracked EF entities.
///
/// Guard denial contract (ADR-130):
///   blank/unknown token       → NotFound (hides whether a real request exists)
///   account blocked/feature   → NotFound (hides account state from public callers)
///   expired terminal page     → Success with IsExpired = true (caller maps to 410)
///   active/resolved page      → Success with IsExpired = false
///
/// Account access check blocks only IsBlocked (Suspended, Closed, TrialExpired,
/// PastDueBlocked, Expired, Canceled). IsReadOnly (OffSeason) is NOT blocked for
/// customer page reads — reads remain available in OffSeason per ADR-208. Write services
/// check context.IsOffSeason and return KeepRequestErrors.OffSeasonUnavailable (ADR-221).
///
/// This service uses IKeepRequestDetailPersistence (non-tracked reads). Write services
/// must re-fetch the tracked entity via IKeepCustomerWritePersistence.GetRequestForUpdateAsync
/// using context.RequestId after this guard succeeds.
/// </summary>
public sealed class KeepPublicCustomerAccessGuard(
    IKeepRequestDetailPersistence persistence,
    IAccountAccessPolicy accessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    public async Task<Result<KeepPublicCustomerContext>> EvaluateAsync(
        string pageToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
            return Result<KeepPublicCustomerContext>.Failure(KeepRequestErrors.NotFound);

        var lookup = await persistence.GetRequestByPageTokenAsync(pageToken, ct);
        if (lookup is null)
            return Result<KeepPublicCustomerContext>.Failure(KeepRequestErrors.NotFound);

        var request = lookup.Request;

        var snapshot = await persistence.GetAccountAccessSnapshotAsync(request.AccountId, ct);
        if (snapshot is null)
            return Result<KeepPublicCustomerContext>.Failure(KeepRequestErrors.NotFound);

        var nowUtc = clock.UtcNow;

        var accessContext = new AccountAccessContext(
            snapshot.LifecycleState,
            snapshot.Purpose,
            snapshot.CommercialState,
            snapshot.TrialEndsAtUtc,
            snapshot.PastDueGraceEndsAtUtc,
            snapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            nowUtc);

        var decision = accessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<KeepPublicCustomerContext>.Failure(KeepRequestErrors.NotFound);

        if (!featurePolicy.IsEnabled(snapshot.Plan, FeatureKeys.Keep.CustomerPage))
            return Result<KeepPublicCustomerContext>.Failure(KeepRequestErrors.NotFound);

        // Expiry is enforced only after terminal lifecycle (ADR-120). Active and resolved
        // requests remain accessible even if ExpiresAtUtc is populated — this avoids the
        // reference-app bug where open requests could show a tombstone.
        var isExpired = request.IsTerminal
            && request.ExpiresAtUtc.HasValue
            && request.ExpiresAtUtc.Value <= nowUtc;

        return Result<KeepPublicCustomerContext>.Success(new KeepPublicCustomerContext(
            RequestId: request.Id,
            AccountId: request.AccountId,
            ReferenceCode: request.ReferenceCode,
            BusinessName: lookup.BusinessName,
            CustomerName: request.CustomerName,
            Status: request.Status,
            Description: request.Description,
            CurrentStatusText: request.CurrentStatusText,
            IsTerminal: request.IsTerminal,
            IsExpired: isExpired,
            ExpiresAtUtc: request.ExpiresAtUtc,
            FeedbackWasResolved: request.FeedbackWasResolved,
            FeedbackSubmittedAtUtc: request.FeedbackSubmittedAtUtc,
            IsOffSeason: snapshot.OperatingMode == AccountOperatingMode.OffSeason,
            IntakeUrgency: request.IntakeUrgency,
            Origin: request.Origin,
            Version: isExpired ? null : (Guid?)request.ConcurrencyVersion));
    }
}
