using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestServiceLocationTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    static KeepRequest AnyRequest() =>
        KeepRequest.CreateByBusiness(
            AccountId, CustomerId, "Jane Smith", "555-0001", null,
            "Burst pipe", "PQRS0001", "tok_loc", Now, KeepRequestSource.Phone);

    static KeepRequest PublicIntakeRequest() =>
        KeepRequest.CreateFromCustomerIntake(
            AccountId, CustomerId, "Jane Smith", "555-0001", null,
            "Burst pipe", "PQRS0002", "tok_pi", Now, 60);

    // --- Success: sets fields and emits event ---

    [Fact]
    public void SetServiceLocation_sets_required_fields_and_emits_event()
    {
        var r = AnyRequest();

        var result = r.SetServiceLocation(
            "123 Main St", null, "Springfield", "IL", null,
            ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("123 Main St", r.ServiceAddressLine1);
        Assert.Null(r.ServiceAddressLine2);
        Assert.Equal("Springfield", r.ServiceCity);
        Assert.Equal("IL", r.ServiceState);
        Assert.Null(r.ServiceZip);
        Assert.Equal(Now, r.LastBusinessActivityAt);

        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.ServiceLocationChanged, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.Internal, ev.Visibility);
        Assert.Equal(ActorId, ev.ActorAccountUserId);
        Assert.Equal("Jane", ev.ActorDisplayName);
    }

    [Fact]
    public void SetServiceLocation_sets_all_optional_fields()
    {
        var r = AnyRequest();

        var result = r.SetServiceLocation(
            "456 Oak Ave", "Suite 2", "Chicago", "IL", "60601",
            ActorId, "Bob", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("456 Oak Ave", r.ServiceAddressLine1);
        Assert.Equal("Suite 2", r.ServiceAddressLine2);
        Assert.Equal("Chicago", r.ServiceCity);
        Assert.Equal("IL", r.ServiceState);
        Assert.Equal("60601", r.ServiceZip);
    }

    [Fact]
    public void SetServiceLocation_normalizes_state_to_uppercase()
    {
        var r = AnyRequest();

        var result = r.SetServiceLocation(
            "1 Main St", null, "Springfield", "il", null,
            ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("IL", r.ServiceState);
    }

    [Fact]
    public void SetServiceLocation_trims_whitespace_from_fields()
    {
        var r = AnyRequest();

        var result = r.SetServiceLocation(
            "  123 Main St  ", "  Apt 1  ", "  Springfield  ", " IL ", "  62701  ",
            ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("123 Main St", r.ServiceAddressLine1);
        Assert.Equal("Apt 1", r.ServiceAddressLine2);
        Assert.Equal("Springfield", r.ServiceCity);
        Assert.Equal("IL", r.ServiceState);
        Assert.Equal("62701", r.ServiceZip);
    }

    [Fact]
    public void SetServiceLocation_clears_optional_fields_when_blank()
    {
        var r = AnyRequest();
        // Set a full location first
        r.SetServiceLocation("123 Main St", "Suite 1", "Springfield", "IL", "62701",
            ActorId, "Jane", Now);

        // Update with no line2 or zip
        var result = r.SetServiceLocation(
            "456 Oak Ave", "   ", "Chicago", "IL", "  ",
            ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Null(r.ServiceAddressLine2);
        Assert.Null(r.ServiceZip);
    }

    [Fact]
    public void SetServiceLocation_allowed_on_public_intake_request()
    {
        var r = PublicIntakeRequest();

        var result = r.SetServiceLocation(
            "123 Main St", null, "Springfield", "IL", null,
            ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
    }

    // --- Validation failures ---

    [Fact]
    public void SetServiceLocation_fails_when_address_line1_is_blank()
    {
        var r = AnyRequest();

        var result = r.SetServiceLocation(
            "   ", null, "Springfield", "IL", null,
            ActorId, "Jane", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepRequestErrors.ServiceAddressLine1Required.Code, result.Error.Code);
    }

    [Fact]
    public void SetServiceLocation_fails_when_city_is_blank()
    {
        var r = AnyRequest();

        var result = r.SetServiceLocation(
            "123 Main St", null, "  ", "IL", null,
            ActorId, "Jane", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepRequestErrors.ServiceCityRequired.Code, result.Error.Code);
    }

    [Fact]
    public void SetServiceLocation_fails_when_state_is_blank()
    {
        var r = AnyRequest();

        var result = r.SetServiceLocation(
            "123 Main St", null, "Springfield", "  ", null,
            ActorId, "Jane", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepRequestErrors.ServiceStateRequired.Code, result.Error.Code);
    }

    // --- Event shape ---

    [Fact]
    public void SetServiceLocation_event_is_internal_visibility()
    {
        var r = AnyRequest();
        var result = r.SetServiceLocation(
            "1 Street", null, "City", "TX", null,
            ActorId, "Actor", Now);

        Assert.Equal(KeepRequestEventVisibility.Internal, result.Value.Visibility);
        Assert.Equal(ActorType.AccountUser, result.Value.ActorType);
    }
}
