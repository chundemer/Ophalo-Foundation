using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Authorization;

/// <summary>
/// Evaluates ADR-007 "User is permitted" as a composition of role + membership status + account
/// purpose (Phase 4c). Three gates, in order:
/// <list type="number">
/// <item>Only <see cref="MembershipStatus.Active"/> members are permitted anything.</item>
/// <item><c>internal.*</c> keys additionally require <see cref="AccountPurpose.Internal"/> —
/// preserving the legacy AdminGuard semantic that internal authority comes from the account's
/// purpose boundary, not from a role name alone.</item>
/// <item>The key is then checked against the explicit role → permission-set map.</item>
/// </list>
/// Fail-closed: an empty, whitespace, unknown, or unmapped key is denied. Keys are matched
/// exactly (no trimming) — callers are responsible for passing canonical permission keys.
/// </summary>
public sealed class UserAccessPolicy : IUserAccessPolicy
{
    public bool IsPermitted(
        AccountUserRole role,
        MembershipStatus membershipStatus,
        AccountPurpose accountPurpose,
        string permissionKey)
    {
        if (string.IsNullOrWhiteSpace(permissionKey))
            return false;

        // Gate 1 — only active members hold any permission.
        if (membershipStatus != MembershipStatus.Active)
            return false;

        // Gate 2 + 3 — internal.* keys compose the purpose boundary with the internal role map.
        if (permissionKey.StartsWith(PermissionKeys.InternalPrefix, StringComparison.Ordinal))
        {
            return accountPurpose == AccountPurpose.Internal
                && RolePermissions.Internal.TryGetValue(role, out var internalSet)
                && internalSet.Contains(permissionKey);
        }

        // Gate 3 — base account.*/keep.* keys evaluate against the role map for any account.
        return RolePermissions.Base.TryGetValue(role, out var baseSet)
            && baseSet.Contains(permissionKey);
    }
}
