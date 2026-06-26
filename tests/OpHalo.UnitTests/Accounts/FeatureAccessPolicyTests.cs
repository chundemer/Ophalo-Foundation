using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Phase 4d exit-gate matrix for ADR-009 "Account is entitled": the plan → feature map, plan
/// limit defaults, the <c>account.user_limit</c> per-account override, and fail-closed handling
/// of unknown plans/keys (§4.11). The tier matrix itself is provisional (not locked); these
/// tests assert the <em>shape</em> of the entitlement model, not final packaging.
/// </summary>
public class FeatureAccessPolicyTests
{
    static readonly IFeatureAccessPolicy Policy = new FeatureAccessPolicy();
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly DateTime TrialEnds = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    static AccountEntitlements Entitlements(AccountPlan plan, int maxUserSeats) =>
        AccountEntitlements.Create(AccountId, plan, maxUserSeats, TrialEnds, AccountClassification.Production);

    // --- Feature surface by plan ---------------------------------------------

    [Fact]
    public void Starter_has_browser_push_but_not_mobile_push_or_insights()
    {
        Assert.True(Policy.IsEnabled(AccountPlan.Starter, FeatureKeys.Keep.BrowserPush));
        Assert.False(Policy.IsEnabled(AccountPlan.Starter, FeatureKeys.Keep.MobilePush));
        Assert.False(Policy.IsEnabled(AccountPlan.Starter, FeatureKeys.Keep.Insights));
    }

    [Fact]
    public void Professional_has_mobile_push_and_insights()
    {
        Assert.True(Policy.IsEnabled(AccountPlan.Professional, FeatureKeys.Keep.MobilePush));
        Assert.True(Policy.IsEnabled(AccountPlan.Professional, FeatureKeys.Keep.Insights));
        Assert.True(Policy.IsEnabled(AccountPlan.Professional, FeatureKeys.Keep.BrowserPush));
    }

    [Fact]
    public void Trial_is_full_featured()
    {
        Assert.True(Policy.IsEnabled(AccountPlan.Trial, FeatureKeys.Keep.MobilePush));
        Assert.True(Policy.IsEnabled(AccountPlan.Trial, FeatureKeys.Keep.Insights));
        Assert.True(Policy.IsEnabled(AccountPlan.Trial, FeatureKeys.Keep.PublicIntake));
    }

    [Theory]
    [InlineData(AccountPlan.Trial)]
    [InlineData(AccountPlan.Starter)]
    [InlineData(AccountPlan.Professional)]
    [InlineData(AccountPlan.Business)]
    [InlineData(AccountPlan.Enterprise)]
    [InlineData(AccountPlan.Internal)]
    public void Every_plan_enables_the_keep_core_surface(AccountPlan plan)
    {
        // keep.enabled (the master switch) plus the always-on operating surface.
        Assert.True(Policy.IsEnabled(plan, FeatureKeys.Keep.Enabled));
        Assert.True(Policy.IsEnabled(plan, FeatureKeys.Keep.OperatorQueue));
        Assert.True(Policy.IsEnabled(plan, FeatureKeys.Keep.CloseRequest));
    }

    // --- Fail-closed feature handling ----------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("keep.does_not_exist")]
    [InlineData("account.billing.manage")]  // a permission key, never a feature key
    public void Unknown_or_blank_feature_key_is_disabled(string key) =>
        Assert.False(Policy.IsEnabled(AccountPlan.Enterprise, key));

    [Fact]
    public void Undefined_plan_enables_nothing()
    {
        Assert.False(Policy.IsEnabled((AccountPlan)999, FeatureKeys.Keep.Enabled));
    }

    // --- Limit defaults -------------------------------------------------------

    [Fact]
    public void Plan_limit_defaults_resolve()
    {
        Assert.Equal(10, Policy.GetLimit(AccountPlan.Professional, FeatureLimitKeys.Account.UserLimit));
        Assert.Equal(1500, Policy.GetLimit(AccountPlan.Professional, FeatureLimitKeys.Keep.MonthlyRequestLimit));
    }

    [Fact]
    public void Internal_limits_are_unlimited()
    {
        Assert.Equal(FeatureLimitKeys.Unlimited, Policy.GetLimit(AccountPlan.Internal, FeatureLimitKeys.Account.UserLimit));
        Assert.Equal(FeatureLimitKeys.Unlimited, Policy.GetLimit(AccountPlan.Internal, FeatureLimitKeys.Keep.MonthlyRequestLimit));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("keep.no_such_limit")]
    public void Unknown_or_blank_limit_key_fails_closed_to_zero(string key) =>
        Assert.Equal(0, Policy.GetLimit(AccountPlan.Enterprise, key));

    [Fact]
    public void Undefined_plan_limit_fails_closed_to_zero() =>
        Assert.Equal(0, Policy.GetLimit((AccountPlan)999, FeatureLimitKeys.Account.UserLimit));

    // --- ResolveLimit: per-account seat override ------------------------------

    [Fact]
    public void Max_user_seats_overrides_only_the_user_limit()
    {
        var e = Entitlements(AccountPlan.Professional, maxUserSeats: 20);

        // account.user_limit takes the per-account override (20), not the plan default (10).
        Assert.Equal(20, Policy.ResolveLimit(e, FeatureLimitKeys.Account.UserLimit));
        // monthly request limit is unaffected by seats — still the plan default.
        Assert.Equal(1500, Policy.ResolveLimit(e, FeatureLimitKeys.Keep.MonthlyRequestLimit));
    }

    [Fact]
    public void User_limit_falls_back_to_plan_default_when_no_seat_override()
    {
        var e = Entitlements(AccountPlan.Professional, maxUserSeats: 0);

        Assert.Equal(10, Policy.ResolveLimit(e, FeatureLimitKeys.Account.UserLimit));
    }

    [Fact]
    public void Resolve_limit_passes_through_to_plan_default_for_non_overridable_keys()
    {
        var e = Entitlements(AccountPlan.Starter, maxUserSeats: 99);

        Assert.Equal(250, Policy.ResolveLimit(e, FeatureLimitKeys.Keep.MonthlyRequestLimit));
    }

    [Fact]
    public void Resolve_limit_rejects_null_entitlements() =>
        Assert.Throws<ArgumentNullException>(
            () => Policy.ResolveLimit(null!, FeatureLimitKeys.Account.UserLimit));
}
