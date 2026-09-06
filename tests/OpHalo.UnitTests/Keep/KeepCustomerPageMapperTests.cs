using System.Reflection;
using System.Runtime.CompilerServices;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// GAP-033: the public customer request feed is a default-deny allowlist. Only StatusChanged and
/// customer/business MessageAdded events (with a customer-safe intent) may appear; every other
/// current event type, and any unknown/future enum value, must be excluded.
/// </summary>
public class KeepCustomerPageMapperTests
{
    static readonly Guid ReqId = Guid.NewGuid();
    static readonly Guid AcctId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StatusChanged_is_customer_visible()
    {
        var e = KeepRequestEvent.CreateStatusChanged(
            ReqId, AcctId, ActorId, "Operator", KeepRequestStatus.Scheduled, "We scheduled a visit.", Now);

        Assert.True(KeepCustomerPageMapper.IsCustomerVisibleEvent(e));
    }

    [Fact]
    public void Customer_submitted_message_is_customer_visible()
    {
        var e = KeepRequestEvent.CreateCustomerMessage(
            ReqId, AcctId, "Alice", MessageIntent.Question, "Any update?", Now);

        Assert.True(KeepCustomerPageMapper.IsCustomerVisibleEvent(e));
    }

    [Fact]
    public void Business_update_message_is_customer_visible()
    {
        var e = KeepRequestEvent.CreateBusinessUpdateMessage(
            ReqId, AcctId, ActorId, "Operator", "Technician is on the way.", Now);

        Assert.True(KeepCustomerPageMapper.IsCustomerVisibleEvent(e));
    }

    [Fact]
    public void Internal_events_are_never_customer_visible()
    {
        KeepRequestEvent[] internalEvents =
        [
            KeepRequestEvent.CreateInternalNote(ReqId, AcctId, ActorId, "Operator", "call supplier", Now),
            KeepRequestEvent.CreateAttentionAcknowledged(ReqId, AcctId, ActorId, "Operator", "reviewed", Now),
            KeepRequestEvent.CreateFeedbackReceived(ReqId, AcctId, wasResolved: true, "thanks", Now),
            KeepRequestEvent.CreateExternalContactLogged(
                ReqId, AcctId, ActorId, "Operator", ExternalContactDirection.Outbound,
                CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer, requiresFollowUp: false,
                "left a message", setFirstResponse: false, clearedAttention: false, Now),
            KeepRequestEvent.CreateServiceLocationChanged(ReqId, AcctId, ActorId, "Operator", Now),
            KeepRequestEvent.CreateBusinessPriorityChanged(
                ReqId, AcctId, ActorId, "Operator", "Priority changed from Normal to High", Now),
        ];

        Assert.All(internalEvents, e => Assert.False(KeepCustomerPageMapper.IsCustomerVisibleEvent(e)));
    }

    [Fact]
    public void MessageAdded_from_system_actor_is_not_customer_visible()
    {
        var e = MakeRaw(KeepRequestEventType.MessageAdded, KeepRequestEventVisibility.All,
            ActorType.System, MessageIntent.BusinessUpdate);

        Assert.False(KeepCustomerPageMapper.IsCustomerVisibleEvent(e));
    }

    [Fact]
    public void MessageAdded_without_intent_is_not_customer_visible()
    {
        var e = MakeRaw(KeepRequestEventType.MessageAdded, KeepRequestEventVisibility.All,
            ActorType.AccountUser, intent: null);

        Assert.False(KeepCustomerPageMapper.IsCustomerVisibleEvent(e));
    }

    [Fact]
    public void StatusChanged_stored_as_internal_is_not_customer_visible()
    {
        var e = MakeRaw(KeepRequestEventType.StatusChanged, KeepRequestEventVisibility.Internal,
            ActorType.AccountUser, intent: null);

        Assert.False(KeepCustomerPageMapper.IsCustomerVisibleEvent(e));
    }

    [Theory]
    [MemberData(nameof(AllEventTypes))]
    public void Only_status_changed_and_message_added_pass_the_allowlist(KeepRequestEventType type)
    {
        // Force the most permissive supporting state so the type itself is the only variable.
        var e = MakeRaw(type, KeepRequestEventVisibility.All, ActorType.AccountUser, MessageIntent.BusinessUpdate);

        var expected = type is KeepRequestEventType.StatusChanged or KeepRequestEventType.MessageAdded;

        Assert.Equal(expected, KeepCustomerPageMapper.IsCustomerVisibleEvent(e));
    }

    public static IEnumerable<object[]> AllEventTypes() =>
        Enum.GetValues<KeepRequestEventType>().Select(t => new object[] { t });

    static KeepRequestEvent MakeRaw(
        KeepRequestEventType type,
        KeepRequestEventVisibility visibility,
        ActorType actorType,
        MessageIntent? intent)
    {
        var e = (KeepRequestEvent)RuntimeHelpers.GetUninitializedObject(typeof(KeepRequestEvent));
        Set(e, nameof(KeepRequestEvent.EventType), type);
        Set(e, nameof(KeepRequestEvent.Visibility), visibility);
        Set(e, nameof(KeepRequestEvent.ActorType), actorType);
        Set(e, nameof(KeepRequestEvent.MessageIntent), intent);
        return e;
    }

    static void Set(object target, string property, object? value) =>
        typeof(KeepRequestEvent)
            .GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(target, value);
}
