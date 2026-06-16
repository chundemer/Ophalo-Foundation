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

    // Lifecycle timestamps.
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? TerminatedAtUtc { get; private set; }      // ADR-096: covers Closed and Cancelled
    public DateTime LastBusinessActivityAt { get; private set; }
    public DateTime? LastCustomerActivityAt { get; private set; }

    public bool IsTerminal =>
        Status is KeepRequestStatus.Closed or KeepRequestStatus.Cancelled;

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

    /// <summary>
    /// Moves the request to a new status and optionally attaches a customer-visible message.
    /// Returns a KeepStatusChangeOutcome; IsNoOp is true when the call is a same-status
    /// no-op (same status, no message — success, nothing to persist).
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

        var statusEvent = KeepRequestEvent.CreateStatusChanged(
            Id, AccountId, actorAccountUserId, actorDisplayName, newStatus, trimmedMessage, nowUtc);

        return Result<KeepStatusChangeOutcome>.Success(KeepStatusChangeOutcome.WithEvent(statusEvent));
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

    // ADR-095: origin optional, defaults to Customer (current public intake path).
    public static KeepRequest Create(
        Guid accountId,
        Guid customerId,
        string customerName,
        string customerPhone,
        string? customerEmail,
        string description,
        string referenceCode,
        string pageToken,
        DateTime nowUtc,
        KeepRequestOrigin origin = KeepRequestOrigin.Customer)
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
        if (!Enum.IsDefined(origin))
            throw new ArgumentException($"Unknown KeepRequestOrigin: {origin}.", nameof(origin));

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
            LastBusinessActivityAt = nowUtc,
            // ADR-098: attention starts at None for B1-α; B2 wires business-waiting behavior.
            AttentionLevel = AttentionLevel.None,
            WaitingDirection = WaitingDirection.None,
            PriorityBand = PriorityBand.Standard
        };
    }
}
