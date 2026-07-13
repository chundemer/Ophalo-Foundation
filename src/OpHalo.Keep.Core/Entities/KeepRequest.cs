using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A service request submitted through Keep. Customer contact info is denormalized
/// at creation so operator views remain stable if the customer record is updated later.
/// </summary>
public sealed class KeepRequest : BaseEntity
{
    public Guid AccountId { get; private set; }
    public Guid KeepCustomerId { get; private set; }

    // Denormalized at creation time — intentionally independent of KeepCustomer updates.
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string? CustomerEmail { get; private set; }

    public string Description { get; private set; } = string.Empty;
    public string? CurrentStatusText { get; private set; }
    public KeepRequestStatus Status { get; private set; } = KeepRequestStatus.Received;

    // ReferenceCode: short human-readable identifier (e.g. PQRS7842), account-scoped unique.
    // PageToken: high-entropy opaque token for public request page access.
    public string ReferenceCode { get; private set; } = string.Empty;
    public string PageToken { get; private set; } = string.Empty;

    // D7/ADR-090: who originated the request (Customer vs Business).
    public KeepRequestOrigin Origin { get; private set; } = KeepRequestOrigin.Customer;

    // ADR-369: channel through which the request entered Keep (null for pre-S11 rows).
    public KeepRequestSource? Source { get; private set; }
    // ADR-370: true when business-created and not yet shared via tracker link.
    public bool NeedsShare { get; private set; }

    // Lifecycle timestamps.
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? TerminatedAtUtc { get; private set; }      // ADR-096: covers Closed, Cancelled, Spam, Test
    public DateTime? LastBusinessActivityAt { get; private set; }
    public DateTime? LastCustomerActivityAt { get; private set; }

    public bool IsTerminal =>
        Status is KeepRequestStatus.Closed or KeepRequestStatus.Cancelled
                or KeepRequestStatus.Spam  or KeepRequestStatus.Test;

    public bool HasActiveUnresolvedFeedbackReview =>
        Status == KeepRequestStatus.Closed
        && FeedbackSubmittedAtUtc.HasValue
        && FeedbackWasResolved == false
        && !FeedbackReviewedAtUtc.HasValue
        && AttentionLevel != AttentionLevel.None
        && AttentionReason == Enums.AttentionReason.UnresolvedFeedback;

    // --- First-response fields (D7/ADR-090) ---

    public DateTime? FirstResponseDueAtUtc { get; private set; }
    public DateTime? FirstRespondedAtUtc { get; private set; }
    public Guid? FirstResponderAccountUserId { get; private set; }
    public Guid? FirstResponseEventId { get; private set; }

    // --- Attention fields (D8/ADR-091) ---

    public AttentionLevel AttentionLevel { get; private set; } = AttentionLevel.None;
    public WaitingDirection WaitingDirection { get; private set; } = WaitingDirection.None;
    public AttentionReason? AttentionReason { get; private set; }
    public PriorityBand PriorityBand { get; private set; } = PriorityBand.Standard;
    public DateTime? AttentionSinceUtc { get; private set; }
    public DateTime? NextAttentionAtUtc { get; private set; }
    public DateTime? AttentionClearedAtUtc { get; private set; }
    public Guid? AttentionClearedByAccountUserId { get; private set; }
    public string? AttentionClearReason { get; private set; }

    // --- Terminal feedback fields (D6/ADR-089) ---

    public bool? FeedbackWasResolved { get; private set; }
    public string? FeedbackComment { get; private set; }
    public DateTime? FeedbackSubmittedAtUtc { get; private set; }

    // --- Feedback review fields (ADR-268, Session 5) ---

    public DateTime? FeedbackReviewedAtUtc { get; private set; }
    public Guid? FeedbackReviewedByAccountUserId { get; private set; }
    public string? FeedbackReviewNote { get; private set; }

    // --- Follow Up On fields (ADR-337, P6b-1) ---

    public DateOnly? FollowUpOnDate { get; private set; }
    public FollowUpReason? FollowUpReason { get; private set; }
    public string? FollowUpNote { get; private set; }

    // --- Planned For field (ADR-338, P6b-1) ---

    public DateOnly? PlannedForDate { get; private set; }

    // Service location (S22d). Nullable at DB level; required for public intake via application validation.
    public string? ServiceAddressLine1 { get; private set; }
    public string? ServiceAddressLine2 { get; private set; }
    public string? ServiceCity { get; private set; }
    public string? ServiceState { get; private set; }
    public string? ServiceZip { get; private set; }

    // Customer-reported urgency (S22p2). Default Routine; never auto-elevates attention.
    public IntakeUrgency IntakeUrgency { get; private set; } = IntakeUrgency.Routine;

    // Business-set operational priority (ADR-433, S22p10). Null = not set; overrides IntakeUrgency in ranking when set.
    public BusinessPriority? BusinessPriority { get; private set; }

    // Customer-stated contact preference (S22p3). Default NoPreference.
    public ContactPreference ContactPreference { get; private set; } = ContactPreference.NoPreference;

    // --- Customer page viewed telemetry (ADR-341, P6c-2) ---

    public DateTime? CustomerPageLastViewedAtUtc { get; private set; }

    // --- Optimistic concurrency (G5/ADR-330) ---

    // Application-managed opaque concurrency token for the request aggregate (row, events,
    // participants). New rows receive a random GUID; the API exposes it as `version`. Clients
    // compare and return it but never interpret ordering.
    public Guid ConcurrencyVersion { get; private set; }

    public void RotateConcurrencyVersion() => ConcurrencyVersion = Guid.NewGuid();

    private const int TerminalPageRetentionDays = 30;

    /// <summary>
    /// Moves the request to a new status and optionally attaches a customer-visible message.
    /// Returns a KeepStatusChangeOutcome; IsNoOp is true when the call is a same-status
    /// no-op (same status, no message — success, nothing to persist).
    /// Status-message limit: 2000 characters. For business updates with status use
    /// AddBusinessUpdateWithStatus (4000-character business-update limit).
    /// Wires first-response when a message is provided and this is the first customer-visible
    /// contact on a customer-origin request (D1/B2-beta).
    /// </summary>
    public Result<KeepStatusChangeOutcome> ChangeStatus(
        KeepRequestStatus newStatus,
        string? message,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (!Enum.IsDefined(newStatus))
            return Result<KeepStatusChangeOutcome>.Failure(KeepRequestErrors.InvalidStatus);

        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        var trimmedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        if (trimmedMessage?.Length > 2000)
            return Result<KeepStatusChangeOutcome>.Failure(KeepRequestErrors.MessageTooLong);

        // No-op before terminal: Closed→Closed/no-message is success, not an error.
        if (newStatus == Status && trimmedMessage is null)
            return Result<KeepStatusChangeOutcome>.Success(KeepStatusChangeOutcome.NoOp);

        if (IsTerminal)
            return Result<KeepStatusChangeOutcome>.Failure(KeepRequestErrors.TerminalState);

        // PendingCustomer and Cancelled always require a customer-visible message.
        if (trimmedMessage is null && newStatus is KeepRequestStatus.PendingCustomer or KeepRequestStatus.Cancelled)
            return Result<KeepStatusChangeOutcome>.Failure(KeepRequestErrors.MessageRequired);

        // Validate the transition for actual status changes (same-status is always permitted).
        if (newStatus != Status && !IsAllowedTransition(Status, newStatus))
            return Result<KeepStatusChangeOutcome>.Failure(KeepRequestErrors.InvalidStatusTransition);

        Status = newStatus;
        // Silent status change preserves the last meaningful customer-facing status text.
        if (trimmedMessage is not null)
            CurrentStatusText = trimmedMessage;
        LastBusinessActivityAt = nowUtc;

        if (newStatus is KeepRequestStatus.Closed or KeepRequestStatus.Cancelled)
        {
            TerminatedAtUtc = nowUtc;
            ClearAllAttentionForTerminal(actorAccountUserId, nowUtc);
        }

        if (newStatus is KeepRequestStatus.Closed or KeepRequestStatus.Cancelled)
            ExpiresAtUtc = nowUtc.AddDays(TerminalPageRetentionDays);

        var statusEvent = KeepRequestEvent.CreateStatusChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, newStatus, trimmedMessage, nowUtc);

        // D1: combined status+message counts as first response on customer-origin requests.
        if (trimmedMessage is not null && Origin == KeepRequestOrigin.Customer && FirstRespondedAtUtc is null)
        {
            FirstRespondedAtUtc = nowUtc;
            FirstResponderAccountUserId = actorAccountUserId;
            FirstResponseEventId = statusEvent.Id;
        }

        if (trimmedMessage is not null)
            ClearBusinessWaitingAttention(actorAccountUserId, nowUtc);

        return Result<KeepStatusChangeOutcome>.Success(KeepStatusChangeOutcome.WithEvent(statusEvent));
    }

    /// <summary>
    /// Classifies a non-terminal request as Spam or Test (ADR-349/350). Owner/Admin only;
    /// enforced at the service layer. Classification is final — it sets a terminal status,
    /// stamps TerminatedAtUtc, sets the 30-day customer-page expiry, clears all attention,
    /// and writes an Internal audit event. Optional reason (≤ 500 chars) is stored on the event.
    /// </summary>
    public Result<KeepRequestEvent> Classify(
        KeepRequestStatus targetStatus,
        string? reason,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (targetStatus is not (KeepRequestStatus.Spam or KeepRequestStatus.Test))
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.InvalidClassification);

        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        if (IsTerminal)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.TerminalState);

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason?.Length > 500)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.ClassificationReasonTooLong);

        Status           = targetStatus;
        TerminatedAtUtc  = nowUtc;
        ExpiresAtUtc     = nowUtc.AddDays(TerminalPageRetentionDays);
        LastBusinessActivityAt = nowUtc;
        ClearAllAttentionForTerminal(actorAccountUserId, nowUtc);

        var classifiedEvent = KeepRequestEvent.CreateClassified(
            Id, AccountId, actorAccountUserId, actorDisplayName, targetStatus, trimmedReason, nowUtc);

        return Result<KeepRequestEvent>.Success(classifiedEvent);
    }

    /// <summary>
    /// Adds a customer-visible business update without changing status. Creates a MessageAdded
    /// event with Visibility=All, MessageIntent=BusinessUpdate, CommunicationChannel=InApp.
    /// Business-update message limit: 4000 characters. Not allowed on terminal requests.
    /// Wires first-response when this is the first customer-visible contact on a customer-origin
    /// request (D1).
    /// </summary>
    public Result<KeepRequestEvent> AddBusinessUpdate(
        string message,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        var trimmedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        if (trimmedMessage is null)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.MessageRequired);

        if (trimmedMessage.Length > 4000)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.BusinessUpdateMessageTooLong);

        if (IsTerminal)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.TerminalState);

        LastBusinessActivityAt = nowUtc;

        var messageEvent = KeepRequestEvent.CreateBusinessUpdateMessage(
            Id, AccountId, actorAccountUserId, actorDisplayName, trimmedMessage, nowUtc);

        if (Origin == KeepRequestOrigin.Customer && FirstRespondedAtUtc is null)
        {
            FirstRespondedAtUtc = nowUtc;
            FirstResponderAccountUserId = actorAccountUserId;
            FirstResponseEventId = messageEvent.Id;
        }

        ClearBusinessWaitingAttention(actorAccountUserId, nowUtc);

        return Result<KeepRequestEvent>.Success(messageEvent);
    }

    /// <summary>
    /// Combines a customer-visible business update with a status change. Creates a StatusChanged
    /// event with Visibility=All, MessageIntent=BusinessUpdate, CommunicationChannel=InApp.
    /// Business-update message limit: 4000 characters (not the 2000-char status-message limit).
    /// Validates the status transition using the same rules as ChangeStatus. Not allowed on
    /// terminal requests. Wires first-response (D1).
    /// </summary>
    public Result<KeepRequestEvent> AddBusinessUpdateWithStatus(
        KeepRequestStatus newStatus,
        string message,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (!Enum.IsDefined(newStatus))
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.InvalidStatus);

        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        var trimmedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        if (trimmedMessage is null)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.MessageRequired);

        if (trimmedMessage.Length > 4000)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.BusinessUpdateMessageTooLong);

        if (IsTerminal)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.TerminalState);

        if (newStatus != Status && !IsAllowedTransition(Status, newStatus))
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.InvalidStatusTransition);

        Status = newStatus;
        CurrentStatusText = trimmedMessage;
        LastBusinessActivityAt = nowUtc;

        if (newStatus is KeepRequestStatus.Closed or KeepRequestStatus.Cancelled)
        {
            TerminatedAtUtc = nowUtc;
            ExpiresAtUtc = nowUtc.AddDays(TerminalPageRetentionDays);
            ClearAllAttentionForTerminal(actorAccountUserId, nowUtc);
        }

        // CreateStatusChanged with a non-null message produces the combined event shape (D4):
        // MessageIntent=BusinessUpdate, CommunicationChannel=InApp.
        var statusEvent = KeepRequestEvent.CreateStatusChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, newStatus, trimmedMessage, nowUtc);

        if (Origin == KeepRequestOrigin.Customer && FirstRespondedAtUtc is null)
        {
            FirstRespondedAtUtc = nowUtc;
            FirstResponderAccountUserId = actorAccountUserId;
            FirstResponseEventId = statusEvent.Id;
        }

        ClearBusinessWaitingAttention(actorAccountUserId, nowUtc);

        return Result<KeepRequestEvent>.Success(statusEvent);
    }

    /// <summary>
    /// Adds an internal operator note. Creates an InternalNoteAdded event with
    /// Visibility=Internal. Allowed on terminal requests (D8). Does not update
    /// LastBusinessActivityAt. Does not wire first-response (D1).
    /// Internal-note limit: 4000 characters.
    /// </summary>
    public Result<KeepRequestEvent> AddInternalNote(
        string note,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (trimmedNote is null)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.NoteRequired);

        if (trimmedNote.Length > 4000)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.NoteTooLong);

        // No terminal check — D8: internal notes allowed after Closed/Cancelled.
        // No LastBusinessActivityAt update — D8: internal notes do not update business activity.

        var noteEvent = KeepRequestEvent.CreateInternalNote(
            Id, AccountId, actorAccountUserId, actorDisplayName, trimmedNote, nowUtc);

        return Result<KeepRequestEvent>.Success(noteEvent);
    }

    /// <summary>
    /// Acknowledges active attention without customer communication. Creates an internal
    /// AttentionAcknowledged event and does not count as first response (D5/B2-gamma).
    /// UnresolvedFeedback attention is excluded; it must be resolved via MarkFeedbackReviewed (G7a/ADR-300).
    /// </summary>
    public Result<KeepRequestEvent> AcknowledgeAttention(
        string reason,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (trimmedReason is null)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.AttentionReasonRequired);

        if (trimmedReason.Length > 500)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.AttentionReasonTooLong);

        if (AttentionLevel == AttentionLevel.None)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.AttentionNotRaised);

        // G7a/ADR-300: UnresolvedFeedback must be resolved via MarkFeedbackReviewed, not generic ack.
        if (AttentionReason == Enums.AttentionReason.UnresolvedFeedback)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.AttentionRequiresFeedbackReview);

        AttentionLevel = AttentionLevel.None;
        WaitingDirection = WaitingDirection.None;
        AttentionReason = null;
        PriorityBand = PriorityBand.Standard;
        AttentionSinceUtc = null;
        NextAttentionAtUtc = null;
        AttentionClearedAtUtc = nowUtc;
        AttentionClearedByAccountUserId = actorAccountUserId;
        AttentionClearReason = trimmedReason;

        var attentionEvent = KeepRequestEvent.CreateAttentionAcknowledged(
            Id, AccountId, actorAccountUserId, actorDisplayName, trimmedReason, nowUtc);

        return Result<KeepRequestEvent>.Success(attentionEvent);
    }

    /// <summary>
    /// Records a customer-submitted message from the public customer page. Appends a
    /// MessageAdded event with Visibility=All and ActorType=Customer. Raises or updates
    /// business-waiting attention according to intent and response-policy targets (ADR-125).
    /// Blocked on terminal requests (Closed/Cancelled); Resolved accepts messages (ADR-127).
    /// Does not change status. Does not count as first business response.
    /// Customer message limit: 4000 characters.
    /// </summary>
    /// <param name="firstResponseTargetMinutes">
    /// Resolved from KeepResponsePolicy or pilot default (60). Passed in by the service
    /// so the domain method stays persistence-free.
    /// </param>
    /// <param name="standardResponseTargetMinutes">
    /// Resolved from KeepResponsePolicy or pilot default (240).
    /// </param>
    /// <param name="priorityResponseTargetMinutes">
    /// Resolved from KeepResponsePolicy or pilot default (60).
    /// </param>
    public Result<KeepRequestEvent> AddCustomerMessage(
        MessageIntent intent,
        string message,
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        var trimmedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        if (trimmedMessage is null)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.MessageRequired);

        if (trimmedMessage.Length > 4000)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.CustomerMessageTooLong);

        // Terminal requests block customer writes. Resolved does not (ADR-127).
        if (IsTerminal)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.TerminalState);

        LastCustomerActivityAt = nowUtc;

        var messageEvent = KeepRequestEvent.CreateCustomerMessage(
            Id, AccountId, CustomerName, intent, trimmedMessage, nowUtc);

        var (newReason, newPriority) = MapIntentToAttention(intent);
        var responseTargetMinutes = newPriority == PriorityBand.Priority
            ? priorityResponseTargetMinutes
            : standardResponseTargetMinutes;

        if (WaitingDirection == WaitingDirection.Customer)
        {
            // Flip: business now owes the next move. Reset AttentionSinceUtc to now
            // because this begins a new waiting period from the customer's message.
            AttentionLevel = AttentionLevel.Waiting;
            WaitingDirection = WaitingDirection.Business;
            AttentionReason = newReason;
            PriorityBand = newPriority;
            AttentionSinceUtc = nowUtc;
            NextAttentionAtUtc = nowUtc.AddMinutes(responseTargetMinutes);
        }
        else if (AttentionLevel == AttentionLevel.None)
        {
            // Fresh attention: no prior attention on this request.
            AttentionLevel = AttentionLevel.Waiting;
            WaitingDirection = WaitingDirection.Business;
            AttentionReason = newReason;
            PriorityBand = newPriority;
            AttentionSinceUtc = nowUtc;
            NextAttentionAtUtc = nowUtc.AddMinutes(responseTargetMinutes);
        }
        else if (WaitingDirection == WaitingDirection.Business)
        {
            // Already business-waiting: preserve the oldest unresolved AttentionSinceUtc.
            // Upgrade reason and priority only when the new message is higher-priority.
            // NextAttentionAtUtc is NOT refreshed for same-priority repeated messages —
            // the original deadline stands. A priority upgrade resets it because the
            // response obligation changes to the priority target (ADR-125).
            if (newPriority == PriorityBand.Priority && this.PriorityBand == PriorityBand.Standard)
            {
                AttentionReason = newReason;
                PriorityBand = newPriority;
                NextAttentionAtUtc = nowUtc.AddMinutes(responseTargetMinutes);
            }
        }
        else
        {
            // AttentionLevel is non-None but WaitingDirection is neither Business nor Customer.
            // This is an invalid domain state that should never occur in production.
            throw new InvalidOperationException(
                $"Request {Id} has AttentionLevel {AttentionLevel} with unexpected WaitingDirection {WaitingDirection}.");
        }

        return Result<KeepRequestEvent>.Success(messageEvent);
    }

    /// <summary>
    /// Records closed-request resolution feedback from the customer. Allowed only on Closed
    /// requests; one-time only. Negative feedback raises priority business-waiting attention
    /// without reopening the request or changing status (ADR-135..138).
    /// Comment is optional even when wasResolved = false (ADR-135).
    /// Comment max length: 2000 characters.
    /// </summary>
    public Result SubmitFeedback(
        bool wasResolved,
        string? comment,
        int priorityResponseTargetMinutes,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));

        if (Status != KeepRequestStatus.Closed)
            return Result.Failure(KeepRequestErrors.FeedbackUnavailable);

        if (FeedbackSubmittedAtUtc.HasValue)
            return Result.Failure(KeepRequestErrors.FeedbackAlreadySubmitted);

        var trimmedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (trimmedComment?.Length > 2000)
            return Result.Failure(KeepRequestErrors.FeedbackCommentTooLong);

        FeedbackWasResolved = wasResolved;
        FeedbackComment = trimmedComment;
        FeedbackSubmittedAtUtc = nowUtc;

        if (!wasResolved)
        {
            // Intentional exception to the terminal-no-attention posture (ADR-138).
            // Does not reopen or change status — business decides next action.
            AttentionLevel = AttentionLevel.Waiting;
            WaitingDirection = WaitingDirection.Business;
            AttentionReason = Enums.AttentionReason.UnresolvedFeedback;
            PriorityBand = Enums.PriorityBand.Priority;
            AttentionSinceUtc = nowUtc;
            NextAttentionAtUtc = nowUtc.AddMinutes(priorityResponseTargetMinutes);
        }

        return Result.Success();
    }

    /// <summary>
    /// Marks post-close negative feedback as reviewed by an Owner/Admin (ADR-264..267/273).
    /// Eligible only when: Closed, feedback submitted, wasResolved = false, not already reviewed,
    /// and current attention is specifically UnresolvedFeedback (ADR-273). If AcknowledgeAttention
    /// cleared that state first, this returns FeedbackReviewUnavailable (D1 confirmed).
    /// Clears UnresolvedFeedback attention and stores review metadata + optional note (ADR-267/268).
    /// Creates an internal-only FeedbackReviewed event (ADR-269). Does not reopen the request,
    /// change Status, notify the customer, or overwrite original feedback fields (ADR-265).
    /// Note max length: 2000 characters (ADR-270).
    /// </summary>
    public Result<KeepRequestEvent> MarkFeedbackReviewed(
        string? note,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (Status != KeepRequestStatus.Closed
            || !FeedbackSubmittedAtUtc.HasValue
            || FeedbackWasResolved != false)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FeedbackReviewUnavailable);

        if (FeedbackReviewedAtUtc.HasValue)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FeedbackAlreadyReviewed);

        // ADR-273: require active UnresolvedFeedback attention state (D1).
        if (AttentionLevel == AttentionLevel.None
            || AttentionReason != Enums.AttentionReason.UnresolvedFeedback)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FeedbackReviewUnavailable);

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > 2000)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FeedbackReviewNoteTooLong);

        FeedbackReviewedAtUtc = nowUtc;
        FeedbackReviewedByAccountUserId = actorAccountUserId;
        FeedbackReviewNote = trimmedNote;

        // ADR-267: clear only UnresolvedFeedback attention (state already verified above).
        AttentionLevel = AttentionLevel.None;
        WaitingDirection = WaitingDirection.None;
        AttentionReason = null;
        PriorityBand = PriorityBand.Standard;
        AttentionSinceUtc = null;
        NextAttentionAtUtc = null;
        AttentionClearedAtUtc = nowUtc;
        AttentionClearedByAccountUserId = actorAccountUserId;
        AttentionClearReason = "feedback_reviewed";

        var reviewEvent = KeepRequestEvent.CreateFeedbackReviewed(
            Id, AccountId, actorAccountUserId, actorDisplayName, trimmedNote, nowUtc);

        return Result<KeepRequestEvent>.Success(reviewEvent);
    }

    /// <summary>
    /// Logs operator-initiated outbound contact with the customer (call, SMS, or email).
    /// Valid channels: Phone, Sms, Email. Outcome required for Phone only (ADR-168/203).
    /// RequiresBusinessFollowUp required for spoke/voicemail/SMS/email; must be null for
    /// no-answer/wrong-number (ADR-169/216). Summary required for SMS/Email (ADR-199).
    /// Spoke/voicemail/SMS/email set first response on customer-origin requests that have none (ADR-198/213).
    /// When requiresBusinessFollowUp = false, clears eligible business-waiting attention with
    /// reason "external_contact_no_follow_up" (ADR-214). Not allowed on terminal requests (ADR-200).
    /// </summary>
    public Result<KeepRequestEvent> LogOutboundExternalContact(
        CommunicationChannel channel,
        ExternalContactOutcome? outcome,
        bool? requiresBusinessFollowUp,
        string? summary,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (IsTerminal)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.TerminalState);

        var (validationError, normalizedSummary) = ValidateOutboundContact(channel, outcome, requiresBusinessFollowUp, summary);
        if (validationError is not null)
            return Result<KeepRequestEvent>.Failure(validationError);

        // Effect matrix (ADR-198).
        bool countsFirstResponse = channel == CommunicationChannel.Phone
            ? outcome!.Value is ExternalContactOutcome.SpokeWithCustomer or ExternalContactOutcome.LeftVoicemail
            : true; // Sms / Email always count first response

        bool setFirstResponse = countsFirstResponse
            && Origin == KeepRequestOrigin.Customer
            && FirstRespondedAtUtc is null;

        bool clearAttention = requiresBusinessFollowUp == false
            && AttentionLevel != AttentionLevel.None
            && WaitingDirection == WaitingDirection.Business;

        LastBusinessActivityAt = nowUtc;

        var contactEvent = KeepRequestEvent.CreateExternalContactLogged(
            Id, AccountId, actorAccountUserId, actorDisplayName.Trim(),
            ExternalContactDirection.Outbound, channel, outcome,
            requiresFollowUp: requiresBusinessFollowUp,
            summary: normalizedSummary,
            setFirstResponse: setFirstResponse,
            clearedAttention: clearAttention,
            occurredAtUtc: nowUtc);

        if (setFirstResponse)
        {
            FirstRespondedAtUtc = nowUtc;
            FirstResponderAccountUserId = actorAccountUserId;
            FirstResponseEventId = contactEvent.Id;
        }

        if (clearAttention)
        {
            AttentionLevel = AttentionLevel.None;
            WaitingDirection = WaitingDirection.None;
            AttentionReason = null;
            PriorityBand = PriorityBand.Standard;
            AttentionSinceUtc = null;
            NextAttentionAtUtc = null;
            AttentionClearedAtUtc = nowUtc;
            AttentionClearedByAccountUserId = actorAccountUserId;
            AttentionClearReason = "external_contact_no_follow_up";
        }

        return Result<KeepRequestEvent>.Success(contactEvent);
    }

    /// <summary>
    /// Logs inbound customer contact that occurred outside Keep (call, SMS, email, in-person, other).
    /// Summary always required — captures customer-provided context for the team (ADR-199).
    /// Never counts as business first response (ADR-198).
    /// Updates LastCustomerActivityAt. When requiresBusinessFollowUp = true, raises or preserves
    /// business-waiting attention at standard priority using response-policy timing (ADR-204).
    /// Not allowed on terminal requests (ADR-200).
    /// </summary>
    public Result<KeepRequestEvent> LogInboundExternalContact(
        CommunicationChannel channel,
        bool requiresBusinessFollowUp,
        string summary,
        Guid actorAccountUserId,
        string actorDisplayName,
        int standardResponseTargetMinutes,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (standardResponseTargetMinutes <= 0)
            throw new ArgumentException("standardResponseTargetMinutes must be positive.", nameof(standardResponseTargetMinutes));

        if (IsTerminal)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.TerminalState);

        if (!Enum.IsDefined(channel) || channel == CommunicationChannel.InApp)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.ExternalContactInvalidInboundChannel);

        var trimmedSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        if (trimmedSummary is null)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.ExternalContactSummaryRequired);
        if (trimmedSummary.Length > 4000)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.ExternalContactSummaryTooLong);

        LastCustomerActivityAt = nowUtc;

        if (requiresBusinessFollowUp)
        {
            if (WaitingDirection == WaitingDirection.Customer)
            {
                // Flip: customer made external contact, business now owes the next move.
                AttentionLevel = AttentionLevel.Waiting;
                WaitingDirection = WaitingDirection.Business;
                AttentionReason = Enums.AttentionReason.CustomerMessage;
                PriorityBand = Enums.PriorityBand.Standard;
                AttentionSinceUtc = nowUtc;
                NextAttentionAtUtc = nowUtc.AddMinutes(standardResponseTargetMinutes);
            }
            else if (AttentionLevel == AttentionLevel.None)
            {
                // Fresh attention.
                AttentionLevel = AttentionLevel.Waiting;
                WaitingDirection = WaitingDirection.Business;
                AttentionReason = Enums.AttentionReason.CustomerMessage;
                PriorityBand = Enums.PriorityBand.Standard;
                AttentionSinceUtc = nowUtc;
                NextAttentionAtUtc = nowUtc.AddMinutes(standardResponseTargetMinutes);
            }
            else if (WaitingDirection == WaitingDirection.Business)
            {
                // Already business-waiting: preserve oldest AttentionSinceUtc — no change needed.
            }
            else
            {
                // AttentionLevel is non-None but WaitingDirection is neither Business nor Customer.
                // This is an invalid domain state that should never occur in production.
                throw new InvalidOperationException(
                    $"Request {Id} has AttentionLevel {AttentionLevel} with unexpected WaitingDirection {WaitingDirection}.");
            }
        }

        var contactEvent = KeepRequestEvent.CreateExternalContactLogged(
            Id, AccountId, actorAccountUserId, actorDisplayName.Trim(),
            ExternalContactDirection.Inbound, channel, outcome: null,
            requiresFollowUp: requiresBusinessFollowUp,
            summary: trimmedSummary,
            setFirstResponse: false,
            clearedAttention: false,
            occurredAtUtc: nowUtc);

        return Result<KeepRequestEvent>.Success(contactEvent);
    }

    /// <summary>
    /// Logs outbound contact during the exact active unresolved-feedback review state (GAP-018 / G7b).
    /// Only valid when HasActiveUnresolvedFeedbackReview. Updates LastBusinessActivityAt only;
    /// does not set first response, clear attention, change status, or affect any review/feedback fields.
    /// </summary>
    public Result<KeepRequestEvent> LogClosedFeedbackFollowUpExternalContact(
        CommunicationChannel channel,
        ExternalContactOutcome? outcome,
        bool? requiresBusinessFollowUp,
        string? summary,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (!HasActiveUnresolvedFeedbackReview)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.TerminalState);

        var (validationError, normalizedSummary) = ValidateOutboundContact(channel, outcome, requiresBusinessFollowUp, summary);
        if (validationError is not null)
            return Result<KeepRequestEvent>.Failure(validationError);

        LastBusinessActivityAt = nowUtc;

        var contactEvent = KeepRequestEvent.CreateExternalContactLogged(
            Id, AccountId, actorAccountUserId, actorDisplayName.Trim(),
            ExternalContactDirection.Outbound, channel, outcome,
            requiresFollowUp: requiresBusinessFollowUp,
            summary: normalizedSummary,
            setFirstResponse: false,
            clearedAttention: false,
            occurredAtUtc: nowUtc);

        return Result<KeepRequestEvent>.Success(contactEvent);
    }

    private static (Error? Error, string? NormalizedSummary) ValidateOutboundContact(
        CommunicationChannel channel,
        ExternalContactOutcome? outcome,
        bool? requiresBusinessFollowUp,
        string? summary)
    {
        if (channel is not (CommunicationChannel.Phone or CommunicationChannel.Sms or CommunicationChannel.Email))
            return (KeepRequestErrors.ExternalContactInvalidOutboundChannel, null);

        // Guard undefined outcome before pattern matching — domain returns failure for user-shaped invalid input.
        if (outcome.HasValue && !Enum.IsDefined(outcome.Value))
            return (KeepRequestErrors.ExternalContactOutcomeNotAllowed, null);

        if (channel == CommunicationChannel.Phone)
        {
            if (!outcome.HasValue)
                return (KeepRequestErrors.ExternalContactOutcomeRequired, null);

            if (outcome.Value is ExternalContactOutcome.SpokeWithCustomer or ExternalContactOutcome.LeftVoicemail)
            {
                if (!requiresBusinessFollowUp.HasValue)
                    return (KeepRequestErrors.ExternalContactFollowUpRequired, null);
            }
            else
            {
                // NoAnswer / WrongNumber: follow-up does not apply (ADR-216).
                if (requiresBusinessFollowUp.HasValue)
                    return (KeepRequestErrors.ExternalContactFollowUpNotAllowed, null);
            }
        }
        else
        {
            // Sms / Email: no outcome, follow-up required, summary required.
            if (outcome.HasValue)
                return (KeepRequestErrors.ExternalContactOutcomeNotAllowed, null);

            if (!requiresBusinessFollowUp.HasValue)
                return (KeepRequestErrors.ExternalContactFollowUpRequired, null);
        }

        var normalizedSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();

        if (channel is CommunicationChannel.Sms or CommunicationChannel.Email && normalizedSummary is null)
            return (KeepRequestErrors.ExternalContactSummaryRequired, null);

        if (normalizedSummary?.Length > 4000)
            return (KeepRequestErrors.ExternalContactSummaryTooLong, null);

        return (null, normalizedSummary);
    }

    // Enums. prefix required: AttentionReason and PriorityBand instance properties shadow the
    // enum type names from the using import in static method scope.
    private static (Enums.AttentionReason Reason, Enums.PriorityBand Priority) MapIntentToAttention(MessageIntent intent) =>
        intent switch
        {
            MessageIntent.GeneralMessage        => (Enums.AttentionReason.CustomerMessage, Enums.PriorityBand.Standard),
            MessageIntent.Question              => (Enums.AttentionReason.CustomerMessage, Enums.PriorityBand.Standard),
            MessageIntent.UpdateRequest         => (Enums.AttentionReason.UpdateRequest, Enums.PriorityBand.Standard),
            MessageIntent.ScheduleChangeRequest => (Enums.AttentionReason.ScheduleChangeRequest, Enums.PriorityBand.Priority),
            MessageIntent.ChangeOrCancelRequest => (Enums.AttentionReason.ChangeOrCancelRequest, Enums.PriorityBand.Priority),
            MessageIntent.Complaint             => (Enums.AttentionReason.Complaint, Enums.PriorityBand.Priority),
            MessageIntent.InformationAdded      => (Enums.AttentionReason.CustomerMessage, Enums.PriorityBand.Standard),
            MessageIntent.CallRequested         => (Enums.AttentionReason.CallRequested, Enums.PriorityBand.Priority),
            MessageIntent.TimingChangeRequested => (Enums.AttentionReason.TimingChangeRequested, Enums.PriorityBand.Priority),
            MessageIntent.CancellationRequested => (Enums.AttentionReason.CancellationRequested, Enums.PriorityBand.Priority),
            _ => throw new InvalidOperationException($"MessageIntent {intent} is not a valid customer message intent.")
        };

    private void ClearAllAttentionForTerminal(Guid actorAccountUserId, DateTime nowUtc)
    {
        if (AttentionLevel == AttentionLevel.None)
            return;

        AttentionLevel = AttentionLevel.None;
        WaitingDirection = WaitingDirection.None;
        AttentionReason = null;
        PriorityBand = PriorityBand.Standard;
        AttentionSinceUtc = null;
        NextAttentionAtUtc = null;
        AttentionClearedAtUtc = nowUtc;
        AttentionClearedByAccountUserId = actorAccountUserId;
        AttentionClearReason = null;
    }

    private void ClearBusinessWaitingAttention(Guid actorAccountUserId, DateTime nowUtc)
    {
        if (AttentionLevel == AttentionLevel.None || WaitingDirection != WaitingDirection.Business)
            return;

        AttentionLevel = AttentionLevel.None;
        WaitingDirection = WaitingDirection.None;
        AttentionReason = null;
        PriorityBand = PriorityBand.Standard;
        AttentionSinceUtc = null;
        NextAttentionAtUtc = null;
        AttentionClearedAtUtc = nowUtc;
        AttentionClearedByAccountUserId = actorAccountUserId;
        AttentionClearReason = null;
    }

    private static bool IsAllowedTransition(KeepRequestStatus from, KeepRequestStatus to) =>
        (from, to) switch
        {
            (KeepRequestStatus.Received
             or KeepRequestStatus.Scheduled
             or KeepRequestStatus.InProgress
             or KeepRequestStatus.PendingCustomer,
             KeepRequestStatus.Scheduled
             or KeepRequestStatus.InProgress
             or KeepRequestStatus.PendingCustomer
             or KeepRequestStatus.Resolved
             or KeepRequestStatus.Cancelled) => true,

            (KeepRequestStatus.Resolved,
             KeepRequestStatus.InProgress
             or KeepRequestStatus.PendingCustomer
             or KeepRequestStatus.Closed
             or KeepRequestStatus.Cancelled) => true,

            _ => false
        };

    /// <summary>
    /// Sets or changes Follow Up On. Active requests only; Resolved/Closed/Cancelled rejected.
    /// Reason required; note required when reason is Other; note max 500 characters (ADR-337).
    /// </summary>
    public Result<KeepRequestEvent> SetFollowUpOn(
        DateOnly date,
        FollowUpReason? reason,
        string? note,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (!IsActive)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FollowUpOnRequiresActiveRequest);

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (reason == Enums.FollowUpReason.Other && trimmedNote is null)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FollowUpOnNoteRequired);

        if (trimmedNote?.Length > 500)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FollowUpOnNoteTooLong);

        FollowUpOnDate = date;
        FollowUpReason = reason;
        FollowUpNote = trimmedNote;
        LastBusinessActivityAt = nowUtc;

        var ev = KeepRequestEvent.CreateFollowUpOnChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, date, reason, trimmedNote, nowUtc);

        return Result<KeepRequestEvent>.Success(ev);
    }

    /// <summary>
    /// Clears Follow Up On. Active requests only (ADR-337).
    /// </summary>
    public Result<KeepRequestEvent> ClearFollowUpOn(
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (!IsActive)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.FollowUpOnRequiresActiveRequest);

        FollowUpOnDate = null;
        FollowUpReason = null;
        FollowUpNote = null;
        LastBusinessActivityAt = nowUtc;

        var ev = KeepRequestEvent.CreateFollowUpOnChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName,
            date: null, reason: null, note: null, nowUtc);

        return Result<KeepRequestEvent>.Success(ev);
    }

    /// <summary>
    /// Sets or changes Planned For. Active requests only; does not notify the customer or
    /// change lifecycle status (ADR-338).
    /// </summary>
    public Result<KeepRequestEvent> SetPlannedFor(
        DateOnly date,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (!IsActive)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.PlannedForRequiresActiveRequest);

        PlannedForDate = date;
        LastBusinessActivityAt = nowUtc;

        var ev = KeepRequestEvent.CreatePlannedForChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, date, nowUtc);

        return Result<KeepRequestEvent>.Success(ev);
    }

    /// <summary>
    /// Clears Planned For. Active requests only (ADR-338).
    /// </summary>
    public Result<KeepRequestEvent> ClearPlannedFor(
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (!IsActive)
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.PlannedForRequiresActiveRequest);

        PlannedForDate = null;
        LastBusinessActivityAt = nowUtc;

        var ev = KeepRequestEvent.CreatePlannedForChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, date: null, nowUtc);

        return Result<KeepRequestEvent>.Success(ev);
    }

    /// <summary>
    /// Records a customer page view for adoption/confidence telemetry (ADR-341).
    /// Debounces writes so rapid refreshes do not spam the database. Returns true when
    /// CustomerPageLastViewedAtUtc was updated and a persistence write is required;
    /// returns false when the last view is still within the debounce window.
    /// Does NOT rotate ConcurrencyVersion — page-view telemetry must not cause
    /// stale-version conflicts for concurrent operator writes.
    /// </summary>
    public bool RecordCustomerPageView(DateTime nowUtc, int debounceMinutes = 5)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (debounceMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(debounceMinutes), "Debounce window must be positive.");

        if (CustomerPageLastViewedAtUtc.HasValue
            && (nowUtc - CustomerPageLastViewedAtUtc.Value).TotalMinutes < debounceMinutes)
            return false;

        CustomerPageLastViewedAtUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// Returns needs-status-check eligibility and the latest meaningful activity timestamp
    /// for this request (ADR-339, P6d-1). Fail-closed: excluded requests return IsEligible = false
    /// with a slug describing the suppressor.
    /// </summary>
    public KeepRequestNeedsStatusCheckInputs GetNeedsStatusCheckInputs(DateOnly today)
    {
        if (!IsActive)
            return new KeepRequestNeedsStatusCheckInputs(false, "not_active", null);

        if (AttentionLevel != AttentionLevel.None)
            return new KeepRequestNeedsStatusCheckInputs(false, "active_attention", null);

        if (FollowUpOnDate.HasValue && FollowUpOnDate.Value > today)
            return new KeepRequestNeedsStatusCheckInputs(false, "future_follow_up_on", null);

        // Due/overdue FollowUpOn is active operational attention (ADR-439); it routes to
        // NeedsAttention, not NeedsStatusCheck.
        if (FollowUpOnDate.HasValue && FollowUpOnDate.Value <= today)
            return new KeepRequestNeedsStatusCheckInputs(false, "due_or_overdue_follow_up_on", null);

        if (PlannedForDate.HasValue && PlannedForDate.Value > today)
            return new KeepRequestNeedsStatusCheckInputs(false, "future_planned_for", null);

        var latest = new[]
        {
            (DateTime?)CreatedAtUtc,
            LastCustomerActivityAt,
            LastBusinessActivityAt,
            CustomerPageLastViewedAtUtc
        }.Max();

        return new KeepRequestNeedsStatusCheckInputs(true, null, latest);
    }

    // Enums. prefix required: FollowUpReason instance property shadows the enum type name in static scope.
    private bool IsActive =>
        Status is not (KeepRequestStatus.Resolved
                       or KeepRequestStatus.Closed
                       or KeepRequestStatus.Cancelled
                       or KeepRequestStatus.Spam
                       or KeepRequestStatus.Test);

    /// <summary>
    /// Creates a request submitted by a customer through public intake.
    /// Sets LastCustomerActivityAt = nowUtc; LastBusinessActivityAt is null until the business acts.
    /// Wires FirstResponseDueAtUtc from the account's response policy.
    /// </summary>
    public static KeepRequest CreateFromCustomerIntake(
        Guid accountId,
        Guid customerId,
        string customerName,
        string customerPhone,
        string? customerEmail,
        string description,
        string referenceCode,
        string pageToken,
        DateTime nowUtc,
        int firstResponseTargetMinutes,
        string? serviceAddressLine1 = null,
        string? serviceAddressLine2 = null,
        string? serviceCity = null,
        string? serviceState = null,
        string? serviceZip = null,
        IntakeUrgency intakeUrgency = IntakeUrgency.Routine,
        ContactPreference contactPreference = ContactPreference.NoPreference)
    {
        if (firstResponseTargetMinutes <= 0)
            throw new ArgumentException("First response target minutes must be positive.", nameof(firstResponseTargetMinutes));

        var request = CreateCore(accountId, customerId, customerName, customerPhone, customerEmail,
            description, referenceCode, pageToken, nowUtc, firstResponseTargetMinutes,
            KeepRequestOrigin.Customer, KeepRequestSource.PublicIntake, needsShare: false);

        request.ServiceAddressLine1 = serviceAddressLine1?.Trim();
        request.ServiceAddressLine2 = serviceAddressLine2?.Trim();
        request.ServiceCity = serviceCity?.Trim();
        request.ServiceState = serviceState?.Trim().ToUpperInvariant();
        request.ServiceZip = serviceZip?.Trim();
        request.IntakeUrgency = intakeUrgency;
        request.ContactPreference = contactPreference;

        return request;
    }

    /// <summary>
    /// Creates a request entered by authenticated business staff (phone, voicemail, walk-in, etc.).
    /// Sets LastBusinessActivityAt = nowUtc; LastCustomerActivityAt is null.
    /// No first-response timer — business-created requests do not start the customer-contact clock.
    /// </summary>
    public static KeepRequest CreateByBusiness(
        Guid accountId,
        Guid customerId,
        string customerName,
        string customerPhone,
        string? customerEmail,
        string description,
        string referenceCode,
        string pageToken,
        DateTime nowUtc,
        KeepRequestSource source) =>
        CreateCore(accountId, customerId, customerName, customerPhone, customerEmail,
            description, referenceCode, pageToken, nowUtc, firstResponseTargetMinutes: 0,
            KeepRequestOrigin.Business, source, needsShare: true);

    public void ClearNeedsShare() => NeedsShare = false;

    /// <summary>
    /// Adds or corrects service location on any request. Allowed on all lifecycle states
    /// including terminal — staff may correct location records post-close.
    /// AddressLine1, city, and state are required; the caller must supply a normalized
    /// two-letter US state code (validation and US-code check are the application layer's job).
    /// </summary>
    public Result<KeepRequestEvent> SetServiceLocation(
        string addressLine1,
        string? addressLine2,
        string city,
        string state,
        string? zip,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        if (string.IsNullOrWhiteSpace(addressLine1))
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.ServiceAddressLine1Required);
        if (string.IsNullOrWhiteSpace(city))
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.ServiceCityRequired);
        if (string.IsNullOrWhiteSpace(state))
            return Result<KeepRequestEvent>.Failure(KeepRequestErrors.ServiceStateRequired);

        ServiceAddressLine1 = addressLine1.Trim();
        ServiceAddressLine2 = string.IsNullOrWhiteSpace(addressLine2) ? null : addressLine2.Trim();
        ServiceCity         = city.Trim();
        ServiceState        = state.Trim().ToUpperInvariant();
        ServiceZip          = string.IsNullOrWhiteSpace(zip) ? null : zip.Trim();
        LastBusinessActivityAt = nowUtc;

        var ev = KeepRequestEvent.CreateServiceLocationChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, nowUtc);

        return Result<KeepRequestEvent>.Success(ev);
    }

    public Result<KeepRequestEvent> SetBusinessPriority(
        Enums.BusinessPriority? priority,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a valid UTC timestamp.", nameof(nowUtc));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        var previous = BusinessPriority;
        BusinessPriority = priority;

        var content = FormatPriorityChangeContent(previous, priority);

        var ev = KeepRequestEvent.CreateBusinessPriorityChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, content, nowUtc);

        return Result<KeepRequestEvent>.Success(ev);
    }

    private static string FormatPriorityChangeContent(
        Enums.BusinessPriority? previous, Enums.BusinessPriority? next)
    {
        var prevLabel = previous.HasValue ? previous.Value.ToString() : "Not set";
        var nextLabel = next.HasValue ? next.Value.ToString() : "Not set";
        return $"Priority changed from {prevLabel} to {nextLabel}";
    }

    private static KeepRequest CreateCore(
        Guid accountId,
        Guid customerId,
        string customerName,
        string customerPhone,
        string? customerEmail,
        string description,
        string referenceCode,
        string pageToken,
        DateTime nowUtc,
        int firstResponseTargetMinutes,
        KeepRequestOrigin origin,
        KeepRequestSource? source,
        bool needsShare)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name is required.", nameof(customerName));
        if (string.IsNullOrWhiteSpace(customerPhone))
            throw new ArgumentException("Customer phone is required.", nameof(customerPhone));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));
        if (string.IsNullOrWhiteSpace(referenceCode))
            throw new ArgumentException("Reference code is required.", nameof(referenceCode));
        if (string.IsNullOrWhiteSpace(pageToken))
            throw new ArgumentException("Page token is required.", nameof(pageToken));

        return new KeepRequest
        {
            AccountId = accountId,
            KeepCustomerId = customerId,
            CustomerName = customerName.Trim(),
            CustomerPhone = customerPhone.Trim(),
            CustomerEmail = customerEmail?.Trim(),
            Description = description.Trim(),
            Status = KeepRequestStatus.Received,
            ReferenceCode = referenceCode.Trim(),
            PageToken = pageToken.Trim(),
            Origin = origin,
            Source = source,
            NeedsShare = needsShare,
            LastCustomerActivityAt = origin == KeepRequestOrigin.Customer ? nowUtc : null,
            LastBusinessActivityAt = origin == KeepRequestOrigin.Business ? nowUtc : null,
            FirstResponseDueAtUtc = origin == KeepRequestOrigin.Customer
                ? nowUtc.AddMinutes(firstResponseTargetMinutes)
                : null,
            AttentionLevel = AttentionLevel.None,
            WaitingDirection = WaitingDirection.None,
            PriorityBand = PriorityBand.Standard,
            ConcurrencyVersion = Guid.NewGuid()
        };
    }
}
