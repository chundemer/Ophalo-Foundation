using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestNeedsStatusCheckTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly DateTime Now = new(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    static KeepRequest ActiveRequest() =>
        KeepRequest.CreateByBusiness(
            AccountId, CustomerId,
            "Jane Smith", "0412345678", null,
            "Burst pipe", "PQRS0001", "tok_abc", Now, KeepRequestSource.Phone);

    // --- Fail-closed exclusions ---

    [Theory]
    [InlineData(KeepRequestStatus.Resolved)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public void Non_active_status_returns_not_eligible(KeepRequestStatus status)
    {
        var r = ActiveRequest();
        if (status == KeepRequestStatus.Resolved || status == KeepRequestStatus.Closed)
        {
            r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, "Jane", Now);
            if (status == KeepRequestStatus.Closed)
                r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, "Jane", Now);
        }
        else
        {
            r.ChangeStatus(KeepRequestStatus.Cancelled, "Cancelling.", ActorId, "Jane", Now);
        }

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.False(result.IsEligible);
        Assert.Equal("not_active", result.ExclusionReason);
        Assert.Null(result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Active_attention_returns_not_eligible()
    {
        var r = ActiveRequest();
        r.AddCustomerMessage(MessageIntent.GeneralMessage, "Hello?", 60, 120, 60, Now);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.False(result.IsEligible);
        Assert.Equal("active_attention", result.ExclusionReason);
        Assert.Null(result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Future_follow_up_on_returns_not_eligible()
    {
        var r = ActiveRequest();
        r.SetFollowUpOn(Today.AddDays(3), FollowUpReason.Parts, null, ActorId, "Jane", Now);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.False(result.IsEligible);
        Assert.Equal("future_follow_up_on", result.ExclusionReason);
        Assert.Null(result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Future_planned_for_returns_not_eligible()
    {
        var r = ActiveRequest();
        r.SetPlannedFor(Today.AddDays(1), ActorId, "Jane", Now);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.False(result.IsEligible);
        Assert.Equal("future_planned_for", result.ExclusionReason);
        Assert.Null(result.LatestMeaningfulActivityAtUtc);
    }

    // --- Timing suppressors do not exclude when past/today ---

    [Fact]
    public void Past_follow_up_on_does_not_suppress()
    {
        var r = ActiveRequest();
        r.SetFollowUpOn(Today.AddDays(-1), FollowUpReason.Parts, null, ActorId, "Jane", Now);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Follow_up_on_today_does_not_suppress()
    {
        var r = ActiveRequest();
        r.SetFollowUpOn(Today, FollowUpReason.Parts, null, ActorId, "Jane", Now);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Past_planned_for_does_not_suppress()
    {
        var r = ActiveRequest();
        r.SetPlannedFor(Today.AddDays(-2), ActorId, "Jane", Now);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
    }

    // --- Signal max calculation ---

    [Fact]
    public void Eligible_request_returns_business_activity_as_baseline_for_business_created_request()
    {
        // CreateByBusiness sets LastBusinessActivityAt = Now; CreatedAtUtc is default in unit tests.
        var r = ActiveRequest();

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(Now, result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Latest_customer_activity_wins_over_created_at()
    {
        var r = ActiveRequest();
        var laterNow = Now.AddHours(2);
        var ackTime = laterNow.AddMinutes(1);

        // Add a customer message then acknowledge attention so the request stays eligible.
        r.AddCustomerMessage(MessageIntent.GeneralMessage, "Still waiting", 60, 120, 60, laterNow);
        r.AcknowledgeAttention("Seen", ActorId, "Jane", ackTime);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(laterNow, result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Latest_business_activity_wins_over_customer_activity()
    {
        var r = ActiveRequest();
        var customerTime = Now.AddHours(1);
        var ackTime = customerTime.AddMinutes(1);
        var businessTime = Now.AddHours(3);

        r.AddCustomerMessage(MessageIntent.GeneralMessage, "Hi", 60, 120, 60, customerTime);
        r.AcknowledgeAttention("Seen", ActorId, "Jane", ackTime);
        r.AddBusinessUpdate("Following up.", ActorId, "Jane", businessTime);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(businessTime, result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Customer_page_viewed_beats_older_business_activity()
    {
        var r = ActiveRequest();
        var businessTime = Now.AddHours(1);
        var pageViewTime = Now.AddHours(5);

        r.AddBusinessUpdate("Update sent.", ActorId, "Jane", businessTime);
        r.RecordCustomerPageView(pageViewTime);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(pageViewTime, result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Set_follow_up_on_updates_business_activity_and_counts_as_signal()
    {
        var r = ActiveRequest();
        var followUpTime = Now.AddHours(4);

        r.SetFollowUpOn(Today.AddDays(-1), FollowUpReason.Parts, null, ActorId, "Jane", followUpTime);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(followUpTime, result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Clear_follow_up_on_updates_business_activity_and_counts_as_signal()
    {
        var r = ActiveRequest();
        var setTime = Now.AddHours(1);
        var clearTime = Now.AddHours(6);

        r.SetFollowUpOn(Today.AddDays(-1), FollowUpReason.Parts, null, ActorId, "Jane", setTime);
        r.ClearFollowUpOn(ActorId, "Jane", clearTime);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(clearTime, result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Set_planned_for_updates_business_activity_and_counts_as_signal()
    {
        var r = ActiveRequest();
        var planTime = Now.AddHours(3);

        r.SetPlannedFor(Today.AddDays(-1), ActorId, "Jane", planTime);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(planTime, result.LatestMeaningfulActivityAtUtc);
    }

    [Fact]
    public void Clear_planned_for_updates_business_activity_and_counts_as_signal()
    {
        var r = ActiveRequest();
        var setTime = Now.AddHours(1);
        var clearTime = Now.AddHours(7);

        r.SetPlannedFor(Today.AddDays(-1), ActorId, "Jane", setTime);
        r.ClearPlannedFor(ActorId, "Jane", clearTime);

        var result = r.GetNeedsStatusCheckInputs(Today);

        Assert.True(result.IsEligible);
        Assert.Equal(clearTime, result.LatestMeaningfulActivityAtUtc);
    }
}
