using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the ADR-462 AccountCapabilityPackageEnrollment state machine: Enroll/Disable/Reenable
/// guards, the Core-owned feature-key allow-list, and actor/time validation.
/// </summary>
public class AccountCapabilityPackageEnrollmentTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Later = new(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc);

    static AccountCapabilityPackageEnrollment Enrolled() =>
        AccountCapabilityPackageEnrollment.Enroll(
            AccountId, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, Actor, Now).Value;

    // --- Enroll ---

    [Fact]
    public void Enroll_with_allow_listed_key_succeeds()
    {
        var result = AccountCapabilityPackageEnrollment.Enroll(
            AccountId, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, Actor, Now);

        Assert.True(result.IsSuccess);
        var e = result.Value;
        Assert.Equal(AccountId, e.AccountId);
        Assert.Equal(CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, e.FeatureKey);
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, e.Status);
        Assert.Equal(Now, e.EnabledAt);
        Assert.Null(e.DisabledAt);
        Assert.Equal(Actor, e.ChangedByAccountUserId);
        Assert.NotEqual(Guid.Empty, e.ConcurrencyVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("keep.not_a_real_capability_package")]
    public void Enroll_with_key_outside_allow_list_fails(string featureKey)
    {
        var result = AccountCapabilityPackageEnrollment.Enroll(AccountId, featureKey, Actor, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.UnknownFeatureKey, result.Error);
    }

    [Fact]
    public void Enroll_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => AccountCapabilityPackageEnrollment.Enroll(
            Guid.Empty, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, Actor, Now));
    }

    [Fact]
    public void Enroll_with_empty_actor_throws()
    {
        Assert.Throws<ArgumentException>(() => AccountCapabilityPackageEnrollment.Enroll(
            AccountId, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, Guid.Empty, Now));
    }

    [Fact]
    public void Enroll_with_non_utc_time_throws()
    {
        Assert.Throws<ArgumentException>(() => AccountCapabilityPackageEnrollment.Enroll(
            AccountId, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, Actor, DateTime.Now));
    }

    // --- Disable ---

    [Fact]
    public void Disable_from_Enrolled_succeeds()
    {
        var e = Enrolled();
        var disabledBy = Guid.CreateVersion7();
        var versionBefore = e.ConcurrencyVersion;

        var result = e.Disable(disabledBy, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(CapabilityEnrollmentStatus.Disabled, e.Status);
        Assert.Equal(Later, e.DisabledAt);
        Assert.Equal(disabledBy, e.ChangedByAccountUserId);
        Assert.NotEqual(versionBefore, e.ConcurrencyVersion);
    }

    [Fact]
    public void Disable_when_already_Disabled_fails_not_noop()
    {
        var e = Enrolled();
        e.Disable(Actor, Later);

        var result = e.Disable(Actor, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.AlreadyDisabled, result.Error);
    }

    // --- Reenable ---

    [Fact]
    public void Reenable_from_Disabled_succeeds_and_clears_DisabledAt()
    {
        var e = Enrolled();
        e.Disable(Actor, Now);
        var reenabledBy = Guid.CreateVersion7();
        var versionBefore = e.ConcurrencyVersion;

        var result = e.Reenable(reenabledBy, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, e.Status);
        Assert.Equal(Later, e.EnabledAt);
        Assert.Null(e.DisabledAt);
        Assert.Equal(reenabledBy, e.ChangedByAccountUserId);
        Assert.NotEqual(versionBefore, e.ConcurrencyVersion);
    }

    [Fact]
    public void Reenable_when_already_Enrolled_fails_not_noop()
    {
        var e = Enrolled();

        var result = e.Reenable(Actor, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.AlreadyEnrolled, result.Error);
    }

    // --- Actor/time guards on transitions ---

    [Fact]
    public void Disable_with_empty_actor_throws()
    {
        var e = Enrolled();
        Assert.Throws<ArgumentException>(() => e.Disable(Guid.Empty, Later));
    }

    [Fact]
    public void Reenable_with_non_utc_time_throws()
    {
        var e = Enrolled();
        e.Disable(Actor, Now);
        Assert.Throws<ArgumentException>(() => e.Reenable(Actor, DateTime.Now));
    }
}
