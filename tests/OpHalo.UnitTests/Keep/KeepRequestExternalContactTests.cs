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
        KeepRequest.Create(AccountId, CustomerId,
            "John Customer", "0412345678", null,
            "Fix the hot water system", "ABCD1234", "tok_abc", Now,
            firstResponseTargetMinutes, KeepRequestOrigin.Customer);

    static KeepRequest NewBusinessRequest() =>
        KeepRequest.Create(AccountId, CustomerId,
            "John Customer", "0412345678", null,
            "Fix the hot water system", "ABCD1234", "tok_abc", Now,
            60, KeepRequestOrigin.Business);

    // Raise standard business-waiting attention on the request.
    static void RaiseBusinessWaiting(KeepRequest request, DateTime? since = null)
    {
        var t = since ?? Now;
        request.AddCustomerMessage(
            MessageIntent.GeneralMessage, "Still waiting", 60, StandardMinutes, 60, t);
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

    [Theory]
    [InlineData(ExternalContactOutcome.SpokeWithCustomer)]
    [InlineData(ExternalContactOutcome.LeftVoicemail)]
    public void Outbound_spoke_voicemail_sets_first_response_on_customer_origin(ExternalContactOutcome outcome)
    {
        var request = NewCustomerRequest();

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, outcome, requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, request.FirstRespondedAtUtc);
        Assert.Equal(ActorId, request.FirstResponderAccountUserId);
        Assert.Equal(result.Value!.Id, request.FirstResponseEventId);
        Assert.True(result.Value.ExternalContactSetFirstResponse);
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
        // Record first response via a business update.
        request.AddBusinessUpdate("Initial update", ActorId, ActorName, firstEventTime);
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

    [Theory]
    [InlineData(ExternalContactOutcome.SpokeWithCustomer)]
    [InlineData(ExternalContactOutcome.LeftVoicemail)]
    public void Outbound_spoke_voicemail_no_follow_up_clears_business_waiting(ExternalContactOutcome outcome)
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, outcome, requiresBusinessFollowUp: false, summary: null,
            ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal("external_contact_no_follow_up", request.AttentionClearReason);
        Assert.True(result.Value!.ExternalContactClearedAttention);
    }

    [Theory]
    [InlineData(ExternalContactOutcome.SpokeWithCustomer)]
    [InlineData(ExternalContactOutcome.LeftVoicemail)]
    public void Outbound_spoke_voicemail_follow_up_needed_preserves_attention(ExternalContactOutcome outcome)
    {
        var request = NewCustomerRequest();
        RaiseBusinessWaiting(request);

        var result = request.LogOutboundExternalContact(
            CommunicationChannel.Phone, outcome, requiresBusinessFollowUp: true, summary: null,
            ActorId, ActorName, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(AttentionLevel.None, request.AttentionLevel);
        Assert.False(result.Value!.ExternalContactClearedAttention);
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
}
