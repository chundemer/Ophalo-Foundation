using System.Collections.Frozen;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// The <see cref="AccountPlan"/> → entitlement maps (Phase 4d): which feature capabilities each
/// plan enables, and the default value for each usage limit. "Plans produce entitlements"
/// (build plan §4.11) — runtime code authorizes against the resulting feature/limit keys, never
/// against the plan name itself (ADR-009). Consulted by <see cref="FeatureAccessPolicy"/>.
/// </summary>
/// <remarks>
/// Features are built by explicit set composition (the same idiom as
/// <see cref="Authorization.RolePermissions"/>), not enum arithmetic — plan values are identity,
/// not a ranking. <b>The tier matrix and limit values here are provisional and intentionally NOT
/// locked</b> (build plan §4.11 "do not lock the exact tier matrix yet"); they live in one place
/// so packaging can change without touching policy or call sites.
/// </remarks>
public static class PlanEntitlements
{
    // --- Feature composition --------------------------------------------------

    // The always-on Keep operating surface: everything needed to run the request workflow.
    static readonly string[] KeepCore =
    [
        FeatureKeys.Keep.Enabled,
        FeatureKeys.Keep.PublicIntake,
        FeatureKeys.Keep.CustomerPage,
        FeatureKeys.Keep.OperatorQueue,
        FeatureKeys.Keep.RequestDetail,
        FeatureKeys.Keep.OperatorMessaging,
        FeatureKeys.Keep.CustomerMessaging,
        FeatureKeys.Keep.InternalNotes,
        FeatureKeys.Keep.CloseRequest,
        FeatureKeys.Keep.SseLiveUpdates,
        FeatureKeys.Keep.EmailNotifications,
        FeatureKeys.Keep.RequestSubscriptions,
    ];

    static readonly string[] StarterFeatures =
    [
        .. KeepCore,
        FeatureKeys.Keep.BrowserPush,
    ];

    // Full Keep feature surface — adds mobile push and insights on top of Starter.
    static readonly string[] FullFeatures =
    [
        .. StarterFeatures,
        FeatureKeys.Keep.MobilePush,
        FeatureKeys.Keep.Insights,
    ];

    // Internal accounts get every capability. Aliased explicitly (not reusing FullFeatures
    // inline) so that adding a future feature array forces a deliberate choice about Internal.
    static readonly string[] InternalFeatures =
    [
        .. FullFeatures,
    ];

    /// <summary>Plan → enabled feature keys. Higher tiers (Business/Enterprise) share the full
    /// feature surface and differ only by limits; Trial is full-featured for evaluation.</summary>
    public static readonly FrozenDictionary<AccountPlan, FrozenSet<string>> Features =
        new Dictionary<AccountPlan, FrozenSet<string>>
        {
            [AccountPlan.Trial] = FullFeatures.ToFrozenSet(StringComparer.Ordinal),
            [AccountPlan.Starter] = StarterFeatures.ToFrozenSet(StringComparer.Ordinal),
            [AccountPlan.Professional] = FullFeatures.ToFrozenSet(StringComparer.Ordinal),
            [AccountPlan.Business] = FullFeatures.ToFrozenSet(StringComparer.Ordinal),
            [AccountPlan.Enterprise] = FullFeatures.ToFrozenSet(StringComparer.Ordinal),
            [AccountPlan.Internal] = InternalFeatures.ToFrozenSet(StringComparer.Ordinal),
        }.ToFrozenDictionary();

    // --- Limit defaults -------------------------------------------------------

    static FrozenDictionary<string, int> Limits(int userLimit, int monthlyRequestLimit) =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [FeatureLimitKeys.Account.UserLimit] = userLimit,
            [FeatureLimitKeys.Keep.MonthlyRequestLimit] = monthlyRequestLimit,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Plan → default value per limit key. Per-account overrides (seats) are applied by
    /// <see cref="FeatureAccessPolicy.ResolveLimit"/>, not here.</summary>
    public static readonly FrozenDictionary<AccountPlan, FrozenDictionary<string, int>> LimitDefaults =
        new Dictionary<AccountPlan, FrozenDictionary<string, int>>
        {
            [AccountPlan.Trial] = Limits(userLimit: 3, monthlyRequestLimit: 50),
            [AccountPlan.Starter] = Limits(userLimit: 3, monthlyRequestLimit: 250),
            [AccountPlan.Professional] = Limits(userLimit: 10, monthlyRequestLimit: 1500),
            [AccountPlan.Business] = Limits(userLimit: 25, monthlyRequestLimit: 6000),
            [AccountPlan.Enterprise] = Limits(userLimit: FeatureLimitKeys.Unlimited, monthlyRequestLimit: FeatureLimitKeys.Unlimited),
            [AccountPlan.Internal] = Limits(userLimit: FeatureLimitKeys.Unlimited, monthlyRequestLimit: FeatureLimitKeys.Unlimited),
        }.ToFrozenDictionary();
}
