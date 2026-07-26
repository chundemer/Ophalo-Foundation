using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestExternalContactTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    const string ActorName = "Jane Operator";
    static readonly DateTime Now = new(2026, 6, 17, 10, 0, 0, DateTimeKind.Utc);
    const int StandardMinutes = 240;

    static KeepRequest NewCustomerRequest(int firstResponseTargetMinutes = 60) =>
        KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId,
            "John Customer", "0412345678", null,
            "Fix the hot water system", "ABCD1234", "tok_abc", Now,
            firstResponseTargetMinutes);

    static KeepRequest NewBusinessRequest() =>
        KeepRequest.CreateByBusiness(AccountId, CustomerId,
            "John Customer", "0412345678", null,
            "Fix the hot water system", "ABCD1234", "tok_abc", Now, KeepRequestSource.Phone);

    // Raise standard business-waiting attention on the request.
    static void RaiseBusinessWaiting(KeepRequest request, DateTime? since = null)
    {
        var t = since ?? Now;
        request.AddCustomerMessage(
            MessageIntent.GeneralMessage, "Still waiting", 60, StandardMinutes, 60, t);
    }

    // Arbitrary stand-in for the caller-computed next-business-day value (ADR-451); the
    // timezone/weekend-skip computation itself lives in LogExternalContactService, not the domain.
    static readonly DateTime NextBusinessDay = new(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc);

    // Raise CallRequested business-waiting attention on the request (ADR-451 CallRequested row).
    static void RaiseCallRequested(KeepRequest request, DateTime? since = null)
    {
        var t = since ?? Now;
        request.AddCustomerMessage(
            MessageIntent.CallRequested, "Please call me back", 60, StandardMinutes, 60, t);
    }

    // -------------------------------------------------------------------
    // LogOutboundExternalContact — guard failures
    // -------------------------------------------------------------------

    [Fact]
    public void Outbound_blocked_on_closed_request()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now);
        request.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error!.Code);
    }

    [Fact]
    public void Outbound_blocked_on_cancelled_request()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Cancelled, "Cancelled by customer", ActorId, ActorName, Now);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.InPerson)]
    [InlineData(CommunicationChannel.Other)]
    [InlineData(CommunicationChannel.InApp)]
    public void Outbound_rejects_invalid_channel(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            channel, outcome: null, requiresBusinessFollowUp: null, summary: null, ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactInvalidOutboundChannel.Code, result.Error!.Code);
    }

    [Fact]
    public void Outbound_phone_requires_outcome()
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, outcome: null, requiresBusinessFollowUp: null, summary: null,
            ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactOutcomeRequired.Code, result.Error!.Code);
    }

    [Fact]
    public void Outbound_rejects_undefined_outcome_before_pattern_match()
    {
        var request = NewCustomerRequest();
        var undefinedOutcome = (ExternalContactOutcome)99;

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, undefinedOutcome, requiresBusinessFollowUp: null,
            summary: null, ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactOutcomeNotAllowed.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExternalContactOutcome.SpokeWithCustomer)]
    [InlineData(ExternalContactOutcome.LeftVoicemail)]
    public void Outbound_phone_spoke_voicemail_requires_follow_up(ExternalContactOutcome outcome)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, outcome, requiresBusinessFollowUp: null, summary: null,
            ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactFollowUpRequired.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExternalContactOutcome.NoAnswer)]
    [InlineData(ExternalContactOutcome.WrongNumber)]
    public void Outbound_phone_no_answer_wrong_number_rejects_follow_up(ExternalContactOutcome outcome)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, outcome, requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactFollowUpNotAllowed.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void Outbound_sms_email_rejects_outcome(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            channel, ExternalContactOutcome.SpokeWithCustomer, requiresBusinessFollowUp: false,
            summary: "Sent update", ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactOutcomeNotAllowed.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void Outbound_sms_email_requires_follow_up(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            channel, outcome: null, requiresBusinessFollowUp: null, summary: "Sent update",
            ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactFollowUpRequired.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void Outbound_sms_email_requires_summary(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            channel, outcome: null, requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactSummaryRequired.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void Outbound_sms_email_rejects_summary_too_long(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            channel, outcome: null, requiresBusinessFollowUp: false, summary: new string('x', 4001),
            ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactSummaryTooLong.Code, result.Error!.Code);
    }

    [Fact]
    public void Outbound_phone_rejects_summary_too_long()
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: new string('x', 4001),
            ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactSummaryTooLong.Code, result.Error!.Code);
    }

    // -------------------------------------------------------------------
    // LogOutboundExternalContact — first response (ADR-198/213)
    // -------------------------------------------------------------------

    [Fact]
    public void Outbound_spoke_sets_first_response_on_customer_origin()
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, request.FirstRespondedAtUtc);
        Assert.Equal(ActorId, request.FirstResponderAccountUserId);
        Assert.Equal(result.Value!.Id, request.FirstResponseEventId);
        Assert.True(result.Value.ExternalContactSetFirstResponse);
    }

    [Fact]
    public void Outbound_voicemail_never_sets_first_response_on_customer_origin()
    {
        // ADR-451 supersedes ADR-198/213: a detailed voicemail never counts as first response.
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.LeftVoicemail,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now,
            nextBusinessDayAttentionUtc: NextBusinessDay);

        Assert.True(result.IsSuccess);
        Assert.Null(request.FirstRespondedAtUtc);
        Assert.Null(request.FirstResponderAccountUserId);
        Assert.Null(request.FirstResponseEventId);
        Assert.False(result.Value!.ExternalContactSetFirstResponse);
    }

    [Fact]
    public void Outbound_voicemail_without_nextBusinessDay_throws()
    {
        var request = NewCustomerRequest();

        Assert.Throws<ArgumentException>(() => request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.LeftVoicemail,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now));
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void Outbound_sms_email_sets_first_response_on_customer_origin(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            channel, outcome: null, requiresBusinessFollowUp: false, summary: "Sent an update",
            ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, request.FirstRespondedAtUtc);
        Assert.True(result.Value!.ExternalContactSetFirstResponse);
    }

    [Theory]
    [InlineData(ExternalContactOutcome.NoAnswer)]
    [InlineData(ExternalContactOutcome.WrongNumber)]
    public void Outbound_no_answer_wrong_number_does_not_set_first_response(ExternalContactOutcome outcome)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, outcome, requiresBusinessFollowUp: null, summary: null,
            ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(request.FirstRespondedAtUtc);
        Assert.False(result.Value!.ExternalContactSetFirstResponse);
    }

    [Fact]
    public void Outbound_does_not_overwrite_existing_first_response()
    {
        var request = NewCustomerRequest();
        var firstEventTime = Now.AddMinutes(-30);
        // Record first response via a prior confirmed outbound contact (ADR-198/213); a page-only
        // business update no longer sets first response (ADR-451).
        request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: true, summary: null, ActorId, ActorName, firstEventTime);
        var originalFirstRespondedAt = request.FirstRespondedAtUtc;
        var originalFirstResponseEventId = request.FirstResponseEventId;

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalFirstRespondedAt, request.FirstRespondedAtUtc);
        Assert.Equal(originalFirstResponseEventId, request.FirstResponseEventId);
        Assert.False(result.Value!.ExternalContactSetFirstResponse);
    }

    [Fact]
    public void Outbound_does_not_set_first_response_on_business_origin()
    {
        var request = NewBusinessRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(request.FirstRespondedAtUtc);
        Assert.False(result.Value!.ExternalContactSetFirstResponse);
    }

    // -------------------------------------------------------------------
    // LogOutboundExternalContact — attention clearing (ADR-169/214)
    // -------------------------------------------------------------------

    [Fact]
    public void Outbound_spoke_no_follow_up_clears_business_waiting()
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal("external_contact_no_follow_up", request.AttentionClearReason);
        Assert.True(result.Value!.ExternalContactClearedAttention);
    }

    [Fact]
    public void Outbound_spoke_follow_up_needed_preserves_attention()
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: true, summary: null, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);
        Assert.False(result.Value!.ExternalContactClearedAttention);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Outbound_voicemail_never_clears_business_waiting_regardless_of_follow_up(
        bool requiresBusinessFollowUp)
    {
        // ADR-451 supersedes ADR-169/214: a detailed voicemail never clears attention, even when
        // requiresBusinessFollowUp = false. It preserves attention and creates a follow-up promise.
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);
        var attentionReasonBefore = request.AttentionReason;

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.LeftVoicemail,
            requiresBusinessFollowUp, summary: null, ActorId, ActorName, Now.AddMinutes(5),
            nextBusinessDayAttentionUtc: NextBusinessDay);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(attentionReasonBefore, request.AttentionReason);
        Assert.Null(request.AttentionClearReason);
        Assert.False(result.Value!.ExternalContactClearedAttention);
        Assert.Equal(NextBusinessDay, request.NextAttentionAtUtc);
    }

    [Fact]
    public void Outbound_voicemail_does_not_pull_a_later_existing_commitment_earlier()
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);
        var laterCommitment = NextBusinessDay.AddDays(5);
        typeof(KeepRequest).GetProperty(nameof(KeepRequest.NextAttentionAtUtc))!
            .SetValue(request, laterCommitment);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.LeftVoicemail,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now.AddMinutes(5),
            nextBusinessDayAttentionUtc: NextBusinessDay);

        Assert.True(result.IsSuccess);
        Assert.Equal(laterCommitment, request.NextAttentionAtUtc);
    }

    [Fact]
    public void Outbound_voicemail_without_active_business_waiting_attention_does_not_set_next_attention()
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.LeftVoicemail,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now.AddMinutes(5),
            nextBusinessDayAttentionUtc: NextBusinessDay);

        Assert.True(result.IsSuccess);
        Assert.Null(request.NextAttentionAtUtc);
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void Outbound_sms_email_no_follow_up_clears_business_waiting(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);

        var result = request.LogOutboundExternalContact(
            channel, outcome: null, requiresBusinessFollowUp: false, summary: "Sent update",
            ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal("external_contact_no_follow_up", request.AttentionClearReason);
        Assert.True(result.Value!.ExternalContactClearedAttention);
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void Outbound_sms_email_never_satisfies_call_requested(CommunicationChannel channel)
    {
        // ADR-451: a customer-requested call-back is never satisfied by text/email, even when
        // requiresBusinessFollowUp = false — only a completed live call clears it.
        var request = NewCustomerRequest();
        RaiseCallRequested(request);

        var result = request.LogOutboundExternalContact(
            channel, outcome: null, requiresBusinessFollowUp: false, summary: "Sent update",
            ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal(AttentionReason.CallRequested, request.AttentionReason);
        Assert.Null(request.AttentionClearReason);
        Assert.False(result.Value!.ExternalContactClearedAttention);
    }

    [Fact]
    public void Outbound_phone_spoke_still_clears_call_requested()
    {
        var request = NewCustomerRequest();
        RaiseCallRequested(request);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.True(result.Value!.ExternalContactClearedAttention);
    }

    [Fact]
    public void Outbound_no_answer_does_not_clear_attention()
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.NoAnswer,
            requiresBusinessFollowUp: null, summary: null, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);
        Assert.Null(request.AttentionClearReason);
        Assert.False(result.Value!.ExternalContactClearedAttention);
    }

    [Fact]
    public void Outbound_clears_first_response_overdue_business_waiting_attention()
    {
        // First-response overdue: past due + business waiting. Spoke + no follow-up should clear.
        var request = NewCustomerRequest(firstResponseTargetMinutes: 60);
        // Manually raise business-waiting attention (simulates first-response-overdue path).
        RaiseBusinessWaiting(request, since: Now.AddHours(-2));

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal("external_contact_no_follow_up", request.AttentionClearReason);
        Assert.True(result.Value!.ExternalContactSetFirstResponse);
        Assert.True(result.Value!.ExternalContactClearedAttention);
    }

    // -------------------------------------------------------------------
    // LogOutboundExternalContact — activity timestamp and event shape
    // -------------------------------------------------------------------

    [Fact]
    public void Outbound_updates_last_business_activity()
    {
        var request = NewCustomerRequest();
        var before = request.LastBusinessActivityAt;
        var contactTime = Now.AddHours(1);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.NoAnswer,
            requiresBusinessFollowUp: null, summary: null, ActorId, ActorName, contactTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(contactTime, request.LastBusinessActivityAt);
        Assert.NotEqual(before, request.LastBusinessActivityAt);
    }

    [Fact]
    public void Outbound_event_has_correct_fields()
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: "  Confirmed arrival window  ",
            ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        var ev = result.Value!;
        Assert.Equal(KeepRequestEventType.ExternalContactLogged, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.Internal, ev.Visibility);
        Assert.Equal(ExternalContactDirection.Outbound, ev.ExternalContactDirection);
        Assert.Equal(CommunicationChannel.Phone, ev.CommunicationChannel);
        Assert.Equal(ExternalContactOutcome.SpokeWithCustomer, ev.ExternalContactOutcome);
        Assert.Equal(false, ev.ExternalContactRequiresFollowUp);
        Assert.Equal("Confirmed arrival window", ev.Content);
        Assert.Equal(ActorId, ev.ActorAccountUserId);
        Assert.Equal(ActorName, ev.ActorDisplayName);
        Assert.Equal(Now, ev.OccurredAtUtc);
    }

    [Fact]
    public void Outbound_no_answer_event_has_null_outcome_follow_up_metadata()
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.NoAnswer,
            requiresBusinessFollowUp: null, summary: null, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ExternalContactOutcome.NoAnswer, result.Value!.ExternalContactOutcome);
        Assert.Null(result.Value!.ExternalContactRequiresFollowUp);
    }

    // -------------------------------------------------------------------
    // LogInboundExternalContact — guard failures
    // -------------------------------------------------------------------

    [Fact]
    public void Inbound_blocked_on_closed_request()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now);
        request.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now);

        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: true, summary: "Customer called",
            ActorId, ActorName, StandardMinutes, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error!.Code);
    }

    [Fact]
    public void Inbound_rejects_InApp_channel()
    {
        var request = NewCustomerRequest();

        var result = request.LogInboundExternalContact(
            CommunicationChannel.InApp, requiresBusinessFollowUp: true, summary: "Customer called",
            ActorId, ActorName, StandardMinutes, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactInvalidInboundChannel.Code, result.Error!.Code);
    }

    [Fact]
    public void Inbound_requires_summary()
    {
        var request = NewCustomerRequest();

        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: true, summary: "  ",
            ActorId, ActorName, StandardMinutes, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactSummaryRequired.Code, result.Error!.Code);
    }

    [Fact]
    public void Inbound_rejects_summary_too_long()
    {
        var request = NewCustomerRequest();

        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: true, summary: new string('x', 4001),
            ActorId, ActorName, StandardMinutes, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ExternalContactSummaryTooLong.Code, result.Error!.Code);
    }

    // -------------------------------------------------------------------
    // LogInboundExternalContact — first response (ADR-198)
    // -------------------------------------------------------------------

    [Fact]
    public void Inbound_does_not_count_first_response()
    {
        var request = NewCustomerRequest();

        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: true, summary: "Customer called",
            ActorId, ActorName, StandardMinutes, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(request.FirstRespondedAtUtc);
        Assert.False(result.Value!.ExternalContactSetFirstResponse);
    }

    // -------------------------------------------------------------------
    // LogInboundExternalContact — attention effects (ADR-204)
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(CommunicationChannel.Phone)]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    [InlineData(CommunicationChannel.InPerson)]
    [InlineData(CommunicationChannel.Other)]
    public void Inbound_follow_up_from_none_raises_fresh_attention(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);

        var result = request.LogInboundExternalContact(
            channel, requiresBusinessFollowUp: true, summary: "Customer provided gate code",
            ActorId, ActorName, StandardMinutes, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(AttentionReason.CustomerMessage, request.AttentionReason);
        Assert.Equal(PriorityBand.Standard, request.PriorityBand);
        Assert.Equal(Now, request.AttentionSinceUtc);
        Assert.Equal(Now.AddMinutes(StandardMinutes), request.NextAttentionAtUtc);
    }

    [Fact]
    public void Inbound_follow_up_from_waiting_on_customer_flips_to_business_waiting()
    {
        // WaitingDirection.Customer is not set by any current domain method (no write exists yet).
        // Mirror the same branch in AddCustomerMessage: force the state via reflection to test the flip logic.
        var request = NewCustomerRequest();
        typeof(KeepRequest).GetProperty("AttentionLevel")!.SetValue(request, AttentionLevel.Waiting);
        typeof(KeepRequest).GetProperty("WaitingDirection")!.SetValue(request, WaitingDirection.Customer);

        var contactTime = Now.AddHours(2);
        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: true, summary: "Customer called back",
            ActorId, ActorName, StandardMinutes, contactTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(contactTime, request.AttentionSinceUtc);
        Assert.Equal(contactTime.AddMinutes(StandardMinutes), request.NextAttentionAtUtc);
    }

    [Fact]
    public void Inbound_follow_up_already_business_waiting_preserves_oldest_attention_since()
    {
        var request = NewCustomerRequest();
        var originalAttentionTime = Now.AddHours(-3);
        RaiseBusinessWaiting(request, since: originalAttentionTime);
        var originalSince = request.AttentionSinceUtc;

        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: true, summary: "Customer called again",
            ActorId, ActorName, StandardMinutes, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalSince, request.AttentionSinceUtc);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
    }

    [Fact]
    public void Inbound_no_follow_up_does_not_raise_attention()
    {
        var request = NewCustomerRequest();
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);

        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: false, summary: "Customer gave gate code",
            ActorId, ActorName, StandardMinutes, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
    }

    [Fact]
    public void Inbound_invalid_attention_state_throws()
    {
        // This state (AttentionLevel != None, WaitingDirection = None) is an invalid domain state.
        // We cannot reach it through normal domain methods, so this test documents the guard exists.
        // Tested via reflection to force the impossible state.
        var request = NewCustomerRequest();

        // Force invalid internal state via reflection.
        typeof(KeepRequest).GetProperty("AttentionLevel")!
            .SetValue(request, AttentionLevel.Waiting);
        typeof(KeepRequest).GetProperty("WaitingDirection")!
            .SetValue(request, WaitingDirection.None);

        Assert.Throws<InvalidOperationException>(() =>
            request.LogInboundExternalContact(
                CommunicationChannel.Phone, requiresBusinessFollowUp: true,
                summary: "Customer called", ActorId, ActorName, StandardMinutes, Now));
    }

    // -------------------------------------------------------------------
    // LogInboundExternalContact — activity timestamp and event shape
    // -------------------------------------------------------------------

    [Fact]
    public void Inbound_updates_last_customer_activity()
    {
        var request = NewCustomerRequest();
        var contactTime = Now.AddHours(1);

        var result = request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: false, summary: "Gate code 4421",
            ActorId, ActorName, StandardMinutes, contactTime);

        Assert.True(result.IsSuccess);
        Assert.Equal(contactTime, request.LastCustomerActivityAt);
    }

    [Fact]
    public void Inbound_does_not_update_last_business_activity()
    {
        var request = NewCustomerRequest();
        var businessActivity = request.LastBusinessActivityAt;

        request.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: false, summary: "Info provided",
            ActorId, ActorName, StandardMinutes, Now.AddHours(1));

        Assert.Equal(businessActivity, request.LastBusinessActivityAt);
    }

    [Fact]
    public void Inbound_event_has_correct_fields()
    {
        var request = NewCustomerRequest();

        var result = request.LogInboundExternalContact(
            CommunicationChannel.InPerson, requiresBusinessFollowUp: true,
            summary: "  Customer spoke in person  ",
            ActorId, ActorName, StandardMinutes, Now);

        Assert.True(result.IsSuccess);
        var ev = result.Value!;
        Assert.Equal(KeepRequestEventType.ExternalContactLogged, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.Internal, ev.Visibility);
        Assert.Equal(ExternalContactDirection.Inbound, ev.ExternalContactDirection);
        Assert.Equal(CommunicationChannel.InPerson, ev.CommunicationChannel);
        Assert.Null(ev.ExternalContactOutcome);
        Assert.Equal(true, ev.ExternalContactRequiresFollowUp);
        Assert.False(ev.ExternalContactSetFirstResponse);
        Assert.False(ev.ExternalContactClearedAttention);
        Assert.Equal("Customer spoke in person", ev.Content);
    }

    // -------------------------------------------------------------------
    // LogClosedFeedbackFollowUpExternalContact — G7b
    // -------------------------------------------------------------------

    static KeepRequest NewExactReviewStateRequest()
    {
        var r = NewCustomerRequest();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now);
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now);
        r.SubmitFeedback(wasResolved: false, comment: "Not satisfied", priorityResponseTargetMinutes: 60, nowUtc: Now);
        return r;
    }

    [Fact]
    public void FollowUp_success_returns_event_and_updates_activity()
    {
        var request = NewExactReviewStateRequest();
        var before = request.LastBusinessActivityAt;

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddHours(1), request.LastBusinessActivityAt);
        Assert.NotEqual(before, request.LastBusinessActivityAt);
    }

    [Fact]
    public void FollowUp_does_not_set_first_response()
    {
        var request = NewExactReviewStateRequest();
        Assert.Null(request.FirstRespondedAtUtc);

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        var ev = result.Value;
        Assert.False(ev.ExternalContactSetFirstResponse);
        Assert.Null(request.FirstRespondedAtUtc);
        Assert.Null(request.FirstResponderAccountUserId);
    }

    [Fact]
    public void FollowUp_does_not_clear_attention()
    {
        var request = NewExactReviewStateRequest();
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(AttentionReason.UnresolvedFeedback, request.AttentionReason);

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        var ev = result.Value;
        Assert.False(ev.ExternalContactClearedAttention);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(AttentionReason.UnresolvedFeedback, request.AttentionReason);
    }

    [Fact]
    public void FollowUp_leaves_status_and_feedback_fields_unchanged()
    {
        var request = NewExactReviewStateRequest();

        request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.Equal(KeepRequestStatus.Closed, request.Status);
        Assert.False(request.FeedbackWasResolved);
        Assert.Null(request.FeedbackReviewedAtUtc);
        Assert.NotNull(request.FeedbackSubmittedAtUtc);
    }

    [Fact]
    public void FollowUp_event_has_correct_direction_and_flags()
    {
        var request = NewExactReviewStateRequest();

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Sms, outcome: null,
            requiresBusinessFollowUp: true, summary: "Left a message",
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        var ev = result.Value;
        Assert.Equal(ExternalContactDirection.Outbound, ev.ExternalContactDirection);
        Assert.Equal(CommunicationChannel.Sms, ev.CommunicationChannel);
        Assert.False(ev.ExternalContactSetFirstResponse);
        Assert.False(ev.ExternalContactClearedAttention);
        Assert.Equal("Left a message", ev.Content);
    }

    [Fact]
    public void FollowUp_blocked_on_ordinary_closed_no_feedback()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now);
        request.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now);

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error.Code);
    }

    [Fact]
    public void FollowUp_blocked_on_closed_positive_feedback()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now);
        request.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now);
        request.SubmitFeedback(wasResolved: true, comment: "Great job", priorityResponseTargetMinutes: 60, nowUtc: Now);

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error.Code);
    }

    [Fact]
    public void FollowUp_blocked_after_feedback_already_reviewed()
    {
        var request = NewExactReviewStateRequest();
        request.MarkFeedbackReviewed(null, ActorId, ActorName, Now.AddMinutes(10));

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error.Code);
    }

    [Fact]
    public void FollowUp_blocked_on_cancelled_request()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Cancelled, null, ActorId, ActorName, Now);

        var result = request.LogClosedFeedbackFollowUpExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error.Code);
    }

    // -------------------------------------------------------------------
    // PrepareUpdateNotification — durable obligation record (ADR-451, GAP-052a)
    // -------------------------------------------------------------------

    static readonly Guid OtherActorId = Guid.NewGuid();
    const string OtherActorName = "Other Operator";

    [Fact]
    public void PrepareNotification_blocked_on_terminal_request()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now);
        request.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now);

        var result = request.PrepareUpdateNotification(
            Guid.NewGuid(), CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error!.Code);
    }

    [Fact]
    public void PrepareNotification_rejects_empty_related_event_id()
    {
        var request = NewCustomerRequest();

        var result = request.PrepareUpdateNotification(
            Guid.Empty, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationRelatedEventRequired.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.Phone)]
    [InlineData(CommunicationChannel.InPerson)]
    [InlineData(CommunicationChannel.Other)]
    [InlineData(CommunicationChannel.InApp)]
    public void PrepareNotification_rejects_non_sms_email_channel(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.PrepareUpdateNotification(
            Guid.NewGuid(), channel, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationInvalidChannel.Code, result.Error!.Code);
    }

    [Fact]
    public void PrepareNotification_records_pending_obligation_without_effects()
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);
        var attentionBefore = request.AttentionLevel;
        var relatedEventId = Guid.NewGuid();

        var result = request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(relatedEventId, request.PendingNotificationRelatedEventId);
        Assert.Equal(CommunicationChannel.Sms, request.PendingNotificationChannel);
        Assert.Equal(ActorId, request.PendingNotificationPreparedByAccountUserId);
        Assert.NotNull(request.PendingNotificationPreparedAtUtc);
        // Preparation alone never applies effects.
        Assert.Equal(attentionBefore, request.AttentionLevel);
        Assert.Null(request.FirstRespondedAtUtc);
    }

    [Fact]
    public void PrepareNotification_overwrites_prior_pending_obligation()
    {
        var request = NewCustomerRequest();
        request.PrepareUpdateNotification(
            Guid.NewGuid(), CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));
        var secondEventId = Guid.NewGuid();

        var result = request.PrepareUpdateNotification(
            secondEventId, CommunicationChannel.Email, OtherActorId, OtherActorName, Now.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(secondEventId, request.PendingNotificationRelatedEventId);
        Assert.Equal(CommunicationChannel.Email, request.PendingNotificationChannel);
        Assert.Equal(OtherActorId, request.PendingNotificationPreparedByAccountUserId);
    }

    // -------------------------------------------------------------------
    // ConfirmUpdateNotification — sole notification attestation (ADR-451, GAP-052a)
    // -------------------------------------------------------------------

    [Fact]
    public void ConfirmNotification_blocked_on_terminal_request()
    {
        var request = NewCustomerRequest();
        request.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, ActorName, Now);
        request.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, ActorName, Now);

        var result = request.ConfirmUpdateNotification(
            Guid.NewGuid(), CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.TerminalState.Code, result.Error!.Code);
    }

    [Fact]
    public void ConfirmNotification_rejects_empty_related_event_id()
    {
        var request = NewCustomerRequest();

        var result = request.ConfirmUpdateNotification(
            Guid.Empty, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationRelatedEventRequired.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.Phone)]
    [InlineData(CommunicationChannel.InPerson)]
    [InlineData(CommunicationChannel.Other)]
    [InlineData(CommunicationChannel.InApp)]
    public void ConfirmNotification_rejects_non_sms_email_channel(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();

        var result = request.ConfirmUpdateNotification(
            Guid.NewGuid(), channel, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationInvalidChannel.Code, result.Error!.Code);
    }

    [Fact]
    public void ConfirmNotification_rejects_without_any_prepared_obligation()
    {
        var request = NewCustomerRequest();

        var result = request.ConfirmUpdateNotification(
            Guid.NewGuid(), CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationNotPrepared.Code, result.Error!.Code);
    }

    [Fact]
    public void ConfirmNotification_rejects_mismatched_related_event_id()
    {
        var request = NewCustomerRequest();
        request.PrepareUpdateNotification(
            Guid.NewGuid(), CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        var result = request.ConfirmUpdateNotification(
            Guid.NewGuid(), CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(2));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationNotPrepared.Code, result.Error!.Code);
    }

    [Fact]
    public void ConfirmNotification_rejects_mismatched_channel()
    {
        var request = NewCustomerRequest();
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        var result = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Email, ActorId, ActorName, Now.AddMinutes(2));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationNotPrepared.Code, result.Error!.Code);
    }

    [Fact]
    public void ConfirmNotification_rejects_different_confirming_actor()
    {
        // ADR-451: the same authenticated user who prepared the handoff confirms it.
        var request = NewCustomerRequest();
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));

        var result = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, OtherActorId, OtherActorName, Now.AddMinutes(2));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationConfirmerMismatch.Code, result.Error!.Code);
        // Rejected confirmation must not consume the pending obligation.
        Assert.Equal(relatedEventId, request.PendingNotificationRelatedEventId);
    }

    [Fact]
    public void ConfirmNotification_rejects_replay_after_prior_confirmation()
    {
        var request = NewCustomerRequest();
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(1));
        var firstConfirm = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(2));
        Assert.True(firstConfirm.IsSuccess);

        var replay = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(3));

        Assert.False(replay.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotificationNotPrepared.Code, replay.Error!.Code);
    }

    [Theory]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.Email)]
    public void ConfirmNotification_sets_first_response_and_clears_business_waiting(CommunicationChannel channel)
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(relatedEventId, channel, ActorId, ActorName, Now.AddMinutes(4));

        var result = request.ConfirmUpdateNotification(
            relatedEventId, channel, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.NotNull(request.FirstRespondedAtUtc);
        Assert.Equal(ActorId, request.FirstResponderAccountUserId);
        Assert.Equal(result.Value!.Id, request.FirstResponseEventId);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal("notification_confirmed", request.AttentionClearReason);
        Assert.Equal(channel, result.Value!.CommunicationChannel);
        Assert.Equal(relatedEventId, result.Value!.RelatedEventId);
        // Confirmed obligation is consumed.
        Assert.Null(request.PendingNotificationRelatedEventId);
        Assert.Null(request.PendingNotificationChannel);
        Assert.Null(request.PendingNotificationPreparedByAccountUserId);
    }

    [Fact]
    public void ConfirmNotification_never_satisfies_call_requested()
    {
        // ADR-451: only a completed live call satisfies a customer-requested call-back.
        var request = NewCustomerRequest();
        RaiseCallRequested(request);
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(4));

        var result = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal(AttentionReason.CallRequested, request.AttentionReason);
        Assert.Null(request.AttentionClearReason);
    }

    [Fact]
    public void ConfirmNotification_does_not_overwrite_existing_first_response()
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);
        request.LogOutboundExternalContact(
            CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer,
            requiresBusinessFollowUp: false, summary: null, ActorId, ActorName, Now.AddMinutes(1));
        var firstResponseAt = request.FirstRespondedAtUtc;
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(9));

        var result = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(10));

        Assert.True(result.IsSuccess);
        Assert.Equal(firstResponseAt, request.FirstRespondedAtUtc);
    }

    [Fact]
    public void ConfirmNotification_does_not_set_first_response_on_business_origin()
    {
        var request = NewBusinessRequest();
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(4));

        var result = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Null(request.FirstRespondedAtUtc);
    }

    [Fact]
    public void ConfirmNotification_clears_needs_share()
    {
        var request = NewBusinessRequest();
        Assert.True(request.NeedsShare);
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(4));

        var result = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.False(request.NeedsShare);
    }

    [Fact]
    public void ConfirmNotification_updates_last_business_activity()
    {
        var request = NewCustomerRequest();
        var relatedEventId = Guid.NewGuid();
        request.PrepareUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, Now.AddMinutes(4));
        var confirmAt = Now.AddMinutes(5);

        var result = request.ConfirmUpdateNotification(
            relatedEventId, CommunicationChannel.Sms, ActorId, ActorName, confirmAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(confirmAt, request.LastBusinessActivityAt);
    }
}
