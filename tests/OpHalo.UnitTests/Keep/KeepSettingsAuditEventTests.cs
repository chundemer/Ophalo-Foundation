using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepSettingsAuditEventTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid ActorAccountUserId = Guid.NewGuid();
    static readonly DateTime OccurredAt = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateResponseTargetDurationChanged_sets_all_fields()
    {
        var evt = KeepSettingsAuditEvent.CreateResponseTargetDurationChanged(
            AccountId, ActorAccountUserId, "Jane Doe", "First response: 60 -> 90 minutes", OccurredAt);

        Assert.Equal(AccountId, evt.AccountId);
        Assert.Equal(KeepSettingsAuditEventType.ResponseTargetDurationChanged, evt.EventType);
        Assert.Equal("First response: 60 -> 90 minutes", evt.Content);
        Assert.Equal(OccurredAt, evt.OccurredAtUtc);
        Assert.Equal(ActorType.AccountUser, evt.ActorType);
        Assert.Equal(ActorAccountUserId, evt.ActorAccountUserId);
        Assert.Equal("Jane Doe", evt.ActorDisplayName);
        Assert.NotEqual(Guid.Empty, evt.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_normalizes_blank_content_to_null(string? content)
    {
        var evt = KeepSettingsAuditEvent.CreateTimeZoneChanged(
            AccountId, ActorAccountUserId, "Jane Doe", content, OccurredAt);

        Assert.Null(evt.Content);
    }

    [Fact]
    public void Create_trims_actor_display_name_and_content()
    {
        var evt = KeepSettingsAuditEvent.CreateClosureChanged(
            AccountId, ActorAccountUserId, "  Jane Doe  ", "  Added 2026-12-25  ", OccurredAt);

        Assert.Equal("Jane Doe", evt.ActorDisplayName);
        Assert.Equal("Added 2026-12-25", evt.Content);
    }

    [Fact]
    public void Create_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepSettingsAuditEvent.CreateWeeklyIntervalChanged(
                Guid.Empty, ActorAccountUserId, "Jane Doe", null, OccurredAt));

    [Fact]
    public void Create_requires_non_empty_actor_account_user_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepSettingsAuditEvent.CreateResponseTimingBasisChanged(
                AccountId, Guid.Empty, "Jane Doe", null, OccurredAt));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_non_blank_actor_display_name(string? actorDisplayName) =>
        Assert.Throws<ArgumentException>(() =>
            KeepSettingsAuditEvent.CreateTimeZoneChanged(
                AccountId, ActorAccountUserId, actorDisplayName!, null, OccurredAt));

    [Fact]
    public void Create_requires_a_real_occurred_at_timestamp() =>
        Assert.Throws<ArgumentException>(() =>
            KeepSettingsAuditEvent.CreateClosureChanged(
                AccountId, ActorAccountUserId, "Jane Doe", null, default));

    [Fact]
    public void CreateBySystem_sets_system_actor_with_no_actor_identity()
    {
        var evt = KeepSettingsAuditEvent.CreateResponseTargetDurationChangedBySystem(
            AccountId, "Migration backfill: Continuous", OccurredAt);

        Assert.Equal(ActorType.System, evt.ActorType);
        Assert.Null(evt.ActorAccountUserId);
        Assert.Null(evt.ActorDisplayName);
        Assert.Equal(KeepSettingsAuditEventType.ResponseTargetDurationChanged, evt.EventType);
    }

    [Fact]
    public void CreateBySystem_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepSettingsAuditEvent.CreateTimeZoneChangedBySystem(Guid.Empty, null, OccurredAt));

    [Fact]
    public void CreateBySystem_requires_a_real_occurred_at_timestamp() =>
        Assert.Throws<ArgumentException>(() =>
            KeepSettingsAuditEvent.CreateWeeklyIntervalChangedBySystem(AccountId, null, default));
}
