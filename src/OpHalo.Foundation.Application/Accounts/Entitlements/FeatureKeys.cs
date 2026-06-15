namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// The Foundation-owned catalog of <em>feature keys</em> (Phase 4d, §4.11 / ADR-009).
/// A feature key answers "is this <em>account</em> allowed this <em>capability</em>?" — kept
/// deliberately distinct from a <see cref="Authorization.PermissionKeys">permission key</see>
/// (Phase 4c), which answers "can this <em>user</em> perform this <em>action</em>?". The two
/// compose later (permitted <b>and</b> entitled); they never collapse into one catalog.
/// </summary>
/// <remarks>
/// Feature keys are boolean capability gates only. Numeric allowances live in the separate
/// <see cref="FeatureLimitKeys"/> catalog — a feature key answers "is it enabled?", a limit key
/// answers "what is the allowance?". Keys are plain strings (no Keep type references) so
/// Foundation owns the catalog without breaching the no-Keep-reference boundary (§8).
/// Convention: <c>domain.capability</c>. Capabilities are derived from
/// <see cref="OpHalo.Foundation.Core.Entities.Accounts.Enums.AccountPlan"/> by
/// <see cref="PlanEntitlements"/> — never stored as per-account booleans on the entity
/// (that recreates the dropped legacy halo-boolean model, ADR-028).
/// </remarks>
public static class FeatureKeys
{
    // All v1 feature keys are keep.* capabilities (build plan §4.11). Account-level capability
    // keys can be added here if a non-Keep capability ever needs gating.
    public static class Keep
    {
        /// <summary>Master switch — the Keep product surface is available at all.</summary>
        public const string Enabled = "keep.enabled";
        public const string PublicIntake = "keep.public_intake";
        public const string CustomerPage = "keep.customer_page";
        public const string OperatorQueue = "keep.operator_queue";
        public const string RequestDetail = "keep.request_detail";
        public const string OperatorMessaging = "keep.operator_messaging";
        public const string CustomerMessaging = "keep.customer_messaging";
        public const string InternalNotes = "keep.internal_notes";
        public const string CloseRequest = "keep.close_request";
        public const string SseLiveUpdates = "keep.sse_live_updates";
        public const string EmailNotifications = "keep.email_notifications";
        public const string BrowserPush = "keep.browser_push";
        public const string MobilePush = "keep.mobile_push";
        public const string RequestSubscriptions = "keep.request_subscriptions";
        public const string Insights = "keep.insights";
    }
}
