using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// ADR-462 — the account-aware entitlement fan-out: plan **or** an active capability-package
/// enrollment. Callers that need account-scoped access must go through this resolver, never
/// <see cref="IFeatureAccessPolicy"/> alone.
/// </summary>
/// <remarks>
/// Scope boundary (ADR-462): entitlement only. Never duplicates commercial-standing/lifecycle
/// blocking (Suspended, Closed, Expired, Canceled, trial expiry, OffSeason) — that remains
/// <see cref="Access.IAccountAccessPolicy"/>'s gate, evaluated by the caller before this resolver.
/// Composition order: account access gate → this resolver → user permission → request/state policy.
/// </remarks>
public interface IAccountFeatureAccessResolver
{
    /// <summary>
    /// True if the account is entitled to <paramref name="featureKey"/> via plan or an active
    /// enrollment. Fail-closed to <c>false</c> if <paramref name="context"/> is <c>null</c> — a
    /// missing account entitlement/context — even when an enrollment row exists for the account.
    /// </summary>
    Task<bool> IsEnabledAsync(
        Guid accountId,
        AccountFeatureAccessContext? context,
        string featureKey,
        CancellationToken cancellationToken);
}

/// <summary>The plan-derived context an account-aware caller supplies to the resolver.</summary>
public sealed record AccountFeatureAccessContext(AccountPlan Plan);
