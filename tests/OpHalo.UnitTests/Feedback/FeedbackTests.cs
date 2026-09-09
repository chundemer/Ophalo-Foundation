using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using Xunit;

namespace OpHalo.UnitTests.Feedback;

/// <summary>
/// Locks the Feedback domain model: Create validation contract (BL149 — trimmed, 1–4,000 chars,
/// defined category) and the persist-first delivery lifecycle (D5 — attempt / deliver+scrub /
/// retry / abandon, all valid only from Pending).
/// </summary>
public class FeedbackTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    private static FeedbackSubmission CreateValid(
        string message = "The schedule view is confusing.",
        FeedbackCategory category = FeedbackCategory.Confusing,
        string? contextJson = "{\"route\":\"/schedule\"}") =>
        FeedbackSubmission.Create(
            accountId: Guid.NewGuid(),
            accountUserId: Guid.NewGuid(),
            message: message,
            category: category,
            contextJson: contextJson,
            nowUtc: Now);

    [Fact]
    public void Create_happy_path_sets_all_fields_and_pending_delivery()
    {
        var accountId = Guid.NewGuid();
        var accountUserId = Guid.NewGuid();

        var feedback = FeedbackSubmission.Create(
            accountId,
            accountUserId,
            "  please add a dark mode  ",
            FeedbackCategory.MissingThing,
            "{\"build\":\"1.2.3\"}",
            Now);

        Assert.NotEqual(Guid.Empty, feedback.Id);
        Assert.Equal(accountId, feedback.AccountId);
        Assert.Equal(accountUserId, feedback.AccountUserId);
        Assert.Equal("please add a dark mode", feedback.Message);
        Assert.Equal(FeedbackCategory.MissingThing, feedback.Category);
        Assert.Equal("{\"build\":\"1.2.3\"}", feedback.ContextJson);
        Assert.Equal(Now, feedback.CreatedAtUtc);
        Assert.Equal(FeedbackDeliveryState.Pending, feedback.DeliveryState);
        Assert.Equal(0, feedback.AttemptCount);
        Assert.Equal(Now, feedback.NextAttemptAtUtc);
        Assert.Null(feedback.LastAttemptAtUtc);
        Assert.Null(feedback.DeliveredAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_rejects_blank_message(string message)
    {
        var ex = Assert.Throws<ArgumentException>(() => CreateValid(message: message));
        Assert.Equal("message", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_message_over_max_length()
    {
        var tooLong = new string('x', FeedbackSubmission.MessageMaxLength + 1);
        var ex = Assert.Throws<ArgumentException>(() => CreateValid(message: tooLong));
        Assert.Equal("message", ex.ParamName);
    }

    [Fact]
    public void Create_accepts_message_at_exactly_max_length()
    {
        var atMax = new string('x', FeedbackSubmission.MessageMaxLength);
        var feedback = CreateValid(message: atMax);
        Assert.Equal(atMax, feedback.Message);
    }

    [Fact]
    public void Create_trims_before_measuring_length()
    {
        var padded = "  " + new string('x', FeedbackSubmission.MessageMaxLength) + "  ";
        var feedback = CreateValid(message: padded);
        Assert.Equal(FeedbackSubmission.MessageMaxLength, feedback.Message!.Length);
    }

    [Fact]
    public void Create_rejects_empty_account_id()
    {
        var ex = Assert.Throws<ArgumentException>(() => FeedbackSubmission.Create(
            Guid.Empty, Guid.NewGuid(), "hi", FeedbackCategory.Other, null, Now));
        Assert.Equal("accountId", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_empty_account_user_id()
    {
        var ex = Assert.Throws<ArgumentException>(() => FeedbackSubmission.Create(
            Guid.NewGuid(), Guid.Empty, "hi", FeedbackCategory.Other, null, Now));
        Assert.Equal("accountUserId", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_undefined_category()
    {
        var ex = Assert.Throws<ArgumentException>(() => CreateValid(category: (FeedbackCategory)99));
        Assert.Equal("category", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_non_utc_now()
    {
        var local = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Local);
        var ex = Assert.Throws<ArgumentException>(() => FeedbackSubmission.Create(
            Guid.NewGuid(), Guid.NewGuid(), "hi", FeedbackCategory.Other, null, local));
        Assert.Equal("nowUtc", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_default_now()
    {
        var ex = Assert.Throws<ArgumentException>(() => FeedbackSubmission.Create(
            Guid.NewGuid(), Guid.NewGuid(), "hi", FeedbackCategory.Other, null, default));
        Assert.Equal("nowUtc", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_normalises_blank_context_json_to_null(string? contextJson)
    {
        var feedback = CreateValid(contextJson: contextJson);
        Assert.Null(feedback.ContextJson);
    }

    [Fact]
    public void MarkAttempted_increments_count_and_records_timestamp()
    {
        var feedback = CreateValid();
        var attemptAt = Now.AddSeconds(2);

        feedback.MarkAttempted(attemptAt);

        Assert.Equal(1, feedback.AttemptCount);
        Assert.Equal(attemptAt, feedback.LastAttemptAtUtc);
        Assert.Equal(FeedbackDeliveryState.Pending, feedback.DeliveryState);
    }

    [Fact]
    public void MarkDelivered_requires_a_recorded_attempt()
    {
        var feedback = CreateValid();
        Assert.Throws<InvalidOperationException>(() => feedback.MarkDelivered(Now));
    }

    [Fact]
    public void MarkAbandoned_requires_a_recorded_attempt()
    {
        var feedback = CreateValid();
        Assert.Throws<InvalidOperationException>(() => feedback.MarkAbandoned(Now));
    }

    [Fact]
    public void MarkDelivered_is_terminal_and_scrubs_the_body()
    {
        var feedback = CreateValid();
        var deliveredAt = Now.AddSeconds(3);

        feedback.MarkAttempted(Now.AddSeconds(2));
        feedback.MarkDelivered(deliveredAt);

        Assert.Equal(FeedbackDeliveryState.Delivered, feedback.DeliveryState);
        Assert.Equal(deliveredAt, feedback.DeliveredAtUtc);
        Assert.Null(feedback.Message);
        Assert.Null(feedback.ContextJson);
        Assert.Null(feedback.NextAttemptAtUtc);
    }

    [Fact]
    public void ScheduleRetry_advances_next_attempt()
    {
        var feedback = CreateValid();
        var nextAt = Now.AddMinutes(5);

        feedback.ScheduleRetry(nextAt);

        Assert.Equal(nextAt, feedback.NextAttemptAtUtc);
        Assert.Equal(FeedbackDeliveryState.Pending, feedback.DeliveryState);
    }

    [Fact]
    public void MarkAbandoned_retains_body_and_clears_retry_eligibility()
    {
        var feedback = CreateValid(message: "keep this for recovery");

        feedback.MarkAttempted(Now.AddSeconds(2));
        feedback.MarkAbandoned(Now.AddHours(4));

        Assert.Equal(FeedbackDeliveryState.Abandoned, feedback.DeliveryState);
        Assert.Equal("keep this for recovery", feedback.Message);
        Assert.Null(feedback.NextAttemptAtUtc);
    }

    [Fact]
    public void Delivery_transitions_are_rejected_once_not_pending()
    {
        var delivered = CreateValid();
        delivered.MarkAttempted(Now.AddSeconds(1));
        delivered.MarkDelivered(Now.AddSeconds(2));
        Assert.Throws<InvalidOperationException>(() => delivered.MarkDelivered(Now));
        Assert.Throws<InvalidOperationException>(() => delivered.MarkAttempted(Now));
        Assert.Throws<InvalidOperationException>(() => delivered.ScheduleRetry(Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => delivered.MarkAbandoned(Now));

        var abandoned = CreateValid();
        abandoned.MarkAttempted(Now.AddSeconds(1));
        abandoned.MarkAbandoned(Now.AddSeconds(2));
        Assert.Throws<InvalidOperationException>(() => abandoned.MarkDelivered(Now));
        Assert.Throws<InvalidOperationException>(() => abandoned.MarkAbandoned(Now));
        Assert.Throws<InvalidOperationException>(() => abandoned.MarkAttempted(Now));
        Assert.Throws<InvalidOperationException>(() => abandoned.ScheduleRetry(Now.AddMinutes(1)));
    }
}
