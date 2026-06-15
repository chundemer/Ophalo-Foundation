namespace OpHalo.Foundation.Application.Accounts.Authorization;

/// <summary>
/// The Foundation-owned catalog of permission keys (Phase 4c, §4.8 / ADR-007 "User is permitted").
/// A permission answers "can this <em>user</em> perform this <em>action</em>?" — kept deliberately
/// distinct from a feature key (§4.11, Phase 4d), which answers "is this <em>account</em> allowed
/// this capability?". The two compose later; they never collapse into one catalog.
/// </summary>
/// <remarks>
/// Keys are plain strings (no Keep type references) so Foundation can own the catalog without
/// breaching the no-Keep-reference boundary (§8). Convention: <c>domain.resource.action</c>.
/// </remarks>
public static class PermissionKeys
{
    /// <summary>
    /// Prefix marking keys whose authority is composed with the account's purpose boundary:
    /// they are honored only for an <c>AccountPurpose.Internal</c> account (legacy AdminGuard semantic).
    /// </summary>
    public const string InternalPrefix = "internal.";

    public static class Account
    {
        public const string View = "account.view";
        public const string SettingsManage = "account.settings.manage";
        public const string MembersManage = "account.members.manage";
        public const string NotificationsManage = "account.notifications.manage";
        public const string AuditView = "account.audit.view";
        public const string BillingManage = "account.billing.manage";
    }

    // keep.* permission keys are user ACTIONS only. Account-level capability gates
    // (keep.enabled, keep.public_intake, …) are FEATURE keys and belong to Phase 4d, not here.
    public static class Keep
    {
        public const string RequestsView = "keep.requests.view";
        public const string RequestsCreate = "keep.requests.create";
        public const string RequestsUpdate = "keep.requests.update";
        public const string RequestsClose = "keep.requests.close";
        public const string RequestsRespond = "keep.requests.respond";
        public const string UpdatesSend = "keep.updates.send";
        public const string CustomerMessagesSend = "keep.customer_messages.send";
        public const string InternalNotesAdd = "keep.internal_notes.add";
        public const string InsightsView = "keep.insights.view";
        public const string SettingsManage = "keep.settings.manage";
    }

    // internal.* keys require AccountPurpose.Internal (see InternalPrefix). Broader
    // support/platform keys (internal.support.*, internal.platform.manage) are deferred.
    public static class Internal
    {
        public const string AccountsView = "internal.accounts.view";
        public const string AccountsManage = "internal.accounts.manage";
        public const string EntitlementsManage = "internal.entitlements.manage";
    }
}
