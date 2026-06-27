using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepProductOpsEventTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly DateTime OccurredAt = new(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Record_sets_all_fields()
    {
        var evt = KeepProductOpsEvent.Record(AccountId, KeepProductOpsEventType.ProfileAndContactSaved, OccurredAt);

        Assert.Equal(AccountId, evt.AccountId);
        Assert.Equal(KeepProductOpsEventType.ProfileAndContactSaved, evt.EventType);
        Assert.Equal(OccurredAt, evt.OccurredAtUtc);
        Assert.NotEqual(Guid.Empty, evt.Id);
    }

    [Fact]
    public void Record_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepProductOpsEvent.Record(Guid.Empty, KeepProductOpsEventType.PolicySaved, OccurredAt));

    [Theory]
    [InlineData(KeepProductOpsEventType.ProfileAndContactSaved)]
    [InlineData(KeepProductOpsEventType.PolicySaved)]
    [InlineData(KeepProductOpsEventType.QuickCaptureExerciseDone)]
    [InlineData(KeepProductOpsEventType.TrackerReviewDone)]
    [InlineData(KeepProductOpsEventType.SpamClassificationExplained)]
    public void Record_accepts_any_v1_event_type(KeepProductOpsEventType eventType)
    {
        var evt = KeepProductOpsEvent.Record(AccountId, eventType, OccurredAt);
        Assert.Equal(eventType, evt.EventType);
    }
}
