using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Phase 4c exit-gate matrix for ADR-007 "User is permitted": the role → permission map,
/// membership-status denial, and the composed <c>internal.*</c> purpose gate (legacy AdminGuard
/// semantic). Permission keys only — feature keys / usage limits (§4.11) are Phase 4d.
/// </summary>
public class UserAccessPolicyTests
{
    static readonly IUserAccessPolicy Policy = new UserAccessPolicy();

    static bool Permitted(
        string key,
        AccountUserRole role = AccountUserRole.Owner,
        MembershipStatus status = MembershipStatus.Active,
        AccountPurpose purpose = AccountPurpose.Business) =>
        Policy.IsPermitted(role, status, purpose, key);

    // --- Membership status: only Active is permitted anything ---

    [Theory]
    [InlineData(MembershipStatus.Invited)]
    [InlineData(MembershipStatus.Suspended)]
    [InlineData(MembershipStatus.Removed)]
    public void Non_active_membership_is_permitted_nothing(MembershipStatus status)
    {
        // Even an Owner — the broadest role — is denied permissions they would otherwise hold.
        Assert.False(Permitted(PermissionKeys.Account.View, AccountUserRole.Owner, status));
        Assert.False(Permitted(PermissionKeys.Account.BillingManage, AccountUserRole.Owner, status));
    }

    // --- Viewer: *.view only ---

    [Theory]
    [InlineData(PermissionKeys.Account.View, true)]
    [InlineData(PermissionKeys.Keep.RequestsView, true)]
    [InlineData(PermissionKeys.Keep.InsightsView, true)]
    [InlineData(PermissionKeys.Keep.RequestsCreate, false)]
    [InlineData(PermissionKeys.Account.SettingsManage, false)]
    [InlineData(PermissionKeys.Account.BillingManage, false)]
    public void Viewer_has_read_only_base_access(string key, bool expected) =>
        Assert.Equal(expected, Permitted(key, AccountUserRole.Viewer));

    // --- Operator: Viewer + Keep request actions, no account management ---

    [Theory]
    [InlineData(PermissionKeys.Keep.RequestsCreate, true)]
    [InlineData(PermissionKeys.Keep.RequestsUpdate, true)]
    [InlineData(PermissionKeys.Keep.RequestsClose, true)]
    [InlineData(PermissionKeys.Keep.RequestsRespond, true)]
    [InlineData(PermissionKeys.Keep.UpdatesSend, true)]
    [InlineData(PermissionKeys.Keep.CustomerMessagesSend, true)]
    [InlineData(PermissionKeys.Keep.InternalNotesAdd, true)]
    [InlineData(PermissionKeys.Keep.InsightsView, true)]        // locked: operators get operational insight
    [InlineData(PermissionKeys.Keep.SettingsManage, false)]
    [InlineData(PermissionKeys.Account.MembersManage, false)]
    public void Operator_has_request_actions_but_not_account_management(string key, bool expected) =>
        Assert.Equal(expected, Permitted(key, AccountUserRole.Operator));

    // --- Admin: Operator + account management + Keep settings, but not billing ---

    [Theory]
    [InlineData(PermissionKeys.Account.SettingsManage, true)]
    [InlineData(PermissionKeys.Account.MembersManage, true)]
    [InlineData(PermissionKeys.Account.NotificationsManage, true)]
    [InlineData(PermissionKeys.Account.AuditView, true)]
    [InlineData(PermissionKeys.Keep.SettingsManage, true)]
    [InlineData(PermissionKeys.Keep.RequestsCreate, true)]
    [InlineData(PermissionKeys.Account.BillingManage, false)]   // billing is Owner-only
    public void Admin_manages_account_but_not_billing(string key, bool expected) =>
        Assert.Equal(expected, Permitted(key, AccountUserRole.Admin));

    // --- Owner: everything Admin has, plus billing ---

    [Theory]
    [InlineData(PermissionKeys.Account.BillingManage)]
    [InlineData(PermissionKeys.Account.MembersManage)]
    [InlineData(PermissionKeys.Keep.RequestsRespond)]
    public void Owner_has_full_base_access_including_billing(string key) =>
        Assert.True(Permitted(key, AccountUserRole.Owner));

    // --- internal.* requires AccountPurpose == Internal (composed gate) ---

    [Theory]
    [InlineData(AccountUserRole.Owner)]
    [InlineData(AccountUserRole.Admin)]
    public void Internal_keys_are_denied_in_business_accounts(AccountUserRole role)
    {
        // No base-account role grants internal authority — purpose is the boundary, not the role.
        Assert.False(Permitted(PermissionKeys.Internal.AccountsView, role, purpose: AccountPurpose.Business));
        Assert.False(Permitted(PermissionKeys.Internal.AccountsManage, role, purpose: AccountPurpose.Business));
        Assert.False(Permitted(PermissionKeys.Internal.EntitlementsManage, role, purpose: AccountPurpose.Business));
    }

    [Fact]
    public void Internal_account_grants_internal_keys_by_role()
    {
        // Viewer: accounts.view only.
        Assert.True(Permitted(PermissionKeys.Internal.AccountsView, AccountUserRole.Viewer, purpose: AccountPurpose.Internal));
        Assert.False(Permitted(PermissionKeys.Internal.AccountsManage, AccountUserRole.Viewer, purpose: AccountPurpose.Internal));

        // Admin: + accounts.manage + entitlements.manage.
        Assert.True(Permitted(PermissionKeys.Internal.AccountsManage, AccountUserRole.Admin, purpose: AccountPurpose.Internal));
        Assert.True(Permitted(PermissionKeys.Internal.EntitlementsManage, AccountUserRole.Admin, purpose: AccountPurpose.Internal));
    }

    [Fact]
    public void Internal_keys_still_require_active_membership()
    {
        Assert.False(Permitted(
            PermissionKeys.Internal.AccountsManage,
            AccountUserRole.Owner,
            MembershipStatus.Suspended,
            AccountPurpose.Internal));
    }

    [Fact]
    public void Internal_account_members_retain_base_permissions()
    {
        // An Internal-purpose account is still an account: base account.*/keep.* apply by role.
        Assert.True(Permitted(PermissionKeys.Account.View, AccountUserRole.Viewer, purpose: AccountPurpose.Internal));
    }

    // --- Fail closed ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]                        // whitespace is not a canonical key
    [InlineData(" account.view ")]             // not trimmed — non-canonical, denied
    [InlineData("keep.requests.delete")]       // not in catalog
    [InlineData("internal.platform.manage")]   // deferred, not yet mapped
    [InlineData("totally.unknown.key")]
    public void Unknown_or_noncanonical_keys_are_denied(string key) =>
        Assert.False(Permitted(key, AccountUserRole.Owner, purpose: AccountPurpose.Internal));
}
