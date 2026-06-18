using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Domain service for participation writes on a KeepRequest.
/// Enforces cross-participant invariants:
///   - one active Responsible per request
///   - one active participation row per user per request
///   - Responsible and Watching are mutually exclusive
/// The caller (application service) is responsible for:
///   - verifying the request is non-terminal and not OffSeason
///   - verifying the actor has the required role/permission
///   - verifying the target user's eligibility (Active Owner/Admin/Operator)
/// </summary>
public sealed class KeepRequestParticipationService
{
    /// <summary>
    /// Assigns or transfers responsibility to targetAccountUserId.
    /// If the request has no active Responsible: responsible_assigned.
    /// If a different user is already Responsible: responsible_transferred (detaches previous).
    /// If the target is already the active Responsible: idempotent no-op.
    /// If the target is currently Watching: converts them to Responsible.
    /// Previous Responsible is not auto-watched after detach (ADR-224).
    /// </summary>
    public Result<ParticipationChangeOutcome> SetResponsible(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid targetAccountUserId,
        string targetDisplayName,
        Guid actorAccountUserId,
        string actorDisplayName,
        string? note,
        bool includeNotificationIntent,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);
        if (targetAccountUserId == Guid.Empty)
            throw new ArgumentException("Target account user ID is required.", nameof(targetAccountUserId));
        if (string.IsNullOrWhiteSpace(targetDisplayName))
            throw new ArgumentException("Target display name is required.", nameof(targetDisplayName));

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var trimmedNote = TrimNote(note);
        if (trimmedNote?.Length > 4000)
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationNoteTooLong);

        var currentResponsible = FindActiveResponsible(currentParticipants);
        var existingTargetRow = currentParticipants.FirstOrDefault(p => p.AccountUserId == targetAccountUserId);

        // Idempotent no-op: target is already the active Responsible.
        if (currentResponsible?.AccountUserId == targetAccountUserId)
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        var action = currentResponsible is null
            ? ParticipationAction.ResponsibleAssigned
            : ParticipationAction.ResponsibleTransferred;

        var previousResponsibleId = currentResponsible?.AccountUserId;

        currentResponsible?.Detach(nowUtc);

        var newParticipants = new List<KeepRequestParticipant>();
        if (existingTargetRow is not null)
            existingTargetRow.Reactivate(ParticipationType.Responsible, notificationsEnabled: true, nowUtc);
        else
            newParticipants.Add(KeepRequestParticipant.Create(
                requestId, accountId, targetAccountUserId, ParticipationType.Responsible,
                notificationsEnabled: true, nowUtc));

        var intent = includeNotificationIntent
            ? ParticipationNotificationIntentKind.Assignment
            : (ParticipationNotificationIntentKind?)null;

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            action,
            targetAccountUserId, targetDisplayName.Trim(),
            previousResponsibleAccountUserId: previousResponsibleId,
            internalNote: trimmedNote,
            notificationIntentKind: intent,
            notificationIntendedRecipientAccountUserId: intent.HasValue ? targetAccountUserId : null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(
            ParticipationChangeOutcome.WithEvent(@event, newParticipants));
    }

    /// <summary>
    /// Detaches the current active Responsible without assigning a replacement.
    /// Idempotent: no-op if there is no active Responsible.
    /// targetDisplayName is nullable — the cleared user is identified from the participant
    /// row where no display-name snapshot is stored; the caller may supply it if available.
    /// </summary>
    public Result<ParticipationChangeOutcome> ClearResponsible(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string? targetDisplayName,
        string? note,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var trimmedNote = TrimNote(note);
        if (trimmedNote?.Length > 4000)
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationNoteTooLong);

        var currentResponsible = FindActiveResponsible(currentParticipants);

        // Idempotent: nothing to clear.
        if (currentResponsible is null)
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        currentResponsible.Detach(nowUtc);

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            ParticipationAction.ResponsibleCleared,
            targetAccountUserId: currentResponsible.AccountUserId,
            targetDisplayName: string.IsNullOrWhiteSpace(targetDisplayName) ? null : targetDisplayName.Trim(),
            previousResponsibleAccountUserId: null,
            internalNote: trimmedNote,
            notificationIntentKind: null,
            notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.WithEvent(@event));
    }

    /// <summary>
    /// Adds targetAccountUserId as a Watcher with notifications enabled.
    /// Idempotent: no-op if the target is already actively Watching.
    /// Fails if the target is currently the active Responsible (ADR-224 mutual exclusion).
    /// If the target had a detached row, reactivates it as Watching.
    /// </summary>
    public Result<ParticipationChangeOutcome> AddWatcher(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid targetAccountUserId,
        string targetDisplayName,
        Guid actorAccountUserId,
        string actorDisplayName,
        string? note,
        bool includeNotificationIntent,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);
        if (targetAccountUserId == Guid.Empty)
            throw new ArgumentException("Target account user ID is required.", nameof(targetAccountUserId));
        if (string.IsNullOrWhiteSpace(targetDisplayName))
            throw new ArgumentException("Target display name is required.", nameof(targetDisplayName));

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var trimmedNote = TrimNote(note);
        if (trimmedNote?.Length > 4000)
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationNoteTooLong);

        var existingTargetRow = currentParticipants.FirstOrDefault(p => p.AccountUserId == targetAccountUserId);

        // Idempotent: already actively Watching.
        if (existingTargetRow is { IsActive: true, ParticipationType: ParticipationType.Watching })
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        // Responsible/Watching mutual exclusion.
        if (existingTargetRow is { IsActive: true, ParticipationType: ParticipationType.Responsible })
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationResponsibleCannotWatch);

        var newParticipants = new List<KeepRequestParticipant>();
        if (existingTargetRow is not null)
            existingTargetRow.Reactivate(ParticipationType.Watching, notificationsEnabled: true, nowUtc);
        else
            newParticipants.Add(KeepRequestParticipant.Create(
                requestId, accountId, targetAccountUserId, ParticipationType.Watching,
                notificationsEnabled: true, nowUtc));

        var intent = includeNotificationIntent
            ? ParticipationNotificationIntentKind.WatcherAdded
            : (ParticipationNotificationIntentKind?)null;

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            ParticipationAction.WatcherAdded,
            targetAccountUserId, targetDisplayName.Trim(),
            previousResponsibleAccountUserId: null,
            internalNote: trimmedNote,
            notificationIntentKind: intent,
            notificationIntendedRecipientAccountUserId: intent.HasValue ? targetAccountUserId : null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(
            ParticipationChangeOutcome.WithEvent(@event, newParticipants));
    }

    /// <summary>
    /// Removes targetAccountUserId as a Watcher.
    /// Fails if the target is currently the active Responsible — invalid action even though
    /// it resembles a no-op (ADR-230); use ClearResponsible instead.
    /// Idempotent: no-op if the target is not actively Watching (never participated or already detached).
    /// targetDisplayName is nullable; the caller may supply it if available.
    /// </summary>
    public Result<ParticipationChangeOutcome> RemoveWatcher(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid targetAccountUserId,
        string? targetDisplayName,
        Guid actorAccountUserId,
        string actorDisplayName,
        string? note,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);
        if (targetAccountUserId == Guid.Empty)
            throw new ArgumentException("Target account user ID is required.", nameof(targetAccountUserId));

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var trimmedNote = TrimNote(note);
        if (trimmedNote?.Length > 4000)
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationNoteTooLong);

        var existingTargetRow = currentParticipants.FirstOrDefault(p => p.AccountUserId == targetAccountUserId);

        // Invalid: trying to remove a Responsible user via the watcher route.
        if (existingTargetRow is { IsActive: true, ParticipationType: ParticipationType.Responsible })
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationCannotUnwatchResponsible);

        // Idempotent: not actively Watching.
        if (existingTargetRow is not { IsActive: true, ParticipationType: ParticipationType.Watching })
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        existingTargetRow.Detach(nowUtc);

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            ParticipationAction.WatcherRemoved,
            targetAccountUserId,
            targetDisplayName: string.IsNullOrWhiteSpace(targetDisplayName) ? null : targetDisplayName.Trim(),
            previousResponsibleAccountUserId: null,
            internalNote: trimmedNote,
            notificationIntentKind: null,
            notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.WithEvent(@event));
    }

    /// <summary>
    /// Adds the current user as a Watcher (PUT /watch). Actor == target.
    /// Idempotent: no-op if already actively Watching.
    /// Fails if currently the active Responsible (ADR-224 mutual exclusion).
    /// </summary>
    public Result<ParticipationChangeOutcome> SelfWatch(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var existingRow = currentParticipants.FirstOrDefault(p => p.AccountUserId == actorAccountUserId);

        // Idempotent: already actively Watching.
        if (existingRow is { IsActive: true, ParticipationType: ParticipationType.Watching })
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        // Responsible/Watching mutual exclusion.
        if (existingRow is { IsActive: true, ParticipationType: ParticipationType.Responsible })
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationResponsibleCannotWatch);

        var newParticipants = new List<KeepRequestParticipant>();
        if (existingRow is not null)
            existingRow.Reactivate(ParticipationType.Watching, notificationsEnabled: true, nowUtc);
        else
            newParticipants.Add(KeepRequestParticipant.Create(
                requestId, accountId, actorAccountUserId, ParticipationType.Watching,
                notificationsEnabled: true, nowUtc));

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            ParticipationAction.SelfWatched,
            targetAccountUserId: actorAccountUserId,
            targetDisplayName: actorDisplayName.Trim(),
            previousResponsibleAccountUserId: null,
            internalNote: null,
            notificationIntentKind: null,
            notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(
            ParticipationChangeOutcome.WithEvent(@event, newParticipants));
    }

    /// <summary>
    /// Removes the current user's Watcher participation (DELETE /watch). Actor == target.
    /// Fails if the user is currently Responsible — Responsible is not Watching; use
    /// ClearResponsible instead (ADR-230: invalid actions fail even when they resemble no-ops).
    /// Idempotent: no-op if the user is not actively Watching (never participated or already detached).
    /// </summary>
    public Result<ParticipationChangeOutcome> SelfUnwatch(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var existingRow = currentParticipants.FirstOrDefault(p => p.AccountUserId == actorAccountUserId);

        // Invalid: trying to unwatch while Responsible.
        if (existingRow is { IsActive: true, ParticipationType: ParticipationType.Responsible })
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationCannotUnwatchResponsible);

        // Idempotent: not actively Watching.
        if (existingRow is not { IsActive: true, ParticipationType: ParticipationType.Watching })
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        existingRow.Detach(nowUtc);

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            ParticipationAction.SelfUnwatched,
            targetAccountUserId: actorAccountUserId,
            targetDisplayName: actorDisplayName.Trim(),
            previousResponsibleAccountUserId: null,
            internalNote: null,
            notificationIntentKind: null,
            notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.WithEvent(@event));
    }

    /// <summary>
    /// Mutes notifications for the current user (PUT /mute). Sets NotificationsEnabled = false.
    /// Requires an active participation row (Responsible or Watching).
    /// Idempotent: no-op if already muted.
    /// </summary>
    public Result<ParticipationChangeOutcome> Mute(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var existingRow = currentParticipants.FirstOrDefault(p => p.AccountUserId == actorAccountUserId && p.IsActive);

        if (existingRow is null)
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationMuteRequiresActiveParticipation);

        // Idempotent: already muted.
        if (!existingRow.NotificationsEnabled)
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        existingRow.SetNotificationsEnabled(false);

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            ParticipationAction.Muted,
            targetAccountUserId: actorAccountUserId,
            targetDisplayName: actorDisplayName.Trim(),
            previousResponsibleAccountUserId: null,
            internalNote: null,
            notificationIntentKind: null,
            notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.WithEvent(@event));
    }

    /// <summary>
    /// Re-enables notifications for the current user (DELETE /mute). Sets NotificationsEnabled = true.
    /// Requires an active participation row (Responsible or Watching).
    /// Idempotent: no-op if already unmuted.
    /// </summary>
    public Result<ParticipationChangeOutcome> Unmute(
        IReadOnlyList<KeepRequestParticipant> currentParticipants,
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        ValidateCommon(requestId, accountId, actorAccountUserId, actorDisplayName, nowUtc);

        var setError = ValidateParticipantSet(currentParticipants, requestId, accountId);
        if (setError is not null)
            return Result<ParticipationChangeOutcome>.Failure(setError);

        var existingRow = currentParticipants.FirstOrDefault(p => p.AccountUserId == actorAccountUserId && p.IsActive);

        if (existingRow is null)
            return Result<ParticipationChangeOutcome>.Failure(KeepRequestErrors.ParticipationMuteRequiresActiveParticipation);

        // Idempotent: already unmuted.
        if (existingRow.NotificationsEnabled)
            return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.NoOp);

        existingRow.SetNotificationsEnabled(true);

        var @event = KeepRequestEvent.CreateParticipationChanged(
            requestId, accountId, actorAccountUserId, actorDisplayName,
            ParticipationAction.Unmuted,
            targetAccountUserId: actorAccountUserId,
            targetDisplayName: actorDisplayName.Trim(),
            previousResponsibleAccountUserId: null,
            internalNote: null,
            notificationIntentKind: null,
            notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: nowUtc);

        return Result<ParticipationChangeOutcome>.Success(ParticipationChangeOutcome.WithEvent(@event));
    }

    // --- helpers ---

    /// <summary>
    /// Validates that the supplied participant set is internally consistent.
    /// All rows must belong to the given request/account, at most one active Responsible
    /// may exist, and each user may have at most one active row. Returns an error if any
    /// invariant is violated; null if the set is safe to operate on.
    /// </summary>
    private static Error? ValidateParticipantSet(
        IReadOnlyList<KeepRequestParticipant> participants,
        Guid requestId,
        Guid accountId)
    {
        if (participants.Any(p => p.RequestId != requestId || p.AccountId != accountId))
            return KeepRequestErrors.ParticipationStateCorrupt;

        if (participants.Count(p => p.IsActive && p.ParticipationType == ParticipationType.Responsible) > 1)
            return KeepRequestErrors.ParticipationStateCorrupt;

        if (participants.Where(p => p.IsActive).GroupBy(p => p.AccountUserId).Any(g => g.Count() > 1))
            return KeepRequestErrors.ParticipationStateCorrupt;

        return null;
    }

    private static KeepRequestParticipant? FindActiveResponsible(IReadOnlyList<KeepRequestParticipant> participants) =>
        participants.FirstOrDefault(p => p.IsActive && p.ParticipationType == ParticipationType.Responsible);

    private static string? TrimNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static void ValidateCommon(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime nowUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must be a real timestamp.", nameof(nowUtc));
    }
}
