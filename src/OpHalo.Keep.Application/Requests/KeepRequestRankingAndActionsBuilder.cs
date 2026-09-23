using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Maintainability review item 4: the ranking-group, severity, and
/// quick-action/contact-action-building logic previously lived as private static methods on
/// <see cref="GetKeepRequestListService"/>. They are pure functions of a <see cref="KeepRequest"/>
/// plus the caller's pre-computed presentation booleans (post-close, first-response
/// pending/overdue, due/overdue FollowUpOn) and have no dependency on the service's persistence,
/// authorization, or clock collaborators, so they are extracted here as an internal static
/// collaborator rather than a DI-injected service — same shape as <see cref="KeepRequestWireMappers"/>
/// (item 2.2). <c>ComputeRowContext</c> deliberately stays on <see cref="GetKeepRequestListService"/>:
/// it is a distinct UI-row-grouping concern, not named alongside ranking/severity/quick-actions in
/// the workboard's maintainability-review item, and left there for this slice.
/// </summary>
internal static class KeepRequestRankingAndActionsBuilder
{
    internal static (string Group, int Order) ComputeRankingGroup(
        KeepRequest r,
        bool isPostClose,
        bool firstResponsePending,
        bool firstResponseOverdue,
        bool overdueBusinessWaiting,
        bool isDueOrOverdueFollowUpOn)
    {
        if (overdueBusinessWaiting || firstResponseOverdue)
            return ("overdue_business_waiting", 1);

        // Post-close checked before priority and urgent so closed-state requests never
        // accidentally inherit a higher ranking bucket.
        if (isPostClose)
            return ("post_close_unresolved_feedback", 4);

        if (r.PriorityBand == PriorityBand.Priority
            && r.WaitingDirection == WaitingDirection.Business)
            return ("priority_business_waiting", 2);

        if (IsEffectivelyUrgent(r)
            && r.Status is not KeepRequestStatus.PendingCustomer
            && r.Status is not KeepRequestStatus.Resolved
            && !r.IsTerminal)
            return ("customer_urgent_active", 3);

        if (r.WaitingDirection == WaitingDirection.Business)
            return ("standard_business_waiting", 5);

        // ADR-439: due/overdue FollowUpOn with no stronger persisted attention ranks as
        // active promise work alongside standard business-waiting rows (order 5).
        if (isDueOrOverdueFollowUpOn)
            return ("due_follow_up_on", 5);

        if (firstResponsePending)
            return ("first_response_pending", 6);

        if (r.Status == KeepRequestStatus.PendingCustomer)
            return ("waiting_on_customer", 7);

        if (r.Status == KeepRequestStatus.Resolved && r.AttentionLevel == AttentionLevel.None)
            return ("resolved_quiet", 8);

        return ("active", 9);
    }

    internal static string ComputeSeverity(
        KeepRequest r,
        bool isOverdue,
        bool isPostClose,
        bool firstResponsePending,
        bool isDueOrOverdueFollowUpOn,
        bool isFollowUpOverdue)
    {
        if (isOverdue || isPostClose)
            return "danger";

        if (r.AttentionReason is AttentionReason.Complaint
            or AttentionReason.ScheduleChangeRequest
            or AttentionReason.ChangeOrCancelRequest)
            return "danger";

        // ADR-439: overdue follow-up is danger; due today is attention.
        if (isFollowUpOverdue)
            return "danger";

        if (r.PriorityBand == PriorityBand.Priority && r.WaitingDirection == WaitingDirection.Business)
            return "priority";

        if (r.WaitingDirection == WaitingDirection.Business)
            return "attention";

        if (firstResponsePending || isDueOrOverdueFollowUpOn)
            return "attention";

        if (r.Status == KeepRequestStatus.PendingCustomer
            || (r.Status == KeepRequestStatus.Resolved && r.AttentionLevel == AttentionLevel.None))
            return "neutral";

        return "muted";
    }

    internal static IReadOnlyList<KeepQuickAction> BuildQuickActions(
        KeepRequest r,
        bool canOperate,
        bool isPostClose,
        bool firstResponseOverdue,
        KeepRequestActionDecision actionDecision)
    {
        var openDetail = QuickActionDefs.OpenDetail;

        if (isPostClose)
        {
            // ReviewFeedback and ContactCustomer gated by policy; suppressed for OffSeason and non-Owner/Admin (ADR-328 / G7b).
            var postCloseActions = new List<KeepQuickAction> { openDetail };
            if (actionDecision.CanMarkFeedbackReviewed)
                postCloseActions.Add(QuickActionDefs.ReviewFeedback);
            var postCloseHasContact = !string.IsNullOrWhiteSpace(r.CustomerPhone)
                || !string.IsNullOrWhiteSpace(r.CustomerEmail);
            if (postCloseHasContact && actionDecision.CanLogExternalContact)
                postCloseActions.Add(QuickActionDefs.ContactCustomer);
            return postCloseActions;
        }

        if (!canOperate)
            return [openDetail];

        if (r.IsTerminal)
            return [openDetail];

        var actions = new List<KeepQuickAction> { openDetail };

        // CanLogExternalContact gates the contact quick action (ADR-328).
        var hasContactMethods = !string.IsNullOrWhiteSpace(r.CustomerPhone)
            || !string.IsNullOrWhiteSpace(r.CustomerEmail);
        if (hasContactMethods && actionDecision.CanLogExternalContact)
            actions.Add(QuickActionDefs.ContactCustomer);

        // CanSendBusinessUpdate gates the customer update quick action (ADR-328).
        if (actionDecision.CanSendBusinessUpdate)
        {
            actions.Add(new KeepQuickAction(
                "post_customer_update", "Update customer page", "customer_visible",
                RequiresVersion: true,
                ExecutionMode: "modal",
                ClearsAttention: r.WaitingDirection == WaitingDirection.Business && r.AttentionLevel != AttentionLevel.None,
                CountsFirstResponse: false,
                ChangesStatus: false,
                EffectSummaryCode: "customer_visible_status_unchanged"));
        }

        if (actionDecision.CanAddInternalNote)
            actions.Add(QuickActionDefs.AddInternalNote);

        // Policy-derived; first-response-overdue suppression is a presentation condition (ADR-328).
        var isFirstResponseOverdueNoResponse = firstResponseOverdue && r.FirstRespondedAtUtc is null;
        if (actionDecision.CanAcknowledgeAttention && !isFirstResponseOverdueNoResponse)
            actions.Add(QuickActionDefs.AcknowledgeAttention);

        // Terminal action rightmost: triage cue appears after communication/admin tools (GAP-011).
        if (actionDecision.CanClose)
            actions.Add(QuickActionDefs.CloseRequest);

        return actions;
    }

    internal static IReadOnlyList<ContactActionItem> BuildContactActions(
        KeepRequest r, bool canOperate, bool isPostClose, KeepRequestActionDecision actionDecision)
    {
        if (!canOperate || !actionDecision.CanLogExternalContact)
            return [];

        var actions = new List<ContactActionItem>();

        if (!string.IsNullOrWhiteSpace(r.CustomerPhone))
            actions.Add(new ContactActionItem("call", true, r.CustomerPhone));

        if (!string.IsNullOrWhiteSpace(r.CustomerEmail))
            actions.Add(new ContactActionItem("email", true, r.CustomerEmail));

        return actions;
    }

    private static bool IsEffectivelyUrgent(KeepRequest r) =>
        r.BusinessPriority.HasValue
            ? r.BusinessPriority.Value == BusinessPriority.Urgent
            : r.IntakeUrgency == IntakeUrgency.Urgent;

    private static class QuickActionDefs
    {
        public static readonly KeepQuickAction OpenDetail = new(
            "open_detail", "Open detail", "internal",
            RequiresVersion: false, ExecutionMode: "detail",
            false, false, false, "opens_detail");

        public static readonly KeepQuickAction ContactCustomer = new(
            "contact_customer", "Log contact", "external_affordance",
            RequiresVersion: true, ExecutionMode: "modal",
            false, false, false, "external_contact_only");

        public static readonly KeepQuickAction AcknowledgeAttention = new(
            "acknowledge_attention", "Mark handled", "internal",
            RequiresVersion: true, ExecutionMode: "modal",
            true, false, false, "internal_clears_attention");

        public static readonly KeepQuickAction AddInternalNote = new(
            "add_internal_note", "Add note", "internal",
            RequiresVersion: true, ExecutionMode: "modal",
            false, false, false, "internal_note_only");

        public static readonly KeepQuickAction ReviewFeedback = new(
            "review_feedback", "Review feedback", "internal",
            RequiresVersion: false, ExecutionMode: "detail",
            false, false, false, "opens_detail_feedback");

        public static readonly KeepQuickAction CloseRequest = new(
            "close_request", "Close request", "internal",
            RequiresVersion: true, ExecutionMode: "detail",
            false, false, true, "closes_request");
    }
}
