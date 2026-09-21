using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// ADR-505 slice 10: the deadline-delegate overload of <c>SubmitFeedback</c>. Closing clears attention,
/// so negative feedback is always a new obligation and stamps the delegate's result literally.
/// </summary>
public class KeepRequestFeedbackDeadlineTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly DateTime T0 = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    static KeepRequest ClosedRequest()
    {
        var request = KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Jane", "04123", null, "desc", "REF1", "tok", T0.AddDays(-1), 60);
        var actor = Guid.NewGuid();
        Assert.True(request.ChangeStatus(KeepRequestStatus.Resolved, null, actor, "Op", T0.AddHours(-2)).IsSuccess);
        Assert.True(request.ChangeStatus(KeepRequestStatus.Closed, null, actor, "Op", T0.AddHours(-1)).IsSuccess);
        Assert.Null(request.NextAttentionAtUtc); // closing clears attention: no existing deadline
        return request;
    }

    /// <summary>Records every call and returns a fixed deadline.</summary>
    sealed class Deadline(DateTime? result)
    {
        public int Calls { get; private set; }

        public DateTime? For()
        {
            Calls++;
            return result;
        }
    }

    [Fact]
    public void Negative_feedback_stamps_the_delegate_deadline_with_one_call()
    {
        var request = ClosedRequest();
        var deadline = new Deadline(T0.AddHours(20));

        var result = request.SubmitFeedback(false, "still broken", deadline.For, T0);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, deadline.Calls);
        Assert.Equal(T0.AddHours(20), request.NextAttentionAtUtc);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(AttentionReason.UnresolvedFeedback, request.AttentionReason);
        Assert.Equal(PriorityBand.Priority, request.PriorityBand);
        Assert.Equal(T0, request.AttentionSinceUtc);
        Assert.Equal(KeepRequestStatus.Closed, request.Status); // never reopens
    }

    [Fact]
    public void Negative_feedback_with_a_failed_calculation_raises_attention_with_no_deadline()
    {
        var request = ClosedRequest();
        var deadline = new Deadline(null);

        Assert.True(request.SubmitFeedback(false, null, deadline.For, T0).IsSuccess);

        Assert.Equal(1, deadline.Calls);
        Assert.Null(request.NextAttentionAtUtc); // no duration-derived fallback
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(AttentionReason.UnresolvedFeedback, request.AttentionReason);
        Assert.True(request.FeedbackSubmittedAtUtc.HasValue); // the one-time feedback is recorded
    }

    [Fact]
    public void Positive_feedback_never_calls_the_delegate_and_raises_no_attention()
    {
        var request = ClosedRequest();
        var deadline = new Deadline(T0.AddHours(1));

        Assert.True(request.SubmitFeedback(true, "thanks", deadline.For, T0).IsSuccess);

        Assert.Equal(0, deadline.Calls);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.Null(request.NextAttentionAtUtc);
    }

    [Fact]
    public void Validation_failures_never_call_the_delegate()
    {
        var deadline = new Deadline(T0.AddHours(1));

        var open = KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Jane", "04123", null, "desc", "REF2", "tok2", T0.AddDays(-1), 60);
        var notClosed = open.SubmitFeedback(false, null, deadline.For, T0);

        var already = ClosedRequest();
        Assert.True(already.SubmitFeedback(true, null, deadline.For, T0).IsSuccess);
        var duplicate = already.SubmitFeedback(false, null, deadline.For, T0);

        var tooLong = ClosedRequest().SubmitFeedback(false, new string('x', 2001), deadline.For, T0);

        Assert.Equal(KeepRequestErrors.FeedbackUnavailable, notClosed.Error);
        Assert.Equal(KeepRequestErrors.FeedbackAlreadySubmitted, duplicate.Error);
        Assert.Equal(KeepRequestErrors.FeedbackCommentTooLong, tooLong.Error);
        Assert.Equal(0, deadline.Calls);
    }

    [Fact]
    public void Legacy_overload_still_stamps_now_plus_the_priority_minutes()
    {
        var request = ClosedRequest();

        Assert.True(request.SubmitFeedback(false, "bad", 45, T0).IsSuccess);

        Assert.Equal(T0.AddMinutes(45), request.NextAttentionAtUtc);
        Assert.Equal(AttentionReason.UnresolvedFeedback, request.AttentionReason);
    }

    [Fact]
    public void Null_delegate_and_default_now_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ClosedRequest().SubmitFeedback(false, null, (Func<DateTime?>)null!, T0));
        Assert.Throws<ArgumentException>(() =>
            ClosedRequest().SubmitFeedback(false, null, new Deadline(T0).For, default));
    }
}
