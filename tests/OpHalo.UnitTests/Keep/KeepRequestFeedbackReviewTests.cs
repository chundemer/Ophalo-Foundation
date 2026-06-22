using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestFeedbackReviewTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    const string ActorName = "Jane Owner";
    static readonly DateTime BaseTime = new(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);

    // Drives request through Create → Resolved → Closed → negative feedback,
    // producing the eligible state for MarkFeedbackReviewed.
    static KeepRequest EligibleRequest(DateTime? submittedAt = null)
    {
        var now = submittedAt ?? BaseTime;
        var r = KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Alice", "555-0001", null, "Desc", "REF001",
            "tok_" + Guid.NewGuid().ToString("N"), now.AddHours(-48), 60);
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, now.AddHours(-24));
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, now.AddHours(-20));
        r.SubmitFeedback(wasResolved: false, comment: "Not resolved", priorityResponseTargetMinutes: 60, now.AddHours(-18));
        return r;
    }

    static void SetProp(KeepRequest r, string name, object? value) =>
        typeof(KeepRequest).GetProperty(name)!.SetValue(r, value);

    // -----------------------------------------------------------------------
    // MarkFeedbackReviewed — success path
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkFeedbackReviewed_succeeds_and_returns_FeedbackReviewed_event()
    {
        var r = EligibleRequest();
        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(KeepRequestEventType.FeedbackReviewed, result.Value.EventType);
        Assert.Equal(KeepRequestEventVisibility.Internal, result.Value.Visibility);
        Assert.Equal(ActorId, result.Value.ActorAccountUserId);
        Assert.Equal(ActorName, result.Value.ActorDisplayName);
        Assert.Equal(BaseTime, result.Value.OccurredAtUtc);
        Assert.Null(result.Value.Content);
    }

    [Fact]
    public void MarkFeedbackReviewed_stores_review_metadata_on_request()
    {
        var r = EligibleRequest();
        r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.Equal(BaseTime, r.FeedbackReviewedAtUtc);
        Assert.Equal(ActorId, r.FeedbackReviewedByAccountUserId);
        Assert.Null(r.FeedbackReviewNote);
    }

    [Fact]
    public void MarkFeedbackReviewed_stores_and_trims_optional_note()
    {
        var r = EligibleRequest();
        r.MarkFeedbackReviewed(note: "  Follow-up call placed.  ", ActorId, ActorName, BaseTime);

        Assert.Equal("Follow-up call placed.", r.FeedbackReviewNote);
    }

    [Fact]
    public void MarkFeedbackReviewed_note_stored_on_event_content()
    {
        var r = EligibleRequest();
        var result = r.MarkFeedbackReviewed(note: "Called customer.", ActorId, ActorName, BaseTime);

        Assert.Equal("Called customer.", result.Value!.Content);
    }

    [Fact]
    public void MarkFeedbackReviewed_null_note_produces_null_event_content()
    {
        var r = EligibleRequest();
        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.Null(result.Value!.Content);
    }

    [Fact]
    public void MarkFeedbackReviewed_whitespace_note_treated_as_null()
    {
        var r = EligibleRequest();
        r.MarkFeedbackReviewed(note: "   ", ActorId, ActorName, BaseTime);

        Assert.Null(r.FeedbackReviewNote);
    }

    [Fact]
    public void MarkFeedbackReviewed_clears_UnresolvedFeedback_attention()
    {
        var r = EligibleRequest();
        Assert.Equal(AttentionLevel.Waiting, r.AttentionLevel);

        r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.Equal(AttentionLevel.None, r.AttentionLevel);
        Assert.Equal(WaitingDirection.None, r.WaitingDirection);
        Assert.Null(r.AttentionReason);
        Assert.Equal(PriorityBand.Standard, r.PriorityBand);
        Assert.Null(r.AttentionSinceUtc);
        Assert.Null(r.NextAttentionAtUtc);
        Assert.Equal(BaseTime, r.AttentionClearedAtUtc);
        Assert.Equal(ActorId, r.AttentionClearedByAccountUserId);
        Assert.Equal("feedback_reviewed", r.AttentionClearReason);
    }

    [Fact]
    public void MarkFeedbackReviewed_does_not_change_status_or_original_feedback_fields()
    {
        var r = EligibleRequest();
        var originalWasResolved = r.FeedbackWasResolved;
        var originalComment = r.FeedbackComment;
        var originalSubmittedAt = r.FeedbackSubmittedAtUtc;

        r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.Equal(KeepRequestStatus.Closed, r.Status);
        Assert.Equal(originalWasResolved, r.FeedbackWasResolved);
        Assert.Equal(originalComment, r.FeedbackComment);
        Assert.Equal(originalSubmittedAt, r.FeedbackSubmittedAtUtc);
    }

    [Fact]
    public void MarkFeedbackReviewed_note_at_exactly_2000_chars_succeeds()
    {
        var r = EligibleRequest();
        var result = r.MarkFeedbackReviewed(new string('x', 2000), ActorId, ActorName, BaseTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(2000, r.FeedbackReviewNote!.Length);
    }

    // -----------------------------------------------------------------------
    // MarkFeedbackReviewed — eligibility failures → FeedbackReviewUnavailable
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkFeedbackReviewed_non_Closed_status_returns_unavailable()
    {
        var r = KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Alice", "555-0001", null, "Desc", "REF002",
            "tok_" + Guid.NewGuid().ToString("N"), BaseTime, 60);
        // Status = Received, no feedback, no attention
        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FeedbackReviewUnavailable.Code, result.Error!.Code);
    }

    [Fact]
    public void MarkFeedbackReviewed_Cancelled_returns_unavailable()
    {
        var r = KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Alice", "555-0001", null, "Desc", "REF003",
            "tok_" + Guid.NewGuid().ToString("N"), BaseTime, 60);
        r.ChangeStatus(KeepRequestStatus.Cancelled, "Cancelling.", ActorId, ActorName, BaseTime);

        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FeedbackReviewUnavailable.Code, result.Error!.Code);
    }

    [Fact]
    public void MarkFeedbackReviewed_no_feedback_submitted_returns_unavailable()
    {
        // Closed but no feedback submitted
        var r = KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Alice", "555-0001", null, "Desc", "REF004",
            "tok_" + Guid.NewGuid().ToString("N"), BaseTime.AddHours(-48), 60);
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, BaseTime.AddHours(-24));
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, BaseTime.AddHours(-20));

        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FeedbackReviewUnavailable.Code, result.Error!.Code);
    }

    [Fact]
    public void MarkFeedbackReviewed_positive_feedback_returns_unavailable()
    {
        var r = KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Alice", "555-0001", null, "Desc", "REF005",
            "tok_" + Guid.NewGuid().ToString("N"), BaseTime.AddHours(-48), 60);
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, BaseTime.AddHours(-24));
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, BaseTime.AddHours(-20));
        r.SubmitFeedback(wasResolved: true, comment: null, priorityResponseTargetMinutes: 60, BaseTime.AddHours(-18));

        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FeedbackReviewUnavailable.Code, result.Error!.Code);
    }

    [Fact]
    public void AcknowledgeAttention_on_UnresolvedFeedback_returns_AttentionRequiresFeedbackReview()
    {
        // G7a/ADR-300: generic acknowledgement must not clear UnresolvedFeedback attention.
        // Prove the error is returned and all state is unchanged, then MarkFeedbackReviewed still succeeds.
        var r = EligibleRequest();
        var preFeedbackReviewedAt = r.FeedbackReviewedAtUtc;
        var preFeedbackReviewedBy = r.FeedbackReviewedByAccountUserId;
        var preAttentionLevel = r.AttentionLevel;
        var preAttentionReason = r.AttentionReason;

        var ackResult = r.AcknowledgeAttention("Handled separately.", ActorId, ActorName, BaseTime.AddMinutes(-5));

        Assert.False(ackResult.IsSuccess);
        Assert.Equal(KeepRequestErrors.AttentionRequiresFeedbackReview.Code, ackResult.Error!.Code);
        Assert.Equal(preAttentionLevel, r.AttentionLevel);
        Assert.Equal(preAttentionReason, r.AttentionReason);
        Assert.Equal(preFeedbackReviewedAt, r.FeedbackReviewedAtUtc);
        Assert.Equal(preFeedbackReviewedBy, r.FeedbackReviewedByAccountUserId);

        // MarkFeedbackReviewed must still succeed on the unchanged request.
        var reviewResult = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);
        Assert.True(reviewResult.IsSuccess);
        Assert.Equal(KeepRequestEventType.FeedbackReviewed, reviewResult.Value!.EventType);
    }

    [Fact]
    public void MarkFeedbackReviewed_different_attention_reason_returns_unavailable()
    {
        // D1: active attention but not UnresolvedFeedback → unavailable.
        var r = EligibleRequest();
        // Override the attention reason via reflection to simulate a different active state.
        SetProp(r, "AttentionReason", AttentionReason.CustomerMessage);

        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FeedbackReviewUnavailable.Code, result.Error!.Code);
    }

    // -----------------------------------------------------------------------
    // MarkFeedbackReviewed — already reviewed → FeedbackAlreadyReviewed
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkFeedbackReviewed_duplicate_returns_already_reviewed()
    {
        var r = EligibleRequest();
        r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime);

        var result = r.MarkFeedbackReviewed(note: null, ActorId, ActorName, BaseTime.AddMinutes(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FeedbackAlreadyReviewed.Code, result.Error!.Code);
    }

    // -----------------------------------------------------------------------
    // MarkFeedbackReviewed — note validation → FeedbackReviewNoteTooLong
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkFeedbackReviewed_note_over_2000_chars_returns_note_too_long()
    {
        var r = EligibleRequest();
        var result = r.MarkFeedbackReviewed(new string('x', 2001), ActorId, ActorName, BaseTime);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FeedbackReviewNoteTooLong.Code, result.Error!.Code);
    }

    [Fact]
    public void MarkFeedbackReviewed_note_too_long_does_not_mutate_request()
    {
        var r = EligibleRequest();
        r.MarkFeedbackReviewed(new string('x', 2001), ActorId, ActorName, BaseTime);

        // Review metadata should not have been set
        Assert.Null(r.FeedbackReviewedAtUtc);
        Assert.Null(r.FeedbackReviewedByAccountUserId);
    }

    // -----------------------------------------------------------------------
    // MarkFeedbackReviewed — programmer-error guards (ArgumentException)
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkFeedbackReviewed_empty_actor_id_throws()
    {
        var r = EligibleRequest();
        Assert.Throws<ArgumentException>(() =>
            r.MarkFeedbackReviewed(null, Guid.Empty, ActorName, BaseTime));
    }

    [Fact]
    public void MarkFeedbackReviewed_blank_actor_name_throws()
    {
        var r = EligibleRequest();
        Assert.Throws<ArgumentException>(() =>
            r.MarkFeedbackReviewed(null, ActorId, "   ", BaseTime));
    }

    [Fact]
    public void MarkFeedbackReviewed_default_timestamp_throws()
    {
        var r = EligibleRequest();
        Assert.Throws<ArgumentException>(() =>
            r.MarkFeedbackReviewed(null, ActorId, ActorName, default));
    }

    // -----------------------------------------------------------------------
    // FeedbackReviewPolicy — age bucket boundaries
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(23)]
    public void FeedbackReviewPolicy_less_than_24h_is_New(int hours)
    {
        var submitted = BaseTime.AddHours(-hours);
        Assert.Equal(FeedbackReviewAgeBucket.New,
            FeedbackReviewPolicy.ComputeAgeBucket(submitted, BaseTime));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(72)] // exactly 72h is still Aging per ADR-279 "overdue: more than 72h"
    public void FeedbackReviewPolicy_24_to_72h_inclusive_is_Aging(int hours)
    {
        var submitted = BaseTime.AddHours(-hours);
        Assert.Equal(FeedbackReviewAgeBucket.Aging,
            FeedbackReviewPolicy.ComputeAgeBucket(submitted, BaseTime));
    }

    [Theory]
    [InlineData(73)]
    [InlineData(96)]
    [InlineData(168)]
    public void FeedbackReviewPolicy_more_than_72h_is_Overdue(int hours)
    {
        var submitted = BaseTime.AddHours(-hours);
        Assert.Equal(FeedbackReviewAgeBucket.Overdue,
            FeedbackReviewPolicy.ComputeAgeBucket(submitted, BaseTime));
    }

    [Fact]
    public void FeedbackReviewPolicy_ComputeReviewDueAtUtc_is_72h_after_submission()
    {
        var submitted = new DateTime(2026, 6, 19, 8, 0, 0, DateTimeKind.Utc);
        var expected = submitted.AddHours(72);
        Assert.Equal(expected, FeedbackReviewPolicy.ComputeReviewDueAtUtc(submitted));
    }
}
