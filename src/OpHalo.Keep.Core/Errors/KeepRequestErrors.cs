using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class KeepRequestErrors
{
    public static readonly Error NotFound =
        Error.Create("KeepRequest.NotFound", "Request not found.");

    public static readonly Error Forbidden =
        Error.Create("KeepRequest.Forbidden", "You do not have permission to access this request.");

    public static readonly Error CustomerNameRequired =
        Error.Create("KeepRequest.CustomerNameRequired", "Customer name is required.");

    public static readonly Error CustomerNameTooLong =
        Error.Create("KeepRequest.CustomerNameTooLong", "Customer name must not exceed 200 characters.");

    public static readonly Error CustomerPhoneRequired =
        Error.Create("KeepRequest.CustomerPhoneRequired", "Customer phone is required.");

    public static readonly Error CustomerPhoneTooLong =
        Error.Create("KeepRequest.CustomerPhoneTooLong", "Customer phone must not exceed 50 characters.");

    public static readonly Error CustomerPhoneInvalidCharacters =
        Error.Create("KeepRequest.CustomerPhoneInvalidCharacters", "Customer phone contains unsupported characters. Digits, spaces, and the characters +, -, (, ), and . are allowed.");

    public static readonly Error CustomerPhoneInvalidFormat =
        Error.Create("KeepRequest.CustomerPhoneInvalidFormat", "Customer phone must contain between 7 and 15 digits.");

    public static readonly Error CustomerEmailTooLong =
        Error.Create("KeepRequest.CustomerEmailTooLong", "Customer email must not exceed 320 characters.");

    public static readonly Error CustomerEmailInvalid =
        Error.Create("KeepRequest.CustomerEmailInvalid", "Customer email is not a valid email address.");

    public static readonly Error DescriptionRequired =
        Error.Create("KeepRequest.DescriptionRequired", "Description is required.");

    public static readonly Error DescriptionTooLong =
        Error.Create("KeepRequest.DescriptionTooLong", "Description must not exceed 4000 characters.");

    public static readonly Error InvalidStatus =
        Error.Create("KeepRequest.InvalidStatus", "The provided status is not valid.");

    public static readonly Error InvalidStatusTransition =
        Error.Create("KeepRequest.InvalidStatusTransition", "The requested status transition is not allowed.");

    public static readonly Error MessageRequired =
        Error.Create("KeepRequest.MessageRequired", "A customer-visible message is required for this status.");

    public static readonly Error MessageTooLong =
        Error.Create("KeepRequest.MessageTooLong", "The message exceeds the maximum allowed length of 2000 characters.");

    public static readonly Error TerminalState =
        Error.Create("KeepRequest.TerminalState", "This request is in a terminal state and cannot be updated.");

    public static readonly Error BusinessUpdateMessageTooLong =
        Error.Create("KeepRequest.BusinessUpdateMessageTooLong", "The business update exceeds the maximum allowed length of 4000 characters.");

    public static readonly Error NoteRequired =
        Error.Create("KeepRequest.NoteRequired", "An internal note is required.");

    public static readonly Error NoteTooLong =
        Error.Create("KeepRequest.NoteTooLong", "The internal note exceeds the maximum allowed length of 4000 characters.");

    public static readonly Error AttentionReasonRequired =
        Error.Create("KeepRequest.AttentionReasonRequired", "An acknowledgement reason is required.");

    public static readonly Error AttentionReasonTooLong =
        Error.Create("KeepRequest.AttentionReasonTooLong", "The acknowledgement reason exceeds the maximum allowed length of 500 characters.");

    public static readonly Error AttentionNotRaised =
        Error.Create("KeepRequest.AttentionNotRaised", "There is no active attention to acknowledge.");

    public static readonly Error AttentionRequiresFeedbackReview =
        Error.Create("KeepRequest.AttentionRequiresFeedbackReview", "Unresolved feedback attention must be completed through feedback review, not generic acknowledgement.");

    public static readonly Error CustomerMessageTooLong =
        Error.Create("KeepRequest.CustomerMessageTooLong", "The customer message exceeds the maximum allowed length of 4000 characters.");

    public static readonly Error FeedbackResolutionRequired =
        Error.Create("KeepRequest.FeedbackResolutionRequired", "wasResolved is required to submit feedback.");

    public static readonly Error FeedbackCommentTooLong =
        Error.Create("KeepRequest.FeedbackCommentTooLong", "The feedback comment exceeds the maximum allowed length of 2000 characters.");

    public static readonly Error FeedbackUnavailable =
        Error.Create("KeepRequest.FeedbackUnavailable", "Feedback is only available on closed requests.");

    public static readonly Error FeedbackAlreadySubmitted =
        Error.Create("KeepRequest.FeedbackAlreadySubmitted", "Feedback has already been submitted for this request.");

    // External contact logging errors (ADR-207).
    public static readonly Error ExternalContactInvalidDirection =
        Error.Create("KeepRequest.ExternalContactInvalidDirection", "Direction must be 'outbound' or 'inbound'.");

    public static readonly Error ExternalContactInvalidOutboundChannel =
        Error.Create("KeepRequest.ExternalContactInvalidOutboundChannel", "Outbound external contact supports phone, SMS, and email only.");

    public static readonly Error ExternalContactInvalidInboundChannel =
        Error.Create("KeepRequest.ExternalContactInvalidInboundChannel", "Inbound external contact supports phone, SMS, email, in-person, and other.");

    public static readonly Error ExternalContactOutcomeRequired =
        Error.Create("KeepRequest.ExternalContactOutcomeRequired", "An outcome is required for outbound phone contact.");

    public static readonly Error ExternalContactOutcomeNotAllowed =
        Error.Create("KeepRequest.ExternalContactOutcomeNotAllowed", "An outcome may only be provided for outbound phone contact.");

    public static readonly Error ExternalContactFollowUpRequired =
        Error.Create("KeepRequest.ExternalContactFollowUpRequired", "Follow-up status is required for this contact type.");

    public static readonly Error ExternalContactFollowUpNotAllowed =
        Error.Create("KeepRequest.ExternalContactFollowUpNotAllowed", "Follow-up status does not apply to no-answer or wrong-number outcomes.");

    public static readonly Error ExternalContactSummaryRequired =
        Error.Create("KeepRequest.ExternalContactSummaryRequired", "A summary is required for this contact type.");

    public static readonly Error ExternalContactSummaryTooLong =
        Error.Create("KeepRequest.ExternalContactSummaryTooLong", "The summary exceeds the maximum allowed length of 4000 characters.");

    // ADR-221: customer-page writes blocked in OffSeason.
    public static readonly Error OffSeasonUnavailable =
        Error.Create("KeepRequest.OffSeasonUnavailable", "This business is not accepting updates through OpHalo right now. Please contact them directly.");

    // Participation errors (ADR-222..225).
    public static readonly Error ParticipationNoteTooLong =
        Error.Create("KeepRequest.ParticipationNoteTooLong", "The participation note exceeds the maximum allowed length of 4000 characters.");

    public static readonly Error ParticipationMuteRequiresActiveParticipation =
        Error.Create("KeepRequest.ParticipationMuteRequiresActiveParticipation", "You must be an active participant on this request to mute notifications.");

    public static readonly Error ParticipationCannotUnwatchResponsible =
        Error.Create("KeepRequest.ParticipationCannotUnwatchResponsible", "You are the Responsible user on this request, not a Watcher. Use the clear-responsible action instead.");

    public static readonly Error ParticipationResponsibleCannotWatch =
        Error.Create("KeepRequest.ParticipationResponsibleCannotWatch", "The target user is currently Responsible on this request. Clear their responsibility before adding them as a Watcher.");

    public static readonly Error ParticipationStateCorrupt =
        Error.Create("KeepRequest.ParticipationStateCorrupt", "Participant data is inconsistent and cannot be safely modified.");

    // Application-layer participation authorization errors (ADR-223 / Session 3B).
    public static readonly Error ParticipationTargetIneligible =
        Error.Create("KeepRequest.ParticipationTargetIneligible", "The specified user is not an eligible participant. Only active Owner, Admin, and Operator members of this account may be assigned or watched.");

    public static readonly Error ParticipationOperatorCannotAssignOther =
        Error.Create("KeepRequest.ParticipationOperatorCannotAssignOther", "Operators may not assign responsibility to other users. Only Owner or Admin may assign responsibility to another user.");

    public static readonly Error ParticipationOperatorCannotClear =
        Error.Create("KeepRequest.ParticipationOperatorCannotClear", "Operators may not clear responsibility. Only Owner or Admin may clear a Responsible user.");

    public static readonly Error ParticipationRequestAlreadyAssigned =
        Error.Create("KeepRequest.ParticipationRequestAlreadyAssigned", "This request is already assigned to an active Responsible user.");

    // Request list query validation errors (ADR-257/258, Session 4A).
    public static readonly Error RequestListInvalidView =
        Error.Create("KeepRequest.RequestListInvalidView", "The specified view is not recognized.");

    public static readonly Error RequestListViewNotYetAvailable =
        Error.Create("KeepRequest.RequestListViewNotYetAvailable", "This view is not yet available. Use 'default' or omit the view parameter.");

    public static readonly Error RequestListFilterNotYetAvailable =
        Error.Create("KeepRequest.RequestListFilterNotYetAvailable", "Filter and search parameters are not yet available. Omit status, attentionReason, assignedAccountUserId, q, and date range parameters.");

    public static readonly Error RequestListInvalidLimit =
        Error.Create("KeepRequest.RequestListInvalidLimit", "The limit is outside the allowed range.");

    public static readonly Error RequestListInvalidCursor =
        Error.Create("KeepRequest.RequestListInvalidCursor", "The cursor is invalid, malformed, or does not match the current query.");

    public static readonly Error RequestListInvalidDateFormat =
        Error.Create("KeepRequest.RequestListInvalidDateFormat", "Dates must be full ISO-8601/RFC3339 timestamps with UTC or an explicit offset.");

    public static readonly Error RequestListInvalidClosedShortcut =
        Error.Create("KeepRequest.RequestListInvalidClosedShortcut", "The closedShortcut value is not recognized. Valid values are: yesterday, this_week.");

    public static readonly Error RequestListContradictoryParameters =
        Error.Create("KeepRequest.RequestListContradictoryParameters", "The provided query parameters are contradictory and cannot be combined.");

    // Request list binder errors (Session 4A).
    public static readonly Error RequestListInvalidAssignedAccountUserId =
        Error.Create("KeepRequest.RequestListInvalidAssignedAccountUserId", "The provided assignedAccountUserId is not a valid identifier.");

    public static readonly Error RequestListUnknownParameter =
        Error.Create("KeepRequest.RequestListUnknownParameter", "One or more query parameters are not recognized.");

    public static readonly Error RequestListDuplicateParameter =
        Error.Create("KeepRequest.RequestListDuplicateParameter", "A query parameter was supplied more than once. Each parameter must appear at most once.");

    // Feedback review errors (ADR-276, Session 5).
    public static readonly Error FeedbackReviewUnavailable =
        Error.Create("KeepRequest.FeedbackReviewUnavailable", "Feedback review is not available for this request. The request must be closed with unreviewed negative feedback and an active unresolved-feedback review state.");

    public static readonly Error FeedbackAlreadyReviewed =
        Error.Create("KeepRequest.FeedbackAlreadyReviewed", "Feedback has already been reviewed for this request.");

    public static readonly Error FeedbackReviewNoteTooLong =
        Error.Create("KeepRequest.FeedbackReviewNoteTooLong", "The review note exceeds the maximum allowed length of 2000 characters.");

    // Request list filter/view validation errors (Session 4B).
    public static readonly Error RequestListInvalidStatus =
        Error.Create("KeepRequest.RequestListInvalidStatus", "The provided status filter value is not recognized.");

    public static readonly Error RequestListInvalidAttentionReason =
        Error.Create("KeepRequest.RequestListInvalidAttentionReason", "The provided attentionReason filter value is not recognized.");

    public static readonly Error RequestListHistoryViewForbidden =
        Error.Create("KeepRequest.RequestListHistoryViewForbidden", "This view requires Owner or Admin access.");

    // Optimistic concurrency (G5/ADR-330–334). Header enforcement and conflict behavior
    // are wired by G5b–d; the constants are defined here as the shared foundation.
    public static readonly Error ExpectedVersionRequired =
        Error.Create("KeepRequest.ExpectedVersionRequired", "An expected request version is required.");

    public static readonly Error ExpectedVersionInvalid =
        Error.Create("KeepRequest.ExpectedVersionInvalid", "The expected request version is not a valid version value.");

    public static readonly Error RequestChanged =
        Error.Create("KeepRequest.RequestChanged", "The request changed. Refresh and try again.");

    // Follow Up On errors (ADR-337, P6b-1).
    public static readonly Error FollowUpOnRequiresActiveRequest =
        Error.Create("KeepRequest.FollowUpOnRequiresActiveRequest", "Follow Up On can only be set or changed on active requests.");

    public static readonly Error FollowUpOnReasonRequired =
        Error.Create("KeepRequest.FollowUpOnReasonRequired", "A reason is required for Follow Up On.");

    public static readonly Error FollowUpOnNoteRequired =
        Error.Create("KeepRequest.FollowUpOnNoteRequired", "A note is required when the Follow Up reason is 'other'.");

    public static readonly Error FollowUpOnNoteTooLong =
        Error.Create("KeepRequest.FollowUpOnNoteTooLong", "The Follow Up On note exceeds the maximum allowed length of 500 characters.");

    // Planned For errors (ADR-338, P6b-1).
    public static readonly Error PlannedForRequiresActiveRequest =
        Error.Create("KeepRequest.PlannedForRequiresActiveRequest", "Planned For can only be set or changed on active requests.");

    // Timing mutation API-edge errors (P6b-2).
    public static readonly Error InvalidDateFormat =
        Error.Create("KeepRequest.InvalidDateFormat", "Date must be a valid calendar date in yyyy-MM-dd format.");

    // Close permission errors (P6f-1 / ADR-343).
    public static readonly Error CloseRequiresOwnerOrAdmin =
        Error.Create("KeepRequest.CloseRequiresOwnerOrAdmin", "Only an Owner or Admin can close a request.");

    public static readonly Error CloseBlockedByAttention =
        Error.Create("KeepRequest.CloseBlockedByAttention", "Active attention must be resolved before closing this request.");

    // Detail navigation errors (P6f-4).
    public static readonly Error RequestDetailInvalidNavView =
        Error.Create("KeepRequest.RequestDetailInvalidNavView", "The specified navView is not recognized. Supported values are: ready_to_close.");

    // Service location errors (S22d).
    public static readonly Error ServiceAddressLine1Required =
        Error.Create("KeepRequest.ServiceAddressLine1Required", "Service address is required.");

    public static readonly Error ServiceCityRequired =
        Error.Create("KeepRequest.ServiceCityRequired", "Service city is required.");

    public static readonly Error ServiceStateRequired =
        Error.Create("KeepRequest.ServiceStateRequired", "Service state is required.");

    public static readonly Error ServiceStateInvalid =
        Error.Create("KeepRequest.ServiceStateInvalid", "Service state must be a valid US state code.");

    // Follow-up resolution errors (ADR-440, S83b).
    public static readonly Error FollowUpOnNotSet =
        Error.Create("KeepRequest.FollowUpOnNotSet", "No Follow Up On date is set. There is nothing to resolve.");

    public static readonly Error FollowUpOnInvalidOutcome =
        Error.Create("KeepRequest.FollowUpOnInvalidOutcome", "Outcome must be 'complete', 'move', or 'keep_active'.");

    public static readonly Error FollowUpOnCompletionReasonRequired =
        Error.Create("KeepRequest.FollowUpOnCompletionReasonRequired", "A completion reason is required.");

    public static readonly Error FollowUpOnMoveRequiresDate =
        Error.Create("KeepRequest.FollowUpOnMoveRequiresDate", "A new date is required when moving a follow-up.");

    // Spam/Test classification errors (ADR-349/350, S7e).
    public static readonly Error ClassificationRequiresOwnerOrAdmin =
        Error.Create("KeepRequest.ClassificationRequiresOwnerOrAdmin", "Only an Owner or Admin can classify a request as Spam or Test.");

    public static readonly Error InvalidClassification =
        Error.Create("KeepRequest.InvalidClassification", "The classification target is not valid. Supported values are: spam, test.");

    public static readonly Error ClassificationReasonTooLong =
        Error.Create("KeepRequest.ClassificationReasonTooLong", "The classification reason exceeds the maximum allowed length of 500 characters.");
}
