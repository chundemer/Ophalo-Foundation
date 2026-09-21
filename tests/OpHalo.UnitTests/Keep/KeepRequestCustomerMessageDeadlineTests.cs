using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// ADR-505 slice 9b: the deadline-delegate overload of <c>AddCustomerMessage</c> — fresh/flip stamping,
/// the earlier-of escalation guard, failure handling, and that the legacy minutes overload is guarded too.
/// </summary>
public class KeepRequestCustomerMessageDeadlineTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly DateTime T0 = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    static KeepRequest MakeRequest() =>
        KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Jane", "04123", null, "desc", "REF1", "tok", T0, 60);

    static void SetProp(KeepRequest request, string name, object? value) =>
        typeof(KeepRequest).GetProperty(name)!.SetValue(request, value);

    /// <summary>Records every call and returns a fixed deadline.</summary>
    sealed class Deadline(DateTime? result)
    {
        public List<PriorityBand> Calls { get; } = [];

        public DateTime? For(PriorityBand band)
        {
            Calls.Add(band);
            return result;
        }
    }

    /// <summary>A request already business-waiting on a Standard message raised at T0 (deadline T0+240).</summary>
    static KeepRequest StandardWaiting()
    {
        var request = MakeRequest();
        Assert.True(request.AddCustomerMessage(MessageIntent.GeneralMessage, "hi", 60, 240, 60, T0).IsSuccess);
        Assert.Equal(T0.AddMinutes(240), request.NextAttentionAtUtc);
        return request;
    }

    // ── Fresh and flipped obligations ─────────────────────────────────────────

    [Theory]
    [InlineData(MessageIntent.GeneralMessage, PriorityBand.Standard)]
    [InlineData(MessageIntent.Question, PriorityBand.Standard)]
    [InlineData(MessageIntent.Complaint, PriorityBand.Priority)]
    [InlineData(MessageIntent.CallRequested, PriorityBand.Priority)]
    public void Fresh_obligation_stamps_the_deadline_for_its_band_with_one_call(MessageIntent intent, PriorityBand band)
    {
        var request = MakeRequest();
        var deadline = new Deadline(T0.AddHours(9));

        var result = request.AddCustomerMessage(intent, "msg", deadline.For, T0);

        Assert.True(result.IsSuccess);
        Assert.Equal([band], deadline.Calls);
        Assert.Equal(T0.AddHours(9), request.NextAttentionAtUtc);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(band, request.PriorityBand);
    }

    [Fact]
    public void Fresh_obligation_with_a_failed_calculation_is_waiting_with_no_deadline()
    {
        var request = MakeRequest();
        var deadline = new Deadline(null);

        Assert.True(request.AddCustomerMessage(MessageIntent.GeneralMessage, "msg", deadline.For, T0).IsSuccess);

        Assert.Single(deadline.Calls);
        Assert.Null(request.NextAttentionAtUtc); // no duration-derived fallback
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Flipped_obligation_replaces_the_deadline_with_the_new_result_even_when_null(bool calculationSucceeds)
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.Customer);
        SetProp(request, nameof(KeepRequest.NextAttentionAtUtc), T0.AddDays(2)); // e.g. an old follow-up promise
        var deadline = new Deadline(calculationSucceeds ? T0.AddMinutes(90) : null);

        Assert.True(request.AddCustomerMessage(MessageIntent.GeneralMessage, "msg", deadline.For, T0).IsSuccess);

        Assert.Single(deadline.Calls);
        Assert.Equal(calculationSucceeds ? T0.AddMinutes(90) : null, request.NextAttentionAtUtc);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(T0, request.AttentionSinceUtc);
    }

    // ── Repeats never consult the delegate ────────────────────────────────────

    [Fact]
    public void Same_priority_repeat_preserves_the_deadline_and_does_not_call_the_delegate()
    {
        var request = StandardWaiting();
        var deadline = new Deadline(T0.AddMinutes(5));

        request.AddCustomerMessage(MessageIntent.Question, "again", deadline.For, T0.AddMinutes(30));

        Assert.Empty(deadline.Calls);
        Assert.Equal(T0.AddMinutes(240), request.NextAttentionAtUtc);
        Assert.Equal(T0, request.AttentionSinceUtc);
    }

    [Fact]
    public void Priority_message_on_an_already_priority_request_does_not_call_the_delegate()
    {
        var request = MakeRequest();
        request.AddCustomerMessage(MessageIntent.Complaint, "bad", 60, 240, 60, T0);
        var before = request.NextAttentionAtUtc;
        var deadline = new Deadline(T0.AddMinutes(1));

        request.AddCustomerMessage(MessageIntent.CallRequested, "call", deadline.For, T0.AddMinutes(20));

        Assert.Empty(deadline.Calls);
        Assert.Equal(before, request.NextAttentionAtUtc);
    }

    // ── Standard → Priority escalation: the earlier of new and existing ───────

    [Fact]
    public void Escalation_takes_the_new_deadline_when_it_is_earlier()
    {
        var request = StandardWaiting(); // existing T0+240
        var deadline = new Deadline(T0.AddMinutes(30 + 60));

        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", deadline.For, T0.AddMinutes(30));

        Assert.Equal([PriorityBand.Priority], deadline.Calls);
        Assert.Equal(T0.AddMinutes(90), request.NextAttentionAtUtc);
        Assert.Equal(PriorityBand.Priority, request.PriorityBand);
        Assert.Equal(AttentionReason.Complaint, request.AttentionReason);
    }

    [Fact]
    public void Escalation_keeps_the_existing_deadline_when_the_new_one_is_later()
    {
        var request = StandardWaiting(); // existing T0+240
        var deadline = new Deadline(T0.AddMinutes(200 + 60)); // T0+260, later

        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", deadline.For, T0.AddMinutes(200));

        Assert.Equal(T0.AddMinutes(240), request.NextAttentionAtUtc);
        Assert.Equal(PriorityBand.Priority, request.PriorityBand); // band and reason still upgrade
        Assert.Equal(AttentionReason.Complaint, request.AttentionReason);
    }

    [Fact]
    public void Escalation_of_an_already_overdue_request_does_not_reset_the_clock()
    {
        var request = StandardWaiting(); // existing T0+240, overdue at T0+300
        var deadline = new Deadline(T0.AddMinutes(300 + 60));

        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", deadline.For, T0.AddMinutes(300));

        Assert.Equal(T0.AddMinutes(240), request.NextAttentionAtUtc);
    }

    [Fact]
    public void Escalation_with_no_existing_deadline_adopts_the_new_one()
    {
        var request = StandardWaiting();
        SetProp(request, nameof(KeepRequest.NextAttentionAtUtc), null);
        var deadline = new Deadline(T0.AddMinutes(100));

        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", deadline.For, T0.AddMinutes(40));

        Assert.Equal(T0.AddMinutes(100), request.NextAttentionAtUtc);
    }

    [Fact]
    public void Failed_escalation_keeps_the_existing_deadline_and_still_upgrades_band_and_reason()
    {
        var request = StandardWaiting();
        var deadline = new Deadline(null);

        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", deadline.For, T0.AddMinutes(30));

        Assert.Single(deadline.Calls);
        Assert.Equal(T0.AddMinutes(240), request.NextAttentionAtUtc);
        Assert.Equal(PriorityBand.Priority, request.PriorityBand);
        Assert.Equal(AttentionReason.Complaint, request.AttentionReason);
    }

    [Fact]
    public void Failed_escalation_with_no_existing_deadline_stays_null()
    {
        var request = StandardWaiting();
        SetProp(request, nameof(KeepRequest.NextAttentionAtUtc), null);

        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", new Deadline(null).For, T0.AddMinutes(30));

        Assert.Null(request.NextAttentionAtUtc);
    }

    // ── The legacy minutes overload is guarded too ────────────────────────────

    [Fact]
    public void Legacy_overload_escalation_cannot_loosen_an_existing_deadline()
    {
        var request = StandardWaiting(); // existing T0+240

        // Legacy path at T0+200: now + 60 = T0+260 would previously have loosened T0+240.
        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", 60, 240, 60, T0.AddMinutes(200));

        Assert.Equal(T0.AddMinutes(240), request.NextAttentionAtUtc);
    }

    [Fact]
    public void Legacy_overload_escalation_still_tightens_when_the_priority_deadline_is_earlier()
    {
        var request = StandardWaiting();

        request.AddCustomerMessage(MessageIntent.Complaint, "urgent", 60, 240, 60, T0.AddMinutes(30));

        Assert.Equal(T0.AddMinutes(90), request.NextAttentionAtUtc);
    }

    // ── Guards short-circuit before the delegate ──────────────────────────────

    [Fact]
    public void Validation_and_terminal_failures_never_call_the_delegate()
    {
        var deadline = new Deadline(T0.AddHours(1));

        var empty = MakeRequest().AddCustomerMessage(MessageIntent.GeneralMessage, "  ", deadline.For, T0);
        var tooLong = MakeRequest().AddCustomerMessage(MessageIntent.GeneralMessage, new string('x', 4001), deadline.For, T0);
        var closed = MakeRequest();
        SetProp(closed, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        var terminal = closed.AddCustomerMessage(MessageIntent.GeneralMessage, "hi", deadline.For, T0);

        Assert.Equal(KeepRequestErrors.MessageRequired, empty.Error);
        Assert.Equal(KeepRequestErrors.CustomerMessageTooLong, tooLong.Error);
        Assert.Equal(KeepRequestErrors.TerminalState, terminal.Error);
        Assert.Empty(deadline.Calls);
    }

    [Fact]
    public void Null_delegate_and_default_now_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MakeRequest().AddCustomerMessage(MessageIntent.GeneralMessage, "hi", (Func<PriorityBand, DateTime?>)null!, T0));
        Assert.Throws<ArgumentException>(() =>
            MakeRequest().AddCustomerMessage(MessageIntent.GeneralMessage, "hi", new Deadline(T0).For, default));
    }
}
