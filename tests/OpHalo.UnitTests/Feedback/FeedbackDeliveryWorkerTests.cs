using Microsoft.Extensions.Logging.Abstractions;
using OpHalo.Foundation.Application.Feedback;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using OpHalo.SharedKernel.Abstractions;
using Xunit;

namespace OpHalo.UnitTests.Feedback;

/// <summary>
/// Locks the 038-2c delivery worker: the backoff retry schedule (5/15/60/180 min after the
/// synchronous attempt), abandonment after the 6th failed attempt with the body retained, the
/// rate-limited backlog/abandoned founder alert (no body, abandoned wins the tick), and the
/// retention sweep cutoffs.
/// </summary>
public class FeedbackDeliveryWorkerTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakePersistence _persistence = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FounderAlertThrottle _throttle = new();
    private DateTime _now = Now;

    private FeedbackDeliveryWorker Worker() => new(
        _persistence,
        _notifier,
        _throttle,
        new FakeClock(() => _now),
        NullLogger<FeedbackDeliveryWorker>.Instance);

    private static FeedbackSubmission Pending(int failedAttempts = 1, DateTime? createdAtUtc = null)
    {
        var submission = FeedbackSubmission.Create(
            AccountId, UserId, "the scope dialog froze", FeedbackCategory.Bug,
            """{"route":"/requests"}""", createdAtUtc ?? Now.AddHours(-1));

        // Simulate prior failed attempts (the 038-2b synchronous attempt plus any earlier retries).
        for (var i = 0; i < failedAttempts; i++)
        {
            submission.MarkAttempted(submission.CreatedAtUtc);
            if (i < failedAttempts - 1)
                submission.ScheduleRetry(submission.CreatedAtUtc);
        }

        return submission;
    }

    [Fact]
    public async Task Confirmed_delivery_marks_delivered_and_scrubs_the_body()
    {
        var submission = Pending();
        _persistence.Due.Add(submission);
        _notifier.DeliveryResult = true;

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Equal(FeedbackDeliveryState.Delivered, submission.DeliveryState);
        Assert.Null(submission.Message);
        Assert.Null(submission.ContextJson);
        Assert.Equal(Now, submission.DeliveredAtUtc);
        Assert.Contains(submission, _persistence.Updated);
        Assert.Equal("feedback_submitted", _notifier.Events[0].Type);
    }

    [Theory]
    [InlineData(1, 5)]   // synchronous attempt already failed → first retry fails → wait 5 min
    [InlineData(2, 15)]
    [InlineData(3, 60)]
    [InlineData(4, 180)]
    public async Task Failed_retry_schedules_the_next_attempt_on_the_backoff_schedule(int priorFailedAttempts, int expectedMinutes)
    {
        var submission = Pending(failedAttempts: priorFailedAttempts);
        _persistence.Due.Add(submission);
        _notifier.DeliveryResult = false;

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Equal(FeedbackDeliveryState.Pending, submission.DeliveryState);
        Assert.Equal(priorFailedAttempts + 1, submission.AttemptCount);
        Assert.Equal(Now.AddMinutes(expectedMinutes), submission.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Sixth_failed_attempt_abandons_the_row_and_retains_the_body()
    {
        var submission = Pending(failedAttempts: 5);
        _persistence.Due.Add(submission);
        _notifier.DeliveryResult = false;

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Equal(FeedbackDeliveryState.Abandoned, submission.DeliveryState);
        Assert.Equal(6, submission.AttemptCount);
        Assert.Equal("the scope dialog froze", submission.Message);
        Assert.Null(submission.NextAttemptAtUtc);

        var alert = Assert.Single(_notifier.Events, e => e.Type == "delivery_abandoned");
        Assert.DoesNotContain("scope dialog", alert.Summary);
        Assert.Equal(1, alert.Count);
    }

    [Fact]
    public async Task Abandoned_alert_is_rate_limited_to_one_per_thirty_minutes()
    {
        _persistence.Due.Add(Pending(failedAttempts: 5));
        _notifier.DeliveryResult = false;
        await Worker().RunOnceAsync(CancellationToken.None);

        _persistence.Due.Clear();
        _persistence.Due.Add(Pending(failedAttempts: 5));
        _now = Now.AddMinutes(20);
        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Single(_notifier.Events, e => e.Type == "delivery_abandoned");

        _persistence.Due.Clear();
        _persistence.Due.Add(Pending(failedAttempts: 5));
        _now = Now.AddMinutes(31);
        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, _notifier.Events.Count(e => e.Type == "delivery_abandoned"));
    }

    [Fact]
    public async Task Backlog_alert_fires_when_at_least_three_pending_rows_are_older_than_fifteen_minutes()
    {
        _persistence.BacklogCount = 3;
        _persistence.BacklogOldestCreatedAtUtc = Now.AddHours(-2);

        await Worker().RunOnceAsync(CancellationToken.None);

        var alert = Assert.Single(_notifier.Events, e => e.Type == "delivery_backlog");
        Assert.Equal(3, alert.Count);
        Assert.Equal("2h 0m", alert.OldestAge);
        Assert.DoesNotContain("scope", alert.Summary);
    }

    [Fact]
    public async Task Backlog_alert_does_not_fire_below_the_threshold()
    {
        _persistence.BacklogCount = 2;
        _persistence.BacklogOldestCreatedAtUtc = Now.AddHours(-2);

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.DoesNotContain(_notifier.Events, e => e.Type == "delivery_backlog");
    }

    [Fact]
    public async Task An_abandonment_suppresses_the_backlog_alert_on_the_same_pass()
    {
        _persistence.Due.Add(Pending(failedAttempts: 5));
        _notifier.DeliveryResult = false;
        _persistence.BacklogCount = 10;
        _persistence.BacklogOldestCreatedAtUtc = Now.AddHours(-3);

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Single(_notifier.Events, e => e.Type == "delivery_abandoned");
        Assert.DoesNotContain(_notifier.Events, e => e.Type == "delivery_backlog");
    }

    [Fact]
    public async Task The_retention_sweep_uses_seven_and_thirty_day_cutoffs()
    {
        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Equal(Now.AddDays(-7), _persistence.SweepDeliveredBefore);
        Assert.Equal(Now.AddDays(-30), _persistence.SweepAbandonedBefore);
    }

    [Fact]
    public async Task A_post_delivery_persistence_failure_is_swallowed()
    {
        var submission = Pending();
        _persistence.Due.Add(submission);
        _persistence.ThrowOnUpdate = true;
        _notifier.DeliveryResult = true;

        var ex = await Record.ExceptionAsync(() => Worker().RunOnceAsync(CancellationToken.None));

        Assert.Null(ex);
    }

    private sealed class FakePersistence : IFeedbackPersistence
    {
        public List<FeedbackSubmission> Due { get; } = [];
        public List<FeedbackSubmission> Updated { get; } = [];
        public bool ThrowOnUpdate { get; set; }

        public int BacklogCount { get; set; }
        public DateTime? BacklogOldestCreatedAtUtc { get; set; }

        public DateTime? SweepDeliveredBefore { get; private set; }
        public DateTime? SweepAbandonedBefore { get; private set; }

        public Task AddAsync(FeedbackSubmission submission, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateAsync(FeedbackSubmission submission, CancellationToken cancellationToken)
        {
            if (ThrowOnUpdate) throw new InvalidOperationException("update failed");
            Updated.Add(submission);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FeedbackSubmission>> GetDueForRetryAsync(
            DateTime nowUtc, int batchLimit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeedbackSubmission>>(Due.ToList());

        public Task<(int Count, DateTime? OldestCreatedAtUtc)> CountAndOldestPendingBeforeAsync(
            DateTime createdBeforeUtc, CancellationToken cancellationToken) =>
            Task.FromResult((BacklogCount, BacklogCount == 0 ? null : BacklogOldestCreatedAtUtc));

        public Task<int> DeleteExpiredAsync(
            DateTime deliveredBeforeUtc, DateTime abandonedBeforeUtc, CancellationToken cancellationToken)
        {
            SweepDeliveredBefore = deliveredBeforeUtc;
            SweepAbandonedBefore = abandonedBeforeUtc;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeNotifier : IFounderNotifier
    {
        public bool DeliveryResult { get; set; }
        public List<FounderEvent> Events { get; } = [];

        public Task<bool> NotifyAsync(FounderEvent founderEvent, CancellationToken cancellationToken)
        {
            Events.Add(founderEvent);
            return Task.FromResult(founderEvent.Type == "feedback_submitted" && DeliveryResult);
        }
    }

    private sealed class FakeClock(Func<DateTime> now) : IClock
    {
        public DateTime UtcNow => now();
    }
}
