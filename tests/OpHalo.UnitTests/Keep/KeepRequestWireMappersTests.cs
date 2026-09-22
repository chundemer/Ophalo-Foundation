using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

// Maintainability review item 2.2: exhaustive lock-down of every enum-to-wire-string mapping now
// shared across KeepRequestDetailMapper, GetKeepRequestListService, GetAvailableKeepRequestsService,
// LookupKeepRequestByPhoneService, and KeepCustomerPageMapper's status mapping — one enum member
// missing a case here breaks every one of those consumers identically, so this must stay exhaustive
// per enum, not spot-checked.
public class KeepRequestWireMappersTests
{
    [Theory]
    [InlineData(KeepRequestStatus.Received, "received")]
    [InlineData(KeepRequestStatus.Scheduled, "scheduled")]
    [InlineData(KeepRequestStatus.InProgress, "in_progress")]
    [InlineData(KeepRequestStatus.PendingCustomer, "pending_customer")]
    [InlineData(KeepRequestStatus.Resolved, "resolved")]
    [InlineData(KeepRequestStatus.Closed, "closed")]
    [InlineData(KeepRequestStatus.Cancelled, "cancelled")]
    [InlineData(KeepRequestStatus.Spam, "spam")]
    [InlineData(KeepRequestStatus.Test, "test")]
    public void MapStatus_maps_every_enum_member(KeepRequestStatus status, string expected) =>
        Assert.Equal(expected, KeepRequestWireMappers.MapStatus(status));

    [Fact]
    public void MapStatus_throws_on_unmapped_value() =>
        Assert.Throws<InvalidOperationException>(() => KeepRequestWireMappers.MapStatus((KeepRequestStatus)999));

    [Theory]
    [InlineData(AttentionLevel.None, "none")]
    [InlineData(AttentionLevel.Waiting, "waiting")]
    [InlineData(AttentionLevel.NeedsAttention, "needs_attention")]
    [InlineData(AttentionLevel.Overdue, "overdue")]
    public void MapAttentionLevel_maps_every_enum_member(AttentionLevel level, string expected) =>
        Assert.Equal(expected, KeepRequestWireMappers.MapAttentionLevel(level));

    [Fact]
    public void MapAttentionLevel_throws_on_unmapped_value() =>
        Assert.Throws<InvalidOperationException>(() => KeepRequestWireMappers.MapAttentionLevel((AttentionLevel)999));

    [Theory]
    [InlineData(WaitingDirection.None, "none")]
    [InlineData(WaitingDirection.Business, "business")]
    [InlineData(WaitingDirection.Customer, "customer")]
    public void MapWaitingDirection_maps_every_enum_member(WaitingDirection direction, string expected) =>
        Assert.Equal(expected, KeepRequestWireMappers.MapWaitingDirection(direction));

    [Fact]
    public void MapWaitingDirection_throws_on_unmapped_value() =>
        Assert.Throws<InvalidOperationException>(() => KeepRequestWireMappers.MapWaitingDirection((WaitingDirection)999));

    [Theory]
    [InlineData(AttentionReason.CustomerMessage, "customer_message")]
    [InlineData(AttentionReason.UpdateRequest, "update_request")]
    [InlineData(AttentionReason.ScheduleChangeRequest, "schedule_change_request")]
    [InlineData(AttentionReason.ChangeOrCancelRequest, "change_or_cancel_request")]
    [InlineData(AttentionReason.Complaint, "complaint")]
    [InlineData(AttentionReason.FirstResponseDue, "first_response_due")]
    [InlineData(AttentionReason.UnresolvedFeedback, "unresolved_feedback")]
    [InlineData(AttentionReason.CallRequested, "call_requested")]
    [InlineData(AttentionReason.TimingChangeRequested, "timing_change_requested")]
    [InlineData(AttentionReason.CancellationRequested, "cancellation_requested")]
    [InlineData(AttentionReason.FollowUpDue, "follow_up_due")]
    public void MapAttentionReason_maps_every_enum_member(AttentionReason reason, string expected) =>
        Assert.Equal(expected, KeepRequestWireMappers.MapAttentionReason(reason));

    [Fact]
    public void MapAttentionReason_throws_on_unmapped_value() =>
        Assert.Throws<InvalidOperationException>(() => KeepRequestWireMappers.MapAttentionReason((AttentionReason)999));

    [Theory]
    [InlineData(FollowUpReason.Weather, "weather")]
    [InlineData(FollowUpReason.Parts, "parts")]
    [InlineData(FollowUpReason.CustomerDelay, "customer_delay")]
    [InlineData(FollowUpReason.BusinessOperatorAvailability, "business_operator_availability")]
    [InlineData(FollowUpReason.ThirdParty, "third_party")]
    [InlineData(FollowUpReason.Other, "other")]
    public void MapFollowUpReason_maps_every_enum_member(FollowUpReason reason, string expected) =>
        Assert.Equal(expected, KeepRequestWireMappers.MapFollowUpReason(reason));

    [Fact]
    public void MapFollowUpReason_throws_on_unmapped_value() =>
        Assert.Throws<InvalidOperationException>(() => KeepRequestWireMappers.MapFollowUpReason((FollowUpReason)999));

    [Theory]
    [InlineData(ContactPreference.NoPreference, "no_preference")]
    [InlineData(ContactPreference.TextMessage, "text_message")]
    [InlineData(ContactPreference.PhoneCall, "phone_call")]
    [InlineData(ContactPreference.Email, "email")]
    public void MapContactPreference_maps_every_enum_member(ContactPreference preference, string expected) =>
        Assert.Equal(expected, KeepRequestWireMappers.MapContactPreference(preference));

    [Fact]
    public void MapContactPreference_throws_on_unmapped_value() =>
        Assert.Throws<InvalidOperationException>(() => KeepRequestWireMappers.MapContactPreference((ContactPreference)999));

    // Staff-facing mapping only. KeepCustomerPageMapper keeps its own customer-facing variant
    // (hides Routine, returns null instead of throwing) — deliberately not this one.
    [Theory]
    [InlineData(IntakeUrgency.Routine, "routine")]
    [InlineData(IntakeUrgency.Soon, "soon")]
    [InlineData(IntakeUrgency.Urgent, "urgent")]
    public void MapIntakeUrgency_maps_every_enum_member(IntakeUrgency urgency, string expected) =>
        Assert.Equal(expected, KeepRequestWireMappers.MapIntakeUrgency(urgency));

    [Fact]
    public void MapIntakeUrgency_throws_on_unmapped_value() =>
        Assert.Throws<InvalidOperationException>(() => KeepRequestWireMappers.MapIntakeUrgency((IntakeUrgency)999));
}
