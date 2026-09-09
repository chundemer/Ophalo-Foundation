using OpHalo.Foundation.Core.Entities.Feedback.Enums;

namespace OpHalo.Foundation.Core.Entities.Feedback;

/// <summary>
/// A single in-product feedback submission (GAP-038, BL149, ADR-500 §5). Foundation-owned,
/// persisted on <c>OpHaloDbContext</c>. This is not a ticket dashboard: no status/assignee/SLA —
/// the row exists only to guarantee the submission reaches the founder channel and is then
/// minimised.
///
/// Persist-first, at-least-once delivery (D5): the row is committed <see cref="FeedbackDeliveryState.Pending"/>
/// before any delivery attempt; <see cref="Id"/> doubles as the delivery/correlation id carried in
/// the founder-channel payload so the receiver can dedupe. On confirmed delivery the raw body
/// (<see cref="Message"/>, <see cref="ContextJson"/>) is nulled immediately; on exhaustion of the
/// retry schedule the row is <see cref="FeedbackDeliveryState.Abandoned"/> with the body retained
/// for recovery. Retention sweep (7-day delivered / 30-day abandoned) and the retry schedule live
/// in later GAP-038 slices; this type only holds the state they act on.
///
/// Does not extend BaseEntity — it has its own delivery lifecycle, is never soft-deleted, and is
/// hard-deleted by the retention sweep.
/// </summary>
public sealed class FeedbackSubmission
{
    /// <summary>Minimum length of a trimmed feedback message.</summary>
    public const int MessageMinLength = 1;

    /// <summary>Maximum length of a trimmed feedback message (BL149 contract).</summary>
    public const int MessageMaxLength = 4_000;

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    /// <summary>Account the submitter was acting in.</summary>
    public Guid AccountId { get; private init; }

    /// <summary>AccountUser membership that submitted the feedback.</summary>
    public Guid AccountUserId { get; private init; }

    /// <summary>
    /// The feedback body. Non-null while delivery is unresolved; set null the moment delivery is
    /// confirmed (<see cref="MarkDelivered"/>). Retained on an <see cref="FeedbackDeliveryState.Abandoned"/>
    /// row for recovery.
    /// </summary>
    public string? Message { get; private set; }

    public FeedbackCategory Category { get; private init; }

    /// <summary>
    /// Opaque JSON blob of non-PII submission context (route, request id, app build, platform,
    /// client timestamp). The domain treats it as an opaque string; nulled alongside
    /// <see cref="Message"/> on confirmed delivery.
    /// </summary>
    public string? ContextJson { get; private set; }

    public DateTime CreatedAtUtc { get; private init; }

    public FeedbackDeliveryState DeliveryState { get; private set; }

    /// <summary>Total delivery attempts made (synchronous attempt + retries).</summary>
    public int AttemptCount { get; private set; }

    public DateTime? LastAttemptAtUtc { get; private set; }

    /// <summary>
    /// Earliest UTC instant the retry worker may next attempt delivery. Set to <see cref="CreatedAtUtc"/>
    /// on creation (immediately eligible), advanced by <see cref="ScheduleRetry"/>, cleared once the
    /// row leaves <see cref="FeedbackDeliveryState.Pending"/>.
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    private FeedbackSubmission()
    {
    }

    public static FeedbackSubmission Create(
        Guid accountId,
        Guid accountUserId,
        string message,
        FeedbackCategory category,
        string? contextJson,
        DateTime nowUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (accountUserId == Guid.Empty)
            throw new ArgumentException("AccountUserId must not be empty.", nameof(accountUserId));
        if (!Enum.IsDefined(category))
            throw new ArgumentException("Category must be a defined value.", nameof(category));

        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length < MessageMinLength)
            throw new ArgumentException("Message must not be blank.", nameof(message));
        if (trimmed.Length > MessageMaxLength)
            throw new ArgumentException($"Message must be at most {MessageMaxLength} characters.", nameof(message));

        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));

        return new FeedbackSubmission
        {
            AccountId = accountId,
            AccountUserId = accountUserId,
            Message = trimmed,
            Category = category,
            ContextJson = string.IsNullOrWhiteSpace(contextJson) ? null : contextJson,
            CreatedAtUtc = nowUtc,
            DeliveryState = FeedbackDeliveryState.Pending,
            AttemptCount = 0,
            NextAttemptAtUtc = nowUtc,
        };
    }

    /// <summary>Records that a delivery attempt is being made now. Pending rows only.</summary>
    public void MarkAttempted(DateTime nowUtc)
    {
        RequirePending();
        RequireUtc(nowUtc);
        AttemptCount++;
        LastAttemptAtUtc = nowUtc;
    }

    /// <summary>
    /// Confirms delivery: terminal <see cref="FeedbackDeliveryState.Delivered"/>, body scrubbed,
    /// retry eligibility cleared. Pending rows only, and only once a delivery attempt has been
    /// recorded (<see cref="MarkAttempted"/>).
    /// </summary>
    public void MarkDelivered(DateTime nowUtc)
    {
        RequirePending();
        RequireAttempted();
        RequireUtc(nowUtc);
        DeliveryState = FeedbackDeliveryState.Delivered;
        DeliveredAtUtc = nowUtc;
        Message = null;
        ContextJson = null;
        NextAttemptAtUtc = null;
    }

    /// <summary>Defers the next retry attempt to <paramref name="nextAttemptAtUtc"/>. Pending rows only.</summary>
    public void ScheduleRetry(DateTime nextAttemptAtUtc)
    {
        RequirePending();
        RequireUtc(nextAttemptAtUtc);
        NextAttemptAtUtc = nextAttemptAtUtc;
    }

    /// <summary>
    /// Marks the row <see cref="FeedbackDeliveryState.Abandoned"/> after the retry schedule is
    /// exhausted. The body is deliberately retained for recovery. Pending rows only, and only once
    /// at least one delivery attempt has been recorded (<see cref="MarkAttempted"/>).
    /// </summary>
    public void MarkAbandoned(DateTime nowUtc)
    {
        RequirePending();
        RequireAttempted();
        RequireUtc(nowUtc);
        DeliveryState = FeedbackDeliveryState.Abandoned;
        NextAttemptAtUtc = null;
    }

    private void RequirePending()
    {
        if (DeliveryState != FeedbackDeliveryState.Pending)
            throw new InvalidOperationException(
                $"FeedbackSubmission {Id} is {DeliveryState}; delivery transitions are only valid from Pending.");
    }

    private void RequireAttempted()
    {
        if (AttemptCount == 0)
            throw new InvalidOperationException(
                $"FeedbackSubmission {Id} has no recorded delivery attempt; call MarkAttempted first.");
    }

    private static void RequireUtc(DateTime value)
    {
        if (value == default)
            throw new ArgumentException("Timestamp must not be default.", nameof(value));
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be UTC.", nameof(value));
    }
}
