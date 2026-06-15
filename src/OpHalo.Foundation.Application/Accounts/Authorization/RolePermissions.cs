using System.Collections.Frozen;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Authorization;

/// <summary>
/// The role → permission-set maps (Phase 4c). The hierarchy Owner ⊇ Admin ⊇ Operator ⊇ Viewer
/// is expressed by <em>explicit set composition</em>, never by enum numeric comparison —
/// <see cref="AccountUserRole"/> values are identity, not authorization arithmetic.
/// </summary>
/// <remarks>
/// Two maps, consulted by <see cref="UserAccessPolicy"/>:
/// <list type="bullet">
/// <item><b>Base</b> — <c>account.*</c> / <c>keep.*</c>, applies to every account regardless of purpose.</item>
/// <item><b>Internal</b> — <c>internal.*</c>, consulted only for an <c>AccountPurpose.Internal</c> account,
/// using the same role hierarchy.</item>
/// </list>
/// </remarks>
public static class RolePermissions
{
    // --- Base (account.* / keep.*) — every account, by role ---

    static readonly string[] ViewerBase =
    [
        PermissionKeys.Account.View,
        PermissionKeys.Keep.RequestsView,
        PermissionKeys.Keep.InsightsView,   // operational visibility — locked for Operator+ incl. Viewer
    ];

    static readonly string[] OperatorBase =
    [
        .. ViewerBase,
        PermissionKeys.Keep.RequestsCreate,
        PermissionKeys.Keep.RequestsUpdate,
        PermissionKeys.Keep.RequestsClose,
        PermissionKeys.Keep.RequestsRespond,
        PermissionKeys.Keep.UpdatesSend,
        PermissionKeys.Keep.CustomerMessagesSend,
        PermissionKeys.Keep.InternalNotesAdd,
    ];

    static readonly string[] AdminBase =
    [
        .. OperatorBase,
        PermissionKeys.Account.SettingsManage,
        PermissionKeys.Account.MembersManage,
        PermissionKeys.Account.NotificationsManage,
        PermissionKeys.Account.AuditView,
        PermissionKeys.Keep.SettingsManage,
    ];

    static readonly string[] OwnerBase =
    [
        .. AdminBase,
        PermissionKeys.Account.BillingManage,   // billing is ownership-level authority — Owner only
    ];

    // --- Internal (internal.*) — only honored when AccountPurpose == Internal ---
    // Broader internal.support.* / internal.platform.manage keys are deferred (no caller yet).
    // Operator currently mirrors Viewer and Owner mirrors Admin here until those keys land.

    static readonly string[] ViewerInternal =
    [
        PermissionKeys.Internal.AccountsView,
    ];

    static readonly string[] OperatorInternal =
    [
        .. ViewerInternal,
    ];

    static readonly string[] AdminInternal =
    [
        .. OperatorInternal,
        PermissionKeys.Internal.AccountsManage,
        PermissionKeys.Internal.EntitlementsManage,
    ];

    static readonly string[] OwnerInternal =
    [
        .. AdminInternal,
    ];

    /// <summary>Role → <c>account.*</c>/<c>keep.*</c> permissions, for every account.</summary>
    public static readonly FrozenDictionary<AccountUserRole, FrozenSet<string>> Base =
        new Dictionary<AccountUserRole, FrozenSet<string>>
        {
            [AccountUserRole.Viewer] = ViewerBase.ToFrozenSet(StringComparer.Ordinal),
            [AccountUserRole.Operator] = OperatorBase.ToFrozenSet(StringComparer.Ordinal),
            [AccountUserRole.Admin] = AdminBase.ToFrozenSet(StringComparer.Ordinal),
            [AccountUserRole.Owner] = OwnerBase.ToFrozenSet(StringComparer.Ordinal),
        }.ToFrozenDictionary();

    /// <summary>Role → <c>internal.*</c> permissions, honored only for Internal-purpose accounts.</summary>
    public static readonly FrozenDictionary<AccountUserRole, FrozenSet<string>> Internal =
        new Dictionary<AccountUserRole, FrozenSet<string>>
        {
            [AccountUserRole.Viewer] = ViewerInternal.ToFrozenSet(StringComparer.Ordinal),
            [AccountUserRole.Operator] = OperatorInternal.ToFrozenSet(StringComparer.Ordinal),
            [AccountUserRole.Admin] = AdminInternal.ToFrozenSet(StringComparer.Ordinal),
            [AccountUserRole.Owner] = OwnerInternal.ToFrozenSet(StringComparer.Ordinal),
        }.ToFrozenDictionary();
}
