using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestParticipationTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid RequestId = Guid.NewGuid();
    static readonly Guid ActorId = Guid.NewGuid();
    static readonly Guid UserId1 = Guid.NewGuid();
    static readonly Guid UserId2 = Guid.NewGuid();
    static readonly Guid UserId3 = Guid.NewGuid();
    const string ActorName = "Jane Owner";
    const string User1Name = "Alice Operator";
    const string User2Name = "Bob Admin";
    static readonly DateTime Now = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    readonly KeepRequestParticipationService _svc = new();

    static KeepRequestParticipant Active(Guid userId, ParticipationType type, bool notifications = true) =>
        KeepRequestParticipant.Create(RequestId, AccountId, userId, type, notifications, Now.AddHours(-1));

    static KeepRequestParticipant Detached(Guid userId, ParticipationType type)
    {
        var p = KeepRequestParticipant.Create(RequestId, AccountId, userId, type, true, Now.AddHours(-2));
        p.Detach(Now.AddHours(-1));
        return p;
    }

    // -----------------------------------------------------------------------
    // SetResponsible — assign
    // -----------------------------------------------------------------------

    [Fact]
    public void SetResponsible_assigns_when_unassigned()
    {
        var result = _svc.SetResponsible(
            [], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: true, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNoOp);
        Assert.Single(result.Value.NewParticipants);
        Assert.Equal(ParticipationType.Responsible, result.Value.NewParticipants[0].ParticipationType);
        Assert.True(result.Value.NewParticipants[0].NotificationsEnabled);
        Assert.Equal(ParticipationAction.ResponsibleAssigned, result.Value.Event!.ParticipationAction);
        Assert.Equal(UserId1, result.Value.Event.ParticipationTargetAccountUserId);
        Assert.Equal(ParticipationNotificationIntentKind.Assignment, result.Value.Event.ParticipationNotificationIntentKind);
        Assert.Equal(UserId1, result.Value.Event.ParticipationNotificationIntendedRecipientAccountUserId);
    }

    [Fact]
    public void SetResponsible_no_notification_intent_when_not_requested()
    {
        var result = _svc.SetResponsible(
            [], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: false, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Event!.ParticipationNotificationIntentKind);
        Assert.Null(result.Value.Event.ParticipationNotificationIntendedRecipientAccountUserId);
    }

    [Fact]
    public void SetResponsible_stores_optional_note()
    {
        var result = _svc.SetResponsible(
            [], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: "  Routing note  ", includeNotificationIntent: false, Now);

        Assert.Equal("Routing note", result.Value!.Event!.ParticipationInternalNote);
    }

    // -----------------------------------------------------------------------
    // SetResponsible — transfer
    // -----------------------------------------------------------------------

    [Fact]
    public void SetResponsible_transfers_detaches_previous()
    {
        var currentResponsible = Active(UserId1, ParticipationType.Responsible);
        var participants = new List<KeepRequestParticipant> { currentResponsible };

        var result = _svc.SetResponsible(
            participants, RequestId, AccountId,
            UserId2, User2Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: true, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNoOp);
        Assert.Equal(ParticipationAction.ResponsibleTransferred, result.Value.Event!.ParticipationAction);
        Assert.Equal(UserId2, result.Value.Event.ParticipationTargetAccountUserId);
        Assert.Equal(UserId1, result.Value.Event.ParticipationPreviousResponsibleAccountUserId);
        Assert.False(currentResponsible.IsActive); // detached
    }

    [Fact]
    public void SetResponsible_transfer_does_not_auto_watch_previous()
    {
        var currentResponsible = Active(UserId1, ParticipationType.Responsible);
        var participants = new List<KeepRequestParticipant> { currentResponsible };

        _svc.SetResponsible(participants, RequestId, AccountId,
            UserId2, User2Name, ActorId, ActorName, null, false, Now);

        // Previous Responsible is detached and NOT converted to Watching.
        Assert.False(currentResponsible.IsActive);
        Assert.Equal(ParticipationType.Responsible, currentResponsible.ParticipationType);
    }

    [Fact]
    public void SetResponsible_converts_watching_user_to_responsible()
    {
        var watcher = Active(UserId1, ParticipationType.Watching);
        var participants = new List<KeepRequestParticipant> { watcher };

        var result = _svc.SetResponsible(
            participants, RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: false, Now);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.NewParticipants); // reactivated existing row
        Assert.Equal(ParticipationType.Responsible, watcher.ParticipationType);
        Assert.True(watcher.IsActive);
    }

    [Fact]
    public void SetResponsible_idempotent_noop_when_already_responsible()
    {
        var currentResponsible = Active(UserId1, ParticipationType.Responsible);

        var result = _svc.SetResponsible(
            [currentResponsible], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: true, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
        Assert.Null(result.Value.Event);
    }

    // -----------------------------------------------------------------------
    // ClearResponsible
    // -----------------------------------------------------------------------

    [Fact]
    public void ClearResponsible_detaches_current_responsible()
    {
        var responsible = Active(UserId1, ParticipationType.Responsible);

        var result = _svc.ClearResponsible(
            [responsible], RequestId, AccountId,
            ActorId, ActorName,
            targetDisplayName: User1Name, note: null, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNoOp);
        Assert.False(responsible.IsActive);
        Assert.Equal(ParticipationAction.ResponsibleCleared, result.Value.Event!.ParticipationAction);
        Assert.Equal(UserId1, result.Value.Event.ParticipationTargetAccountUserId);
        Assert.Null(result.Value.Event.ParticipationNotificationIntentKind);
    }

    [Fact]
    public void ClearResponsible_idempotent_noop_when_unassigned()
    {
        var result = _svc.ClearResponsible(
            [], RequestId, AccountId,
            ActorId, ActorName,
            targetDisplayName: null, note: null, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
    }

    // -----------------------------------------------------------------------
    // AddWatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void AddWatcher_creates_new_watching_participant()
    {
        var result = _svc.AddWatcher(
            [], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: true, Now);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.NewParticipants);
        Assert.Equal(ParticipationType.Watching, result.Value.NewParticipants[0].ParticipationType);
        Assert.True(result.Value.NewParticipants[0].NotificationsEnabled);
        Assert.Equal(ParticipationAction.WatcherAdded, result.Value.Event!.ParticipationAction);
        Assert.Equal(ParticipationNotificationIntentKind.WatcherAdded, result.Value.Event.ParticipationNotificationIntentKind);
    }

    [Fact]
    public void AddWatcher_reactivates_detached_row()
    {
        var detached = Detached(UserId1, ParticipationType.Watching);

        var result = _svc.AddWatcher(
            [detached], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: false, Now);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.NewParticipants);
        Assert.True(detached.IsActive);
        Assert.Equal(ParticipationType.Watching, detached.ParticipationType);
    }

    [Fact]
    public void AddWatcher_idempotent_noop_when_already_watching()
    {
        var watcher = Active(UserId1, ParticipationType.Watching);

        var result = _svc.AddWatcher(
            [watcher], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: true, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
    }

    [Fact]
    public void AddWatcher_fails_when_target_is_responsible()
    {
        var responsible = Active(UserId1, ParticipationType.Responsible);

        var result = _svc.AddWatcher(
            [responsible], RequestId, AccountId,
            UserId1, User1Name,
            ActorId, ActorName,
            note: null, includeNotificationIntent: false, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationResponsibleCannotWatch.Code, result.Error!.Code);
    }

    // -----------------------------------------------------------------------
    // RemoveWatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void RemoveWatcher_detaches_watching_participant()
    {
        var watcher = Active(UserId1, ParticipationType.Watching);

        var result = _svc.RemoveWatcher(
            [watcher], RequestId, AccountId,
            UserId1, targetDisplayName: User1Name,
            ActorId, ActorName,
            note: null, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNoOp);
        Assert.False(watcher.IsActive);
        Assert.Equal(ParticipationAction.WatcherRemoved, result.Value.Event!.ParticipationAction);
    }

    [Fact]
    public void RemoveWatcher_idempotent_noop_for_non_watching_user()
    {
        // User has never participated.
        var result = _svc.RemoveWatcher(
            [], RequestId, AccountId,
            UserId1, targetDisplayName: null,
            ActorId, ActorName,
            note: null, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
    }

    [Fact]
    public void RemoveWatcher_fails_when_target_is_responsible()
    {
        var responsible = Active(UserId1, ParticipationType.Responsible);

        var result = _svc.RemoveWatcher(
            [responsible], RequestId, AccountId,
            UserId1, targetDisplayName: User1Name,
            ActorId, ActorName,
            note: null, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationCannotUnwatchResponsible.Code, result.Error!.Code);
    }

    // -----------------------------------------------------------------------
    // SelfWatch / SelfUnwatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SelfWatch_adds_actor_as_watcher()
    {
        var result = _svc.SelfWatch([], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.NewParticipants);
        Assert.Equal(ParticipationAction.SelfWatched, result.Value.Event!.ParticipationAction);
        Assert.Equal(ActorId, result.Value.Event.ParticipationTargetAccountUserId);
        Assert.Null(result.Value.Event.ParticipationNotificationIntentKind);
    }

    [Fact]
    public void SelfWatch_idempotent_noop_when_already_watching()
    {
        var watcher = Active(ActorId, ParticipationType.Watching);

        var result = _svc.SelfWatch([watcher], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
    }

    [Fact]
    public void SelfWatch_fails_when_actor_is_responsible()
    {
        var responsible = Active(ActorId, ParticipationType.Responsible);

        var result = _svc.SelfWatch([responsible], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationResponsibleCannotWatch.Code, result.Error!.Code);
    }

    [Fact]
    public void SelfUnwatch_detaches_actor_watcher()
    {
        var watcher = Active(ActorId, ParticipationType.Watching);

        var result = _svc.SelfUnwatch([watcher], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNoOp);
        Assert.False(watcher.IsActive);
        Assert.Equal(ParticipationAction.SelfUnwatched, result.Value.Event!.ParticipationAction);
    }

    [Fact]
    public void SelfUnwatch_idempotent_noop_when_not_watching()
    {
        var result = _svc.SelfUnwatch([], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
    }

    [Fact]
    public void SelfUnwatch_fails_when_actor_is_responsible()
    {
        var responsible = Active(ActorId, ParticipationType.Responsible);

        var result = _svc.SelfUnwatch([responsible], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationCannotUnwatchResponsible.Code, result.Error!.Code);
    }

    // -----------------------------------------------------------------------
    // Mute / Unmute
    // -----------------------------------------------------------------------

    [Fact]
    public void Mute_disables_notifications_for_active_participant()
    {
        var watcher = Active(ActorId, ParticipationType.Watching, notifications: true);

        var result = _svc.Mute([watcher], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNoOp);
        Assert.False(watcher.NotificationsEnabled);
        Assert.Equal(ParticipationAction.Muted, result.Value.Event!.ParticipationAction);
    }

    [Fact]
    public void Mute_works_for_responsible_participant()
    {
        var responsible = Active(ActorId, ParticipationType.Responsible, notifications: true);

        var result = _svc.Mute([responsible], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.False(responsible.NotificationsEnabled);
    }

    [Fact]
    public void Mute_idempotent_noop_when_already_muted()
    {
        var watcher = Active(ActorId, ParticipationType.Watching, notifications: false);

        var result = _svc.Mute([watcher], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
    }

    [Fact]
    public void Mute_fails_when_no_active_participation()
    {
        var result = _svc.Mute([], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationMuteRequiresActiveParticipation.Code, result.Error!.Code);
    }

    [Fact]
    public void Mute_fails_when_only_detached_row_exists()
    {
        var detached = Detached(ActorId, ParticipationType.Watching);

        var result = _svc.Mute([detached], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationMuteRequiresActiveParticipation.Code, result.Error!.Code);
    }

    [Fact]
    public void Unmute_enables_notifications_for_muted_participant()
    {
        var watcher = Active(ActorId, ParticipationType.Watching, notifications: false);

        var result = _svc.Unmute([watcher], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsNoOp);
        Assert.True(watcher.NotificationsEnabled);
        Assert.Equal(ParticipationAction.Unmuted, result.Value.Event!.ParticipationAction);
    }

    [Fact]
    public void Unmute_idempotent_noop_when_already_enabled()
    {
        var watcher = Active(ActorId, ParticipationType.Watching, notifications: true);

        var result = _svc.Unmute([watcher], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsNoOp);
    }

    // -----------------------------------------------------------------------
    // Participant-set invariant: participant set validation
    // -----------------------------------------------------------------------

    [Fact]
    public void SetResponsible_fails_when_participant_belongs_to_wrong_request()
    {
        var wrongRequest = KeepRequestParticipant.Create(
            Guid.NewGuid(), AccountId, UserId1, ParticipationType.Watching, true, Now);

        var result = _svc.SetResponsible(
            [wrongRequest], RequestId, AccountId,
            UserId2, User2Name, ActorId, ActorName, null, false, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationStateCorrupt.Code, result.Error!.Code);
    }

    [Fact]
    public void SetResponsible_fails_when_two_active_responsible_rows_exist()
    {
        var r1 = Active(UserId1, ParticipationType.Responsible);
        var r2 = Active(UserId2, ParticipationType.Responsible);

        var result = _svc.SetResponsible(
            [r1, r2], RequestId, AccountId,
            UserId3, User2Name, ActorId, ActorName, null, false, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationStateCorrupt.Code, result.Error!.Code);
    }

    [Fact]
    public void SetResponsible_fails_when_same_user_has_two_active_rows()
    {
        var a = Active(UserId1, ParticipationType.Responsible);
        var b = Active(UserId1, ParticipationType.Watching);

        var result = _svc.SetResponsible(
            [a, b], RequestId, AccountId,
            UserId2, User2Name, ActorId, ActorName, null, false, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationStateCorrupt.Code, result.Error!.Code);
    }

    // -----------------------------------------------------------------------
    // Note validation
    // -----------------------------------------------------------------------

    [Fact]
    public void SetResponsible_fails_when_note_exceeds_4000_chars()
    {
        var result = _svc.SetResponsible(
            [], RequestId, AccountId,
            UserId1, User1Name, ActorId, ActorName,
            note: new string('x', 4001),
            includeNotificationIntent: false, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationNoteTooLong.Code, result.Error!.Code);
    }

    [Fact]
    public void ClearResponsible_fails_when_note_exceeds_4000_chars()
    {
        var responsible = Active(UserId1, ParticipationType.Responsible);

        var result = _svc.ClearResponsible(
            [responsible], RequestId, AccountId,
            ActorId, ActorName,
            targetDisplayName: null,
            note: new string('x', 4001),
            Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.ParticipationNoteTooLong.Code, result.Error!.Code);
    }

    // -----------------------------------------------------------------------
    // Event fields
    // -----------------------------------------------------------------------

    [Fact]
    public void ParticipationChanged_event_is_internal_visibility()
    {
        var result = _svc.SelfWatch([], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.Equal(KeepRequestEventVisibility.Internal, result.Value!.Event!.Visibility);
    }

    [Fact]
    public void ParticipationChanged_event_has_correct_actor_fields()
    {
        var result = _svc.SelfWatch([], RequestId, AccountId, ActorId, ActorName, Now);

        Assert.Equal(ActorId, result.Value!.Event!.ActorAccountUserId);
        Assert.Equal(ActorName, result.Value.Event.ActorDisplayName);
    }

    [Fact]
    public void Transfer_records_previous_responsible_id_in_event()
    {
        var r1 = Active(UserId1, ParticipationType.Responsible);

        var result = _svc.SetResponsible(
            [r1], RequestId, AccountId,
            UserId2, User2Name, ActorId, ActorName, null, false, Now);

        Assert.Equal(UserId1, result.Value!.Event!.ParticipationPreviousResponsibleAccountUserId);
    }

    [Fact]
    public void Assign_has_null_previous_responsible_in_event()
    {
        var result = _svc.SetResponsible(
            [], RequestId, AccountId,
            UserId1, User1Name, ActorId, ActorName, null, false, Now);

        Assert.Null(result.Value!.Event!.ParticipationPreviousResponsibleAccountUserId);
    }

    // -----------------------------------------------------------------------
    // KeepRequestParticipant mutation methods
    // -----------------------------------------------------------------------

    [Fact]
    public void Detach_sets_DetachedAtUtc_and_IsActive_false()
    {
        var p = Active(UserId1, ParticipationType.Watching);
        p.Detach(Now);
        Assert.False(p.IsActive);
        Assert.Equal(Now, p.DetachedAtUtc);
    }

    [Fact]
    public void Reactivate_clears_DetachedAtUtc_and_updates_type()
    {
        var p = Detached(UserId1, ParticipationType.Watching);
        p.Reactivate(ParticipationType.Responsible, notificationsEnabled: true, Now);
        Assert.True(p.IsActive);
        Assert.Null(p.DetachedAtUtc);
        Assert.Equal(ParticipationType.Responsible, p.ParticipationType);
        Assert.Equal(Now, p.AttachedAtUtc);
    }

    [Fact]
    public void SetNotificationsEnabled_flips_flag_without_detaching()
    {
        var p = Active(UserId1, ParticipationType.Watching, notifications: true);
        p.SetNotificationsEnabled(false);
        Assert.False(p.NotificationsEnabled);
        Assert.True(p.IsActive);
    }
}
