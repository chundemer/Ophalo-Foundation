using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the AccountUser membership transitions ported in Phase 4a (ADR-016/017):
/// Activate, RefreshInvite, Suspend, Remove, plus the trimmed factories.
/// </summary>
public class AccountUserMembershipTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static DateTime Now => DateTime.UtcNow;

    static AccountUser PendingInvite(AccountUserRole role = AccountUserRole.Viewer) =>
        AccountUser.CreatePendingInvite(
            AccountId, "Invitee@X.com", "invitee@x.com", role,
            "token-hash", Now.AddDays(7), Now);

    static AccountUser Owner() =>
        AccountUser.CreateOwner(AccountId, Guid.CreateVersion7(), "Owner@X.com", "owner@x.com");

    // --- Factories ---

    [Fact]
    public void CreateOwner_is_an_active_owner_with_no_invite_state()
    {
        var owner = Owner();

        Assert.Equal(AccountUserRole.Owner, owner.Role);
        Assert.Equal(MembershipStatus.Active, owner.MembershipStatus);
        Assert.True(owner.IsActive);
        Assert.NotNull(owner.UserId);
        Assert.Null(owner.InviteTokenHash);
    }

    [Fact]
    public void CreatePendingInvite_is_invited_with_no_user_link()
    {
        var invite = PendingInvite(AccountUserRole.Operator);

        Assert.Equal(MembershipStatus.Invited, invite.MembershipStatus);
        Assert.False(invite.IsActive);
        Assert.Null(invite.UserId);
        Assert.Equal("token-hash", invite.InviteTokenHash);
        Assert.Equal(AccountUserRole.Operator, invite.Role);
    }

    [Theory]
    [InlineData(AccountUserRole.Admin)]
    [InlineData(AccountUserRole.Operator)]
    [InlineData(AccountUserRole.Viewer)]
    public void CreatePendingInvite_allows_any_non_owner_role(AccountUserRole role)
    {
        var invite = PendingInvite(role);

        Assert.Equal(role, invite.Role);
    }

    [Fact]
    public void CreatePendingInvite_rejects_owner_role() =>
        Assert.Throws<ArgumentException>(() => PendingInvite(AccountUserRole.Owner));

    [Fact]
    public void CreatePendingInvite_rejects_mismatched_normalized_email() =>
        Assert.Throws<ArgumentException>(() => AccountUser.CreatePendingInvite(
            AccountId, "a@x.com", "different@x.com", AccountUserRole.Viewer,
            "hash", Now.AddDays(7), Now));

    [Fact]
    public void CreatePendingInvite_rejects_already_expired_invite() =>
        Assert.Throws<ArgumentException>(() => AccountUser.CreatePendingInvite(
            AccountId, "a@x.com", "a@x.com", AccountUserRole.Viewer,
            "hash", Now.AddMinutes(-1), Now));

    // --- Activate ---

    [Fact]
    public void Activate_links_user_clears_invite_and_stamps_activation()
    {
        var invite = PendingInvite();
        var userId = Guid.CreateVersion7();
        var at = Now;

        var result = invite.Activate(userId, at);

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipStatus.Active, invite.MembershipStatus);
        Assert.True(invite.IsActive);
        Assert.Equal(userId, invite.UserId);
        Assert.Null(invite.InviteTokenHash);
        Assert.Null(invite.InviteExpiresAtUtc);
        Assert.Equal(at, invite.ActivatedAtUtc);
    }

    [Fact]
    public void Activate_is_idempotent_when_already_active()
    {
        var owner = Owner();

        var result = owner.Activate(Guid.CreateVersion7(), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipStatus.Active, owner.MembershipStatus);
    }

    [Fact]
    public void Activate_is_rejected_for_a_removed_membership()
    {
        var invite = PendingInvite();
        invite.Remove();

        var result = invite.Activate(Guid.CreateVersion7(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountUserErrors.InvalidStatusTransition, result.Error);
    }

    [Fact]
    public void Activate_is_rejected_for_a_suspended_membership()
    {
        var invite = PendingInvite();
        invite.Activate(Guid.CreateVersion7(), Now);
        invite.Suspend();

        var result = invite.Activate(Guid.CreateVersion7(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountUserErrors.InvalidStatusTransition, result.Error);
    }

    // --- RefreshInvite ---

    [Fact]
    public void RefreshInvite_rotates_token_on_an_invited_membership()
    {
        var invite = PendingInvite();
        var newExpiry = Now.AddDays(14);

        var result = invite.RefreshInvite("new-hash", newExpiry, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", invite.InviteTokenHash);
        Assert.Equal(newExpiry, invite.InviteExpiresAtUtc);
    }

    [Fact]
    public void RefreshInvite_is_rejected_once_active()
    {
        var owner = Owner();

        var result = owner.RefreshInvite("new-hash", Now.AddDays(14), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountUserErrors.NotPendingInvite, result.Error);
    }

    // --- Suspend ---

    [Fact]
    public void Suspend_moves_an_active_membership_to_suspended()
    {
        var owner = Owner();

        var result = owner.Suspend();

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipStatus.Suspended, owner.MembershipStatus);
        Assert.False(owner.IsActive);
    }

    [Fact]
    public void Suspend_is_idempotent_when_already_suspended()
    {
        var owner = Owner();
        owner.Suspend();

        var result = owner.Suspend();

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipStatus.Suspended, owner.MembershipStatus);
    }

    [Fact]
    public void Suspend_is_rejected_for_an_invited_membership()
    {
        var invite = PendingInvite();

        var result = invite.Suspend();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountUserErrors.InvalidStatusTransition, result.Error);
    }

    [Fact]
    public void Suspend_is_rejected_for_a_removed_membership()
    {
        var owner = Owner();
        owner.Remove();

        var result = owner.Suspend();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountUserErrors.InvalidStatusTransition, result.Error);
    }

    // --- Remove ---

    [Fact]
    public void Remove_is_terminal_and_revokes_access()
    {
        var owner = Owner();

        var result = owner.Remove();

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipStatus.Removed, owner.MembershipStatus);
        Assert.False(owner.IsActive);
    }
}
