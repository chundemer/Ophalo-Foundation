using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Pure O(1) Application-layer action policy for Keep requests (ADR-326–329).
/// No EF, current-user, HTTP, network, or clock dependency.
/// Evaluate after row authorization; this policy never grants row access.
/// </summary>
public static class KeepRequestActionPolicy
{
    /// <summary>Canonical deny-all decision for invalid or insufficient actor context.</summary>
    public static readonly KeepRequestActionDecision DenyAll = new(
        CanChangeStatus:          false,
        CanSendBusinessUpdate:    false,
        CanAddInternalNote:       false,
        CanAcknowledgeAttention:  false,
        CanLogExternalContact:    false,
        CanAssignResponsible:     false,
        CanSelfAssignResponsible: false,
        CanClearResponsible:      false,
        CanManageWatchers:        false,
        CanWatch:                 false,
        CanUnwatch:               false,
        CanMute:                  false,
        CanUnmute:                false,
        CanMarkFeedbackReviewed:  false,
        CanSetFollowUpOn:         false,
        CanSetPlannedFor:         false,
        CanClose:                 false,
        AllowedStatuses:          []);

    public static KeepRequestActionDecision Evaluate(KeepRequest request, KeepRequestActionContext actor)
    {
        // Fail-closed: Viewer, unknown/future role, disabled writes, unknown participation value,
        // or inconsistent participation/notification context.
        if (actor.Role is AccountUserRole.Viewer)
            return DenyAll;

        if (actor.Role is not (AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Operator))
            return DenyAll;

        if (!actor.CanWrite)
            return DenyAll;

        // Unknown/future ParticipationType values fail closed (prevents enum extension bypass).
        if (actor.ActiveParticipation is { } p && !Enum.IsDefined(p))
            return DenyAll;

        // Participation and notification state must be jointly null or jointly set.
        if (actor.NotificationsEnabled.HasValue != (actor.ActiveParticipation != null))
            return DenyAll;

        var isOwnerAdmin  = actor.Role is AccountUserRole.Owner or AccountUserRole.Admin;
        var isOperator    = actor.Role is AccountUserRole.Operator;
        var isNonTerminal = !request.IsTerminal;
        var hasAttention  = request.AttentionLevel != AttentionLevel.None
                         && request.AttentionReason != AttentionReason.UnresolvedFeedback;
        var participation = actor.ActiveParticipation;
        var notifEnabled  = actor.NotificationsEnabled;

        // Mute/unmute: non-terminal, active participation, consistent notification flag.
        var canMute   = isNonTerminal && participation != null && notifEnabled == true;
        var canUnmute = isNonTerminal && participation != null && notifEnabled == false;

        // Watch: non-terminal, no current participation.
        // Unwatch: non-terminal, currently Watching.
        var canWatch   = isNonTerminal && participation == null;
        var canUnwatch = isNonTerminal && participation == ParticipationType.Watching;

        // Timing mutations: active requests only (Resolved/Closed/Cancelled all rejected by domain).
        // CanWrite already incorporates OffSeason freeze; Operator row access enforced at service layer.
        var canSetTiming = actor.CanWrite
            && request.Status is not (KeepRequestStatus.Resolved
                                      or KeepRequestStatus.Closed
                                      or KeepRequestStatus.Cancelled);

        // Close: Owner/Admin only (ADR-343); requires Resolved + no active blocking attention.
        // Operator row access and domain checks remain authoritative at execution time.
        var canClose = isOwnerAdmin
            && request.Status == KeepRequestStatus.Resolved
            && request.AttentionLevel == AttentionLevel.None;

        return new KeepRequestActionDecision(
            CanChangeStatus:          isNonTerminal,
            CanSendBusinessUpdate:    isNonTerminal,
            CanAddInternalNote:       true,
            CanAcknowledgeAttention:  hasAttention,
            CanLogExternalContact:    isNonTerminal || (isOwnerAdmin && request.HasActiveUnresolvedFeedbackReview),
            CanAssignResponsible:     isOwnerAdmin && isNonTerminal,
            CanSelfAssignResponsible: isOperator && isNonTerminal,
            CanClearResponsible:      isOwnerAdmin && isNonTerminal,
            CanManageWatchers:        isOwnerAdmin && isNonTerminal,
            CanWatch:                 canWatch,
            CanUnwatch:               canUnwatch,
            CanMute:                  canMute,
            CanUnmute:                canUnmute,
            CanMarkFeedbackReviewed:  CanMarkFeedbackReviewedCore(isOwnerAdmin, request),
            CanSetFollowUpOn:         canSetTiming,
            CanSetPlannedFor:         canSetTiming,
            CanClose:                 canClose,
            AllowedStatuses:          ComputeAllowedStatuses(request.Status, isOwnerAdmin, canClose));
    }

    private static bool CanMarkFeedbackReviewedCore(bool isOwnerAdmin, KeepRequest request) =>
        isOwnerAdmin
        && request.Status == KeepRequestStatus.Closed
        && request.FeedbackSubmittedAtUtc.HasValue
        && request.FeedbackWasResolved == false
        && !request.FeedbackReviewedAtUtc.HasValue
        && request.AttentionLevel != AttentionLevel.None
        && request.AttentionReason == AttentionReason.UnresolvedFeedback;

    // Actual transitions only; current status excluded. Same-status no-ops remain domain-authoritative.
    // Closed appears only when canClose is true: Operators never see it; Owner/Admin only when
    // CanClose is true (Resolved + no active blocking attention — ADR-343).
    private static IReadOnlyList<KeepRequestStatus> ComputeAllowedStatuses(
        KeepRequestStatus current, bool isOwnerAdmin, bool canClose) =>
        current switch
        {
            KeepRequestStatus.Received =>
                [KeepRequestStatus.Scheduled, KeepRequestStatus.InProgress,
                 KeepRequestStatus.PendingCustomer, KeepRequestStatus.Resolved,
                 KeepRequestStatus.Cancelled],

            KeepRequestStatus.Scheduled =>
                [KeepRequestStatus.InProgress, KeepRequestStatus.PendingCustomer,
                 KeepRequestStatus.Resolved, KeepRequestStatus.Cancelled],

            KeepRequestStatus.InProgress =>
                [KeepRequestStatus.Scheduled, KeepRequestStatus.PendingCustomer,
                 KeepRequestStatus.Resolved, KeepRequestStatus.Cancelled],

            KeepRequestStatus.PendingCustomer =>
                [KeepRequestStatus.Scheduled, KeepRequestStatus.InProgress,
                 KeepRequestStatus.Resolved, KeepRequestStatus.Cancelled],

            KeepRequestStatus.Resolved when canClose =>
                [KeepRequestStatus.InProgress, KeepRequestStatus.PendingCustomer,
                 KeepRequestStatus.Closed, KeepRequestStatus.Cancelled],

            KeepRequestStatus.Resolved =>
                [KeepRequestStatus.InProgress, KeepRequestStatus.PendingCustomer,
                 KeepRequestStatus.Cancelled],

            KeepRequestStatus.Closed or KeepRequestStatus.Cancelled =>
                [],

            _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {current}")
        };
}
