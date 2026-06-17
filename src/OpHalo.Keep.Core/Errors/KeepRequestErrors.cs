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

    public static readonly Error CustomerPhoneRequired =
        Error.Create("KeepRequest.CustomerPhoneRequired", "Customer phone is required.");

    public static readonly Error DescriptionRequired =
        Error.Create("KeepRequest.DescriptionRequired", "Description is required.");

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
}
