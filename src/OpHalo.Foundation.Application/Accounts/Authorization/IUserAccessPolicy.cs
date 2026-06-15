using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Authorization;

/// <summary>
/// ADR-007 "User is permitted" — the user-level authorization counterpart to the account-level
/// <see cref="Access.IAccountAccessPolicy"/>. Answers whether a member, given their role,
/// membership standing, and account purpose, may perform the action named by a
/// <see cref="PermissionKeys">permission key</see>.
/// </summary>
public interface IUserAccessPolicy
{
    bool IsPermitted(
        AccountUserRole role,
        MembershipStatus membershipStatus,
        AccountPurpose accountPurpose,
        string permissionKey);
}
