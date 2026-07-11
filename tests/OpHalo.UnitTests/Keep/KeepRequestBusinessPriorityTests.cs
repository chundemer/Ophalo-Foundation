using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestBusinessPriorityTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    static KeepRequest AnyRequest() =>
        KeepRequest.CreateByBusiness(
            AccountId, CustomerId, "Jane Smith", "555-0001", null,
            "AC not cooling", "ABCD0001", "tok_pri", Now, KeepRequestSource.Phone);

    // --- Success: sets field and emits event ---

    [Fact]
    public void SetBusinessPriority_sets_field_and_emits_event()
    {
        var r = AnyRequest();

        var result = r.SetBusinessPriority(BusinessPriority.Urgent, ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(BusinessPriority.Urgent, r.BusinessPriority);

        var ev = result.Value;
        Assert.Equal(KeepRequestEventType.BusinessPriorityChanged, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.Internal, ev.Visibility);
        Assert.Equal(ActorType.AccountUser, ev.ActorType);
        Assert.Equal(ActorId, ev.ActorAccountUserId);
        Assert.Equal("Jane", ev.ActorDisplayName);
    }

    [Fact]
    public void SetBusinessPriority_content_describes_change_from_not_set_to_value()
    {
        var r = AnyRequest();

        var result = r.SetBusinessPriority(BusinessPriority.Routine, ActorId, "Bob", Now);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Content);
        Assert.Contains("Not set", result.Value.Content);
        Assert.Contains("Routine", result.Value.Content);
    }

    [Fact]
    public void SetBusinessPriority_content_describes_change_between_values()
    {
        var r = AnyRequest();
        r.SetBusinessPriority(BusinessPriority.Urgent, ActorId, "Alice", Now);

        var result = r.SetBusinessPriority(BusinessPriority.Routine, ActorId, "Alice", Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Content);
        Assert.Contains("Urgent", result.Value.Content);
        Assert.Contains("Routine", result.Value.Content);
    }

    [Fact]
    public void SetBusinessPriority_null_clears_field_and_emits_event()
    {
        var r = AnyRequest();
        r.SetBusinessPriority(BusinessPriority.Soon, ActorId, "Jane", Now);

        var result = r.SetBusinessPriority(null, ActorId, "Jane", Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Null(r.BusinessPriority);
        Assert.Equal(KeepRequestEventType.BusinessPriorityChanged, result.Value.EventType);
        Assert.NotNull(result.Value.Content);
        Assert.Contains("Not set", result.Value.Content);
    }

    [Fact]
    public void SetBusinessPriority_defaults_to_null_on_new_request()
    {
        var r = AnyRequest();

        Assert.Null(r.BusinessPriority);
    }

    // --- Guard args ---

    [Fact]
    public void SetBusinessPriority_throws_when_actor_id_empty()
    {
        var r = AnyRequest();

        Assert.Throws<ArgumentException>(() =>
            r.SetBusinessPriority(BusinessPriority.Urgent, Guid.Empty, "Jane", Now));
    }

    [Fact]
    public void SetBusinessPriority_throws_when_actor_display_name_blank()
    {
        var r = AnyRequest();

        Assert.Throws<ArgumentException>(() =>
            r.SetBusinessPriority(BusinessPriority.Urgent, ActorId, "  ", Now));
    }

    [Fact]
    public void SetBusinessPriority_throws_when_nowUtc_is_default()
    {
        var r = AnyRequest();

        Assert.Throws<ArgumentException>(() =>
            r.SetBusinessPriority(BusinessPriority.Urgent, ActorId, "Jane", default));
    }
}
