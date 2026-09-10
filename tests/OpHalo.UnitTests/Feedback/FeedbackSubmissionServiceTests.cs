using Microsoft.Extensions.Logging.Abstractions;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Feedback;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using OpHalo.SharedKernel.Abstractions;
using Xunit;

namespace OpHalo.UnitTests.Feedback;

/// <summary>
/// Locks the persist-first, at-least-once submission flow (BL149 D5 / "038-2 slice split"):
/// validate → commit Pending → one synchronous delivery → scrub-on-confirmed-success, with only a
/// pre-delivery persistence failure surfaced as a non-"sent" (503).
/// </summary>
public class FeedbackSubmissionServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakePersistence _persistence = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeCurrentUser _currentUser = new(AccountId, UserId, isAuthenticated: true);

    private FeedbackSubmissionService Service() => new(
        _persistence,
        _notifier,
        _currentUser,
        new FakeClock(Now),
        NullLogger<FeedbackSubmissionService>.Instance);

    private Task<OpHalo.SharedKernel.Results.Result<FeedbackSubmissionOutcome>> Submit(
        string? message = "The schedule view is confusing.",
        FeedbackCategory category = FeedbackCategory.Confusing,
        string? contextJson = null) =>
        Service().SubmitAsync(message, category, contextJson, CancellationToken.None);

    [Fact]
    public async Task Unauthenticated_caller_is_rejected_without_persisting()
    {
        _currentUser.IsAuthenticated = false;

        var result = await Submit();

        Assert.True(result.IsFailure);
        Assert.Equal("auth.unauthorized", result.Error.Code);
        Assert.Empty(_persistence.Added);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_message_is_rejected_without_persisting(string message)
    {
        var result = await Submit(message);

        Assert.True(result.IsFailure);
        Assert.Equal("feedback.message_required", result.Error.Code);
        Assert.Empty(_persistence.Added);
        Assert.Equal(0, _notifier.CallCount);
    }

    [Fact]
    public async Task Over_length_message_is_rejected_as_too_long()
    {
        var result = await Submit(new string('x', FeedbackSubmission.MessageMaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("feedback.message_too_long", result.Error.Code);
        Assert.Empty(_persistence.Added);
    }

    [Fact]
    public async Task Pre_delivery_persistence_failure_returns_persist_failed_and_never_calls_notifier()
    {
        _persistence.ThrowOnAdd = true;

        var result = await Submit();

        Assert.True(result.IsFailure);
        Assert.Equal("feedback.persist_failed", result.Error.Code);
        Assert.Equal(0, _notifier.CallCount);
    }

    [Fact]
    public async Task Confirmed_delivery_marks_delivered_scrubs_body_and_persists_twice()
    {
        _notifier.Result = true;

        var result = await Submit(contextJson: "{\"route\":\"/schedule\"}");

        Assert.True(result.IsSuccess);
        Assert.Equal(FeedbackSubmissionOutcome.Delivered, result.Value);

        var row = Assert.Single(_persistence.Added);
        Assert.Equal(FeedbackDeliveryState.Delivered, row.DeliveryState);
        Assert.Null(row.Message);
        Assert.Null(row.ContextJson);
        Assert.Equal(1, row.AttemptCount);
        Assert.Same(row, Assert.Single(_persistence.Updated));
    }

    [Fact]
    public async Task Notifier_payload_carries_category_message_and_correlation_id()
    {
        _notifier.Result = true;

        await Submit(message: "  Please add dark mode  ", category: FeedbackCategory.MissingThing, contextJson: "{\"a\":1}");

        var evt = Assert.Single(_notifier.Events);
        Assert.Equal("feedback_submitted", evt.Type);
        Assert.Equal("[MissingThing] Please add dark mode", evt.Summary);
        Assert.Equal("{\"a\":1}", evt.Context);
        Assert.Equal(_persistence.Added[0].Id.ToString(), evt.Correlation);
    }

    [Fact]
    public async Task Unconfirmed_delivery_leaves_a_pending_row_with_the_body_intact_and_returns_queued()
    {
        _notifier.Result = false;

        var result = await Submit();

        Assert.True(result.IsSuccess);
        Assert.Equal(FeedbackSubmissionOutcome.Queued, result.Value);

        var row = Assert.Single(_persistence.Added);
        Assert.Equal(FeedbackDeliveryState.Pending, row.DeliveryState);
        Assert.NotNull(row.Message);
        Assert.Equal(1, row.AttemptCount);
        Assert.Single(_persistence.Updated);
    }

    [Fact]
    public async Task Post_delivery_update_failure_still_reports_delivered()
    {
        _notifier.Result = true;
        _persistence.ThrowOnUpdate = true;

        var result = await Submit();

        Assert.True(result.IsSuccess);
        Assert.Equal(FeedbackSubmissionOutcome.Delivered, result.Value);
    }

    private sealed class FakePersistence : IFeedbackPersistence
    {
        public List<FeedbackSubmission> Added { get; } = [];
        public List<FeedbackSubmission> Updated { get; } = [];
        public bool ThrowOnAdd { get; set; }
        public bool ThrowOnUpdate { get; set; }

        public Task AddAsync(FeedbackSubmission submission, CancellationToken cancellationToken)
        {
            if (ThrowOnAdd) throw new InvalidOperationException("add failed");
            Added.Add(submission);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(FeedbackSubmission submission, CancellationToken cancellationToken)
        {
            if (ThrowOnUpdate) throw new InvalidOperationException("update failed");
            Updated.Add(submission);
            return Task.CompletedTask;
        }

        // The retry-worker seam methods are exercised by FeedbackDeliveryWorkerTests; the
        // synchronous submission path never calls them.
        public Task<IReadOnlyList<FeedbackSubmission>> GetDueForRetryAsync(
            DateTime nowUtc, int batchLimit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeedbackSubmission>>([]);

        public Task<(int Count, DateTime? OldestCreatedAtUtc)> CountAndOldestPendingBeforeAsync(
            DateTime createdBeforeUtc, CancellationToken cancellationToken) =>
            Task.FromResult((0, (DateTime?)null));

        public Task<int> DeleteExpiredAsync(
            DateTime deliveredBeforeUtc, DateTime abandonedBeforeUtc, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class FakeNotifier : IFounderNotifier
    {
        public bool Result { get; set; }
        public int CallCount { get; private set; }
        public List<FounderEvent> Events { get; } = [];

        public Task<bool> NotifyAsync(FounderEvent founderEvent, CancellationToken cancellationToken)
        {
            CallCount++;
            Events.Add(founderEvent);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCurrentUser(Guid accountId, Guid userId, bool isAuthenticated) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
        public Guid AccountId { get; } = accountId;
        public bool IsAuthenticated { get; set; } = isAuthenticated;
        public bool IsVerified => IsAuthenticated;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
