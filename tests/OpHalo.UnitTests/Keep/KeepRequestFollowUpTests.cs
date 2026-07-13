using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestFollowUpTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly DateTime Now = new(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateOnly FutureDate = new(2026, 7, 1);

    static KeepRequest ActiveRequest(KeepRequestStatus status = KeepRequestStatus.Received) =>
        status switch
        {
            KeepRequestStatus.Resolved => ResolvedRequest(),
            KeepRequestStatus.Closed => ClosedRequest(),
            KeepRequestStatus.Cancelled => CancelledRequest(),
            _ => KeepRequest.CreateByBusiness(
                AccountId, CustomerId,
                "Jane Smith", "0412345678", null,
                "Burst pipe", "PQRS0001", "tok_abc", Now, KeepRequestSource.Phone)
        };

    static KeepRequest ResolvedRequest()
    {
        var r = KeepRequest.CreateByBusiness(AccountId, CustomerId, "Jane", "0412345678", null,
            "desc", "PQRS0002", "tok_res", Now, KeepRequestSource.Phone);
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, "Jane", Now);
        return r;
    }

    static KeepRequest ClosedRequest()
    {
        var r = ResolvedRequest();
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, "Jane", Now);
        return r;
    }

    static KeepRequest CancelledRequest()
    {
        var r = KeepRequest.CreateByBusiness(AccountId, CustomerId, "Jane", "0412345678", null,
            "desc", "PQRS0003", "tok_can", Now, KeepRequestSource.Phone);
        r.ChangeStatus(KeepRequestStatus.Cancelled, "Cancelling.", ActorId, "Jane", Now);
        return r;
    }

    // --- SetFollowUpOn ---

    [Fact]
    public void SetFollowUpOn_sets_fields_and_emits_event()
    {
        var r = ActiveRequest();

        var result = r.SetFollowUpOn(FutureDate, FollowUpReason.Parts, null, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(FutureDate, r.FollowUpOnDate);
        Assert.Equal(FollowUpReason.Parts, r.FollowUpReason);
        Assert.Null(r.FollowUpNote);
        Assert.Equal(Now, r.LastBusinessActivityAt);

        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.FollowUpOnChanged, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.Internal, ev.Visibility);
        Assert.Equal(FutureDate, ev.FollowUpOnDate);
        Assert.Equal(FollowUpReason.Parts, ev.FollowUpOnReason);
        Assert.Null(ev.Content);
    }

    [Fact]
    public void SetFollowUpOn_null_reason_succeeds_and_stores_null()
    {
        var r = ActiveRequest();

        var result = r.SetFollowUpOn(FutureDate, null, null, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(FutureDate, r.FollowUpOnDate);
        Assert.Null(r.FollowUpReason);
        Assert.Null(r.FollowUpNote);
        Assert.Null(result.Value.FollowUpOnReason);
    }

    [Fact]
    public void SetFollowUpOn_other_reason_with_note_succeeds()
    {
        var r = ActiveRequest();

        var result = r.SetFollowUpOn(FutureDate, FollowUpReason.Other, "Customer requested callback", ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(FollowUpReason.Other, r.FollowUpReason);
        Assert.Equal("Customer requested callback", r.FollowUpNote);
        Assert.Equal("Customer requested callback", result.Value.Content);
    }

    [Fact]
    public void SetFollowUpOn_other_reason_without_note_returns_failure()
    {
        var r = ActiveRequest();

        var result = r.SetFollowUpOn(FutureDate, FollowUpReason.Other, null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnNoteRequired.Code, result.Error.Code);
    }

    [Fact]
    public void SetFollowUpOn_note_too_long_returns_failure()
    {
        var r = ActiveRequest();
        var longNote = new string('x', 501);

        var result = r.SetFollowUpOn(FutureDate, FollowUpReason.Parts, longNote, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnNoteTooLong.Code, result.Error.Code);
    }

    [Fact]
    public void SetFollowUpOn_note_exactly_500_chars_succeeds()
    {
        var r = ActiveRequest();
        var note = new string('x', 500);

        var result = r.SetFollowUpOn(FutureDate, FollowUpReason.Parts, note, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Resolved)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public void SetFollowUpOn_on_inactive_status_returns_failure(KeepRequestStatus status)
    {
        var r = ActiveRequest(status);

        var result = r.SetFollowUpOn(FutureDate, FollowUpReason.Parts, null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnRequiresActiveRequest.Code, result.Error.Code);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Received)]
    [InlineData(KeepRequestStatus.InProgress)]
    [InlineData(KeepRequestStatus.PendingCustomer)]
    public void SetFollowUpOn_on_active_statuses_succeeds(KeepRequestStatus status)
    {
        var r = KeepRequest.CreateByBusiness(AccountId, CustomerId, "Jane", "0412345678", null,
            "desc", "PQRS0004", "tok_st", Now, KeepRequestSource.Phone);
        if (status != KeepRequestStatus.Received)
            r.ChangeStatus(status, status == KeepRequestStatus.PendingCustomer ? "msg" : null, ActorId, "Jane", Now);

        var result = r.SetFollowUpOn(FutureDate, FollowUpReason.Weather, null, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void SetFollowUpOn_change_updates_all_fields()
    {
        var r = ActiveRequest();
        r.SetFollowUpOn(FutureDate, FollowUpReason.Parts, null, ActorId, "Jane", Now);

        var newDate = new DateOnly(2026, 8, 1);
        var result = r.SetFollowUpOn(newDate, FollowUpReason.Weather, null, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(newDate, r.FollowUpOnDate);
        Assert.Equal(FollowUpReason.Weather, r.FollowUpReason);
        Assert.Null(r.FollowUpNote);
    }

    // --- ClearFollowUpOn ---

    [Fact]
    public void ClearFollowUpOn_clears_fields_and_emits_event()
    {
        var r = ActiveRequest();
        r.SetFollowUpOn(FutureDate, FollowUpReason.Parts, null, ActorId, "Jane", Now);

        var result = r.ClearFollowUpOn(ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Null(r.FollowUpOnDate);
        Assert.Null(r.FollowUpReason);
        Assert.Null(r.FollowUpNote);
        Assert.Equal(Now, r.LastBusinessActivityAt);

        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.FollowUpOnChanged, ev.EventType);
        Assert.Null(ev.FollowUpOnDate);
        Assert.Null(ev.FollowUpOnReason);
        Assert.Null(ev.Content);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Resolved)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public void ClearFollowUpOn_on_inactive_status_returns_failure(KeepRequestStatus status)
    {
        var r = ActiveRequest(status);

        var result = r.ClearFollowUpOn(ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnRequiresActiveRequest.Code, result.Error.Code);
    }

    // --- SetPlannedFor ---

    [Fact]
    public void SetPlannedFor_sets_field_and_emits_event()
    {
        var r = ActiveRequest();

        var result = r.SetPlannedFor(FutureDate, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(FutureDate, r.PlannedForDate);
        Assert.Equal(Now, r.LastBusinessActivityAt);

        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.PlannedForChanged, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.Internal, ev.Visibility);
        Assert.Equal(FutureDate, ev.PlannedForDate);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Resolved)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public void SetPlannedFor_on_inactive_status_returns_failure(KeepRequestStatus status)
    {
        var r = ActiveRequest(status);

        var result = r.SetPlannedFor(FutureDate, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.PlannedForRequiresActiveRequest.Code, result.Error.Code);
    }

    // --- ClearPlannedFor ---

    [Fact]
    public void ClearPlannedFor_clears_field_and_emits_event()
    {
        var r = ActiveRequest();
        r.SetPlannedFor(FutureDate, ActorId, "Jane", Now);

        var result = r.ClearPlannedFor(ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Null(r.PlannedForDate);
        Assert.Equal(Now, r.LastBusinessActivityAt);

        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.PlannedForChanged, ev.EventType);
        Assert.Null(ev.PlannedForDate);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Resolved)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public void ClearPlannedFor_on_inactive_status_returns_failure(KeepRequestStatus status)
    {
        var r = ActiveRequest(status);

        var result = r.ClearPlannedFor(ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.PlannedForRequiresActiveRequest.Code, result.Error.Code);
    }

    // --- ResolveFollowUp ---

    static KeepRequest ActiveRequestWithFollowUp(DateOnly date)
    {
        var r = ActiveRequest();
        r.SetFollowUpOn(date, FollowUpReason.Parts, null, ActorId, "Jane", Now);
        return r;
    }

    [Fact]
    public void ResolveFollowUp_complete_clears_date_and_emits_event()
    {
        var r = ActiveRequestWithFollowUp(FutureDate);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Complete, FollowUpCompletionReason.CustomerContacted,
            note: null, newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Null(r.FollowUpOnDate);
        Assert.Null(r.FollowUpReason);
        Assert.Null(r.FollowUpNote);
        Assert.Equal(Now, r.LastBusinessActivityAt);
        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.FollowUpResolved, ev.EventType);
        Assert.Equal(FollowUpResolutionOutcome.Complete, ev.FollowUpResolutionOutcome);
        Assert.Equal(FollowUpCompletionReason.CustomerContacted, ev.FollowUpCompletionReason);
    }

    [Fact]
    public void ResolveFollowUp_move_sets_new_date_and_emits_event()
    {
        var r = ActiveRequestWithFollowUp(FutureDate);
        var newDate = new DateOnly(2026, 8, 1);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Move, completionReason: null,
            note: "Parts delayed", newDate: newDate, newFollowUpReason: FollowUpReason.Parts,
            ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(newDate, r.FollowUpOnDate);
        Assert.Equal(FollowUpReason.Parts, r.FollowUpReason);
        Assert.Equal("Parts delayed", r.FollowUpNote);
        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.FollowUpResolved, ev.EventType);
        Assert.Equal(FollowUpResolutionOutcome.Move, ev.FollowUpResolutionOutcome);
        Assert.Null(ev.FollowUpCompletionReason);
    }

    [Fact]
    public void ResolveFollowUp_keep_active_leaves_date_and_emits_event()
    {
        var r = ActiveRequestWithFollowUp(FutureDate);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.KeepActive, FollowUpCompletionReason.WorkCompleted,
            note: null, newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(FutureDate, r.FollowUpOnDate);
        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.FollowUpResolved, ev.EventType);
        Assert.Equal(FollowUpResolutionOutcome.KeepActive, ev.FollowUpResolutionOutcome);
        Assert.Equal(FollowUpCompletionReason.WorkCompleted, ev.FollowUpCompletionReason);
    }

    [Fact]
    public void ResolveFollowUp_complete_without_completion_reason_returns_failure()
    {
        var r = ActiveRequestWithFollowUp(FutureDate);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Complete, completionReason: null,
            note: null, newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnCompletionReasonRequired.Code, result.Error.Code);
    }

    [Fact]
    public void ResolveFollowUp_keep_active_without_completion_reason_returns_failure()
    {
        var r = ActiveRequestWithFollowUp(FutureDate);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.KeepActive, completionReason: null,
            note: null, newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnCompletionReasonRequired.Code, result.Error.Code);
    }

    [Fact]
    public void ResolveFollowUp_move_without_new_date_returns_failure()
    {
        var r = ActiveRequestWithFollowUp(FutureDate);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Move, completionReason: null,
            note: null, newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnMoveRequiresDate.Code, result.Error.Code);
    }

    [Fact]
    public void ResolveFollowUp_when_no_follow_up_set_returns_failure()
    {
        var r = ActiveRequest();

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Complete, FollowUpCompletionReason.NoLongerNeeded,
            note: null, newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnNotSet.Code, result.Error.Code);
    }

    [Fact]
    public void ResolveFollowUp_note_too_long_returns_failure()
    {
        var r = ActiveRequestWithFollowUp(FutureDate);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Complete, FollowUpCompletionReason.Other,
            note: new string('x', 501), newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnNoteTooLong.Code, result.Error.Code);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Resolved)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public void ResolveFollowUp_on_inactive_status_returns_failure(KeepRequestStatus status)
    {
        var r = ActiveRequest(status);

        var result = r.ResolveFollowUp(
            FollowUpResolutionOutcome.Complete, FollowUpCompletionReason.CustomerContacted,
            note: null, newDate: null, newFollowUpReason: null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.FollowUpOnRequiresActiveRequest.Code, result.Error.Code);
    }
}
