using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the ADR-462 AccountFeatureAccessResolver composition: plan-only, enrollment-only,
/// disabled-enrollment, unknown feature key, and missing-context fail-closed behavior.
/// </summary>
public class AccountFeatureAccessResolverTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    const string FeatureKey = CapabilityPackageFeatureKeys.PriceBookQuotesMaterials;

    static AccountFeatureAccessResolver Resolver(AccountCapabilityPackageEnrollment? enrollment) =>
        new(new FeatureAccessPolicy(), new FakeEnrollmentPersistence(enrollment));

    [Fact]
    public async Task Plan_included_grants_access_even_without_enrollment()
    {
        // keep.enabled is plan-derived for every tier including Starter (PlanEntitlements) —
        // price_book_quotes_materials is deliberately absent from every plan (ADR-462: enrollment
        // is the only grant path for it), so a plan-based key is used here instead.
        var resolver = Resolver(enrollment: null);

        var result = await resolver.IsEnabledAsync(
            AccountId, new AccountFeatureAccessContext(AccountPlan.Starter), FeatureKeys.Keep.Enabled, default);

        Assert.True(result);
    }

    [Fact]
    public async Task Plan_excluded_but_active_enrollment_grants_access()
    {
        var enrollment = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        var resolver = Resolver(enrollment);

        var result = await resolver.IsEnabledAsync(
            AccountId, new AccountFeatureAccessContext(AccountPlan.Starter), FeatureKey, default);

        Assert.True(result);
    }

    [Fact]
    public async Task Plan_excluded_and_disabled_enrollment_denies_access()
    {
        var enrollment = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        enrollment.Disable(Actor, Now);
        var resolver = Resolver(enrollment);

        var result = await resolver.IsEnabledAsync(
            AccountId, new AccountFeatureAccessContext(AccountPlan.Starter), FeatureKey, default);

        Assert.False(result);
    }

    [Fact]
    public async Task Unknown_feature_key_denies_access()
    {
        var resolver = Resolver(enrollment: null);

        var result = await resolver.IsEnabledAsync(
            AccountId, new AccountFeatureAccessContext(AccountPlan.Starter), "keep.not_a_real_key", default);

        Assert.False(result);
    }

    [Fact]
    public async Task Missing_account_context_denies_access_even_with_active_enrollment()
    {
        var enrollment = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        var resolver = Resolver(enrollment);

        var result = await resolver.IsEnabledAsync(AccountId, context: null, FeatureKey, default);

        Assert.False(result);
    }

    private sealed class FakeEnrollmentPersistence(AccountCapabilityPackageEnrollment? enrollment)
        : IAccountCapabilityPackageEnrollmentPersistence
    {
        public Task<AccountCapabilityPackageEnrollment?> GetByAccountAndFeatureKeyAsync(
            Guid accountId, string featureKey, CancellationToken cancellationToken) =>
            Task.FromResult(enrollment is not null
                && enrollment.AccountId == accountId
                && enrollment.FeatureKey == featureKey
                ? enrollment
                : null);

        public Task AddAsync(AccountCapabilityPackageEnrollment enrollment_, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CommitAsync(AccountCapabilityPackageEnrollment enrollment_, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
