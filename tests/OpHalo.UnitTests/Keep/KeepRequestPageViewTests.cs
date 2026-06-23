using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestPageViewTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly DateTime Now = new(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc);

    static KeepRequest ActiveRequest() =>
        KeepRequest.CreateByBusiness(
            AccountId, CustomerId,
            "Jane Smith", "0412345678", null,
            "Burst pipe", "PQRS0001", "tok_abc", Now);

    static KeepRequest ClosedRequest()
    {
        var r = ActiveRequest();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, ActorId, "Jane", Now);
        r.ChangeStatus(KeepRequestStatus.Closed, null, ActorId, "Jane", Now);
        return r;
    }

    // -------------------------------------------------------------------------
    // First view
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordCustomerPageView_first_view_sets_timestamp_and_returns_true()
    {
        var r = ActiveRequest();

        var needsWrite = r.RecordCustomerPageView(Now);

        Assert.True(needsWrite);
        Assert.Equal(Now, r.CustomerPageLastViewedAtUtc);
    }

    // -------------------------------------------------------------------------
    // Debounce — within window
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordCustomerPageView_within_debounce_window_returns_false_and_does_not_update()
    {
        var r = ActiveRequest();
        r.RecordCustomerPageView(Now, debounceMinutes: 5);

        var refresh = Now.AddMinutes(3); // still inside 5-minute window
        var needsWrite = r.RecordCustomerPageView(refresh, debounceMinutes: 5);

        Assert.False(needsWrite);
        Assert.Equal(Now, r.CustomerPageLastViewedAtUtc); // unchanged
    }

    [Fact]
    public void RecordCustomerPageView_exactly_at_debounce_boundary_triggers_write()
    {
        var r = ActiveRequest();
        r.RecordCustomerPageView(Now, debounceMinutes: 5);

        // Exactly 5 minutes elapsed — 5 < 5 is false, so the window has closed and a write is triggered.
        var atBoundary = Now.AddMinutes(5);
        var needsWrite = r.RecordCustomerPageView(atBoundary, debounceMinutes: 5);

        Assert.True(needsWrite);
        Assert.Equal(atBoundary, r.CustomerPageLastViewedAtUtc);
    }

    // -------------------------------------------------------------------------
    // Debounce — after window
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordCustomerPageView_after_debounce_window_updates_timestamp_and_returns_true()
    {
        var r = ActiveRequest();
        r.RecordCustomerPageView(Now, debounceMinutes: 5);

        var later = Now.AddMinutes(6); // past 5-minute window
        var needsWrite = r.RecordCustomerPageView(later, debounceMinutes: 5);

        Assert.True(needsWrite);
        Assert.Equal(later, r.CustomerPageLastViewedAtUtc);
    }

    // -------------------------------------------------------------------------
    // Works for terminal requests too (ADR-341 records all non-expired accessible pages)
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordCustomerPageView_on_closed_request_sets_timestamp()
    {
        var r = ClosedRequest();

        var needsWrite = r.RecordCustomerPageView(Now);

        Assert.True(needsWrite);
        Assert.Equal(Now, r.CustomerPageLastViewedAtUtc);
    }

    // -------------------------------------------------------------------------
    // Guard arguments
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordCustomerPageView_default_nowUtc_throws()
    {
        var r = ActiveRequest();
        Assert.Throws<ArgumentException>(() => r.RecordCustomerPageView(default));
    }

    [Fact]
    public void RecordCustomerPageView_zero_debounce_throws()
    {
        var r = ActiveRequest();
        Assert.Throws<ArgumentOutOfRangeException>(() => r.RecordCustomerPageView(Now, debounceMinutes: 0));
    }
}
