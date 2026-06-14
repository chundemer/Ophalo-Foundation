using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the Account lifecycle transition guards and the trimmed creation / primary-owner
/// invariants ported in Phase 4a (ADR-018/019/020).
/// </summary>
public class AccountLifecycleTests
{
    static Account NewAccount() =>
        Account.CreateVerified("Acme Plumbing", AccountPurpose.Business, "Europe/Berlin");

    // --- Creation ---

    [Fact]
    public void CreateVerified_starts_active_business_with_trimmed_profile()
    {
        var account = Account.CreateVerified("  Acme Plumbing  ", AccountPurpose.Business, "  Europe/Berlin  ");

        Assert.Equal("Acme Plumbing", account.BusinessName);
        Assert.Equal("Europe/Berlin", account.TimeZone);
        Assert.Equal(AccountPurpose.Business, account.Purpose);
        Assert.Equal(AccountLifecycleState.Active, account.LifecycleState);
        Assert.Null(account.PrimaryOwnerAccountUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateVerified_requires_business_name(string businessName) =>
        Assert.Throws<ArgumentException>(() =>
            Account.CreateVerified(businessName, AccountPurpose.Business, "Europe/Berlin"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateVerified_requires_time_zone(string timeZone) =>
        Assert.Throws<ArgumentException>(() =>
            Account.CreateVerified("Acme", AccountPurpose.Business, timeZone));

    [Fact]
    public void CreateVerified_rejects_undefined_purpose() =>
        Assert.Throws<ArgumentException>(() =>
            Account.CreateVerified("Acme", (AccountPurpose)999, "Europe/Berlin"));

    // --- Suspend ---

    [Fact]
    public void Suspend_moves_active_to_suspended()
    {
        var account = NewAccount();

        var result = account.Suspend();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountLifecycleState.Suspended, account.LifecycleState);
    }

    [Fact]
    public void Suspend_is_rejected_when_already_suspended()
    {
        var account = NewAccount();
        account.Suspend();

        var result = account.Suspend();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.AlreadySuspended, result.Error);
    }

    [Fact]
    public void Suspend_is_rejected_when_closed()
    {
        var account = NewAccount();
        account.Close();

        var result = account.Suspend();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.AlreadyClosed, result.Error);
    }

    // --- Close ---

    [Fact]
    public void Close_moves_active_to_closed()
    {
        var account = NewAccount();

        var result = account.Close();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountLifecycleState.Closed, account.LifecycleState);
    }

    [Fact]
    public void Close_is_rejected_when_already_closed()
    {
        var account = NewAccount();
        account.Close();

        var result = account.Close();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.AlreadyClosed, result.Error);
    }

    // --- Reactivate ---

    [Fact]
    public void Reactivate_restores_a_suspended_account()
    {
        var account = NewAccount();
        account.Suspend();

        var result = account.Reactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountLifecycleState.Active, account.LifecycleState);
    }

    [Fact]
    public void Reactivate_is_rejected_when_not_suspended()
    {
        var account = NewAccount();

        var result = account.Reactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.NotSuspended, result.Error);
    }

    [Fact]
    public void Reactivate_does_not_revive_a_closed_account()
    {
        var account = NewAccount();
        account.Close();

        var result = account.Reactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.NotSuspended, result.Error);
    }

    // --- Primary owner (ADR-019) ---

    [Fact]
    public void AssignPrimaryOwner_accepts_an_active_owner_of_this_account()
    {
        var account = NewAccount();
        var owner = AccountUser.CreateOwner(account.Id, Guid.CreateVersion7(), "o@x.com", "o@x.com");

        var result = account.AssignPrimaryOwner(owner);

        Assert.True(result.IsSuccess);
        Assert.Equal(owner.Id, account.PrimaryOwnerAccountUserId);
    }

    [Fact]
    public void AssignPrimaryOwner_rejects_a_member_of_another_account()
    {
        var account = NewAccount();
        var owner = AccountUser.CreateOwner(Guid.CreateVersion7(), Guid.CreateVersion7(), "o@x.com", "o@x.com");

        var result = account.AssignPrimaryOwner(owner);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.PrimaryOwnerAccountMismatch, result.Error);
        Assert.Null(account.PrimaryOwnerAccountUserId);
    }

    [Fact]
    public void AssignPrimaryOwner_rejects_a_non_owner_role()
    {
        var account = NewAccount();
        var member = AccountUser.CreatePendingInvite(
            account.Id, "v@x.com", "v@x.com", AccountUserRole.Viewer,
            "hash", DateTime.UtcNow.AddDays(7), DateTime.UtcNow);
        member.Activate(Guid.CreateVersion7(), DateTime.UtcNow);

        var result = account.AssignPrimaryOwner(member);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.PrimaryOwnerMustBeOwner, result.Error);
    }

    [Fact]
    public void AssignPrimaryOwner_rejects_a_suspended_owner()
    {
        var account = NewAccount();
        var owner = AccountUser.CreateOwner(account.Id, Guid.CreateVersion7(), "o@x.com", "o@x.com");
        owner.Suspend();

        var result = account.AssignPrimaryOwner(owner);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.PrimaryOwnerMustBeActive, result.Error);
    }
}
