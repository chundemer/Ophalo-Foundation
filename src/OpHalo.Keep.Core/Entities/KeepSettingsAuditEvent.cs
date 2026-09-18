using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// An immutable, account-scoped audit record of a GAP-100/ADR-505 response-policy or
/// business-calendar settings change. Unlike <see cref="KeepRequestEvent"/> it is not bound to a
/// request; unlike <see cref="KeepProductOpsEvent"/> it is append-only, not a per-type singleton.
/// </summary>
public sealed class KeepSettingsAuditEvent : BaseEntity
{
    public Guid AccountId { get; private set; }
    public KeepSettingsAuditEventType EventType { get; private set; }
    public string? Content { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public ActorType ActorType { get; private set; }
    public Guid? ActorAccountUserId { get; private set; }
    public string? ActorDisplayName { get; private set; }

    private static KeepSettingsAuditEvent Create(
        Guid accountId,
        KeepSettingsAuditEventType eventType,
        string? content,
        DateTime occurredAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepSettingsAuditEvent
        {
            AccountId = accountId,
            EventType = eventType,
            Content = string.IsNullOrWhiteSpace(content) ? null : content.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }

    private static KeepSettingsAuditEvent CreateByAccountUser(
        Guid accountId,
        KeepSettingsAuditEventType eventType,
        string? content,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime occurredAtUtc)
    {
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));

        var evt = Create(accountId, eventType, content, occurredAtUtc);
        evt.ActorType = ActorType.AccountUser;
        evt.ActorAccountUserId = actorAccountUserId;
        evt.ActorDisplayName = actorDisplayName.Trim();
        return evt;
    }

    private static KeepSettingsAuditEvent CreateBySystem(
        Guid accountId,
        KeepSettingsAuditEventType eventType,
        string? content,
        DateTime occurredAtUtc)
    {
        var evt = Create(accountId, eventType, content, occurredAtUtc);
        evt.ActorType = ActorType.System;
        return evt;
    }

    public static KeepSettingsAuditEvent CreateResponseTargetDurationChanged(
        Guid accountId, Guid actorAccountUserId, string actorDisplayName, string? content, DateTime occurredAtUtc) =>
        CreateByAccountUser(accountId, KeepSettingsAuditEventType.ResponseTargetDurationChanged, content, actorAccountUserId, actorDisplayName, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateResponseTargetDurationChangedBySystem(
        Guid accountId, string? content, DateTime occurredAtUtc) =>
        CreateBySystem(accountId, KeepSettingsAuditEventType.ResponseTargetDurationChanged, content, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateResponseTimingBasisChanged(
        Guid accountId, Guid actorAccountUserId, string actorDisplayName, string? content, DateTime occurredAtUtc) =>
        CreateByAccountUser(accountId, KeepSettingsAuditEventType.ResponseTimingBasisChanged, content, actorAccountUserId, actorDisplayName, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateResponseTimingBasisChangedBySystem(
        Guid accountId, string? content, DateTime occurredAtUtc) =>
        CreateBySystem(accountId, KeepSettingsAuditEventType.ResponseTimingBasisChanged, content, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateWeeklyIntervalChanged(
        Guid accountId, Guid actorAccountUserId, string actorDisplayName, string? content, DateTime occurredAtUtc) =>
        CreateByAccountUser(accountId, KeepSettingsAuditEventType.WeeklyIntervalChanged, content, actorAccountUserId, actorDisplayName, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateWeeklyIntervalChangedBySystem(
        Guid accountId, string? content, DateTime occurredAtUtc) =>
        CreateBySystem(accountId, KeepSettingsAuditEventType.WeeklyIntervalChanged, content, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateClosureChanged(
        Guid accountId, Guid actorAccountUserId, string actorDisplayName, string? content, DateTime occurredAtUtc) =>
        CreateByAccountUser(accountId, KeepSettingsAuditEventType.ClosureChanged, content, actorAccountUserId, actorDisplayName, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateClosureChangedBySystem(
        Guid accountId, string? content, DateTime occurredAtUtc) =>
        CreateBySystem(accountId, KeepSettingsAuditEventType.ClosureChanged, content, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateTimeZoneChanged(
        Guid accountId, Guid actorAccountUserId, string actorDisplayName, string? content, DateTime occurredAtUtc) =>
        CreateByAccountUser(accountId, KeepSettingsAuditEventType.TimeZoneChanged, content, actorAccountUserId, actorDisplayName, occurredAtUtc);

    public static KeepSettingsAuditEvent CreateTimeZoneChangedBySystem(
        Guid accountId, string? content, DateTime occurredAtUtc) =>
        CreateBySystem(accountId, KeepSettingsAuditEventType.TimeZoneChanged, content, occurredAtUtc);
}
