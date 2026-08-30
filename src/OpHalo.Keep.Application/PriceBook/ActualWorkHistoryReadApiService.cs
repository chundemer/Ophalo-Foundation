using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Price-blind line projection shared by <see cref="ActualWorkOpenDraftEntry"/> and
/// <see cref="ActualWorkSubmittedVisitEntry"/> — no catalog/price-book ids, price, cost, recorder
/// identity, or recorded time (build-log/129, not yet approved for this field-facing view).</summary>
public sealed record ActualWorkLineHistoryEntry(
    Guid Id, string DisplayNameSnapshot, string? UnitOfMeasureSnapshot, decimal ActualQuantity, string? Note,
    Guid PerformedByAccountUserId, string? PerformerDisplayName);

/// <summary>The open Draft for this request, returned to its current recorder (GAP-055) for
/// resume-after-reload editing, or to Owner/Admin read-only so they have grounds to decide on a
/// recorder transfer even when they are not the recorder. <see cref="IsRecorder"/> disambiguates
/// which case this is: false means the composer must render read-only, never offer the mutation
/// actions a recorder gets. Carries <see cref="ConcurrencyVersion"/> because it is the
/// resume-after-reload projection the capture composer edits against.
/// <para><see cref="RecorderAccountUserId"/> and <see cref="RecorderDisplayName"/> are populated
/// only for the Owner/Admin non-recorder case (1a-ii) — the recovery UI needs the id to exclude
/// the current recorder from the transfer-candidate list (a "transfer" back to the current holder
/// is a meaningless no-op that would still write an audit event) and the name to identify who
/// holds the Draft. Both stay null for the recorder's own view and are never exposed to field
/// users (who receive only the <c>OpenDraftHeldByOther</c> boolean).</para>
/// <para><see cref="DefaultPerformedByAccountUserId"/> and <see cref="DefaultPerformerDisplayName"/>
/// carry the Draft's persisted ticket-default performer (ADR-494 D2, 4c-i). They are the
/// resume-after-reload signal the capture composer gates its whole add region on: a server-persisted
/// office-transcription default must reappear on the client after a reload, not silently vanish.
/// Both are null while no default is set. Populated for the recorder's own view <i>and</i> the
/// Owner/Admin read-only view — this is not recorder identity, it is which technician the work is
/// attributed to.</para></summary>
public sealed record ActualWorkOpenDraftEntry(
    Guid Id, ActualWorkStatus Status, ActualWorkOutcome? Outcome, string? CompletionNote,
    DateTime? SubmittedAtUtc, Guid ConcurrencyVersion, bool IsRecorder,
    Guid? RecorderAccountUserId, string? RecorderDisplayName,
    Guid? DefaultPerformedByAccountUserId, string? DefaultPerformerDisplayName,
    string? VisitNote,
    IReadOnlyList<ActualWorkLineHistoryEntry> Lines);

/// <summary>A submitted, immutable visit — no <c>ConcurrencyVersion</c>, since nothing here is ever
/// mutated through this read.</summary>
public sealed record ActualWorkSubmittedVisitEntry(
    Guid Id, ActualWorkStatus Status, ActualWorkOutcome? Outcome, string? CompletionNote,
    DateTime? SubmittedAtUtc, string? VisitNote, IReadOnlyList<ActualWorkLineHistoryEntry> Lines);

/// <summary><see cref="CanCaptureActualWork"/> disambiguates a null <see cref="OpenDraft"/>: it is
/// true whenever the caller has both <c>RequestsOperate</c> and <c>ActualWorkCapture</c> (GAP-055 —
/// no longer tied to active-Responsible participation; whether or not they have opened a Draft
/// yet, and whether or not someone else already holds the open Draft), false for any other visible
/// caller (e.g. a Viewer). The field capture UI must gate its "Record completed work" action on
/// this, not on <c>OpenDraft is null</c> alone — a qualified caller who is not the current recorder
/// still sees this as true, and a create call under those conditions returns the same opaque
/// <c>DraftAlreadyOpenForRequest</c> conflict as any other caller (GAP-055's create-conflict stays
/// opaque; no recorder identity is leaked there).
/// <para><see cref="OpenDraftHeldByOther"/> is the presence-only signal for that last case: true
/// when an open Draft exists but the caller is neither its recorder nor an Owner/Admin, so the
/// field UI can show a non-actionable "another team member is recording this visit" state instead
/// of an entry point that only fails on create. It never carries recorder identity, and it is
/// mutually exclusive with a populated <see cref="OpenDraft"/> (recorder and Owner/Admin get the
/// Draft itself; everyone else gets only this boolean).</para></summary>
public sealed record ActualWorkHistoryResult(
    bool CanCaptureActualWork, ActualWorkOpenDraftEntry? OpenDraft, bool OpenDraftHeldByOther,
    IReadOnlyList<ActualWorkSubmittedVisitEntry> SubmittedVisits);

/// <summary>
/// API-facing read orchestration for Actual Work visit history (Batch 5a, build-log/129):
/// submitted visits are visible to any normally request-visible caller; the open Draft is visible
/// to its current recorder (resume-after-reload — a bare create call cannot otherwise discover an
/// already-open Draft) and, read-only, to Owner/Admin (GAP-055 — grounds for a recorder-transfer
/// decision without mutate rights). Gate 2 is the Price Book entitlement and gate 3 is
/// <c>RequestsView</c>; capture permissions are separately required for the Draft affordance.
/// Gate 1 is read-only (Blocked-only denies — <c>ReadOnly</c>, e.g. OffSeason, may still read),
/// matching <see cref="ProposedScopeReadApiService"/>'s read policy rather than the mutation gate.
/// </summary>
public sealed class ActualWorkHistoryReadApiService(
    IActualWorkPersistence persistence,
    IKeepRequestDetailPersistence requestPersistence,
    IKeepRequestOperatePersistence operatePersistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<ActualWorkHistoryResult>> GetHistoryForRequestAsync(Guid requestId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ActualWorkHistoryResult>.Failure(gate.Error);

        var request = await requestPersistence.GetRequestAsync(
            requestId, currentUser.AccountId, currentUser.UserId, gate.Value.Scope, ct);
        if (request is null)
            return Result<ActualWorkHistoryResult>.Failure(KeepRequestErrors.NotFound);

        var canCaptureActualWork =
            userAccessPolicy.IsPermitted(
                gate.Value.RoleSnapshot.Role, gate.Value.RoleSnapshot.MembershipStatus,
                gate.Value.AccountPurpose, PermissionKeys.Keep.RequestsOperate) &&
            userAccessPolicy.IsPermitted(
                gate.Value.RoleSnapshot.Role, gate.Value.RoleSnapshot.MembershipStatus,
                gate.Value.AccountPurpose, PermissionKeys.Keep.ActualWorkCapture);

        var isOwnerOrAdmin = gate.Value.RoleSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin;

        ActualWork? draft = null;
        var openDraftHeldByOther = false;
        var draftVisibleToCaller = false;
        Guid? recorderIdForRecovery = null;
        if (canCaptureActualWork || isOwnerOrAdmin)
        {
            draft = await persistence.GetOpenDraftForRequestAsync(currentUser.AccountId, requestId, ct);
            if (draft is not null)
            {
                var isRecorder = draft.RecorderAccountUserId == currentUser.UserId;
                if (isRecorder)
                    draftVisibleToCaller = true;
                else if (isOwnerOrAdmin)
                {
                    draftVisibleToCaller = true;
                    recorderIdForRecovery = draft.RecorderAccountUserId;
                }
                else
                    openDraftHeldByOther = true;
            }
        }

        var submittedVisits = await persistence.GetSubmittedVisitsForRequestAsync(currentUser.AccountId, requestId, ct);

        // Per-distinct-id memoized performer-name resolution (locked 2026-08-29): one
        // GetActorDisplayNameAsync call per distinct id across the visible draft and every
        // submitted visit; visits carry 1–2 distinct performers. No batch seam method.
        var performerNames = new Dictionary<Guid, string?>();
        var idsToResolve = new HashSet<Guid>();
        if (draftVisibleToCaller && draft is not null)
        {
            foreach (var line in draft.Lines)
                idsToResolve.Add(line.PerformedByAccountUserId);
            if (draft.DefaultPerformedByAccountUserId is { } d)
                idsToResolve.Add(d);
        }
        if (recorderIdForRecovery is { } r)
            idsToResolve.Add(r);
        foreach (var visit in submittedVisits)
            foreach (var line in visit.Lines)
                idsToResolve.Add(line.PerformedByAccountUserId);
        foreach (var id in idsToResolve)
            performerNames[id] = await operatePersistence.GetActorDisplayNameAsync(id, ct);

        ActualWorkOpenDraftEntry? openDraft = null;
        if (draftVisibleToCaller && draft is not null)
        {
            var defaultPerformerDisplayName = draft.DefaultPerformedByAccountUserId is { } defaultPerformerId
                ? performerNames.GetValueOrDefault(defaultPerformerId)
                : null;
            openDraft = recorderIdForRecovery is { } recorderId
                ? ToOpenDraftEntry(
                    draft, isRecorder: false, recorderId, performerNames.GetValueOrDefault(recorderId),
                    defaultPerformerDisplayName, performerNames)
                : ToOpenDraftEntry(
                    draft, isRecorder: true, recorderAccountUserId: null, recorderDisplayName: null,
                    defaultPerformerDisplayName, performerNames);
        }

        return Result<ActualWorkHistoryResult>.Success(
            new ActualWorkHistoryResult(
                canCaptureActualWork, openDraft, openDraftHeldByOther,
                submittedVisits.Select(v => ToSubmittedVisitEntry(v, performerNames)).ToArray()));
    }

    private static ActualWorkOpenDraftEntry ToOpenDraftEntry(
        ActualWork visit, bool isRecorder, Guid? recorderAccountUserId, string? recorderDisplayName,
        string? defaultPerformerDisplayName, IReadOnlyDictionary<Guid, string?> performerNames) => new(
        visit.Id, visit.Status, visit.Outcome, visit.CompletionNote, visit.SubmittedAtUtc,
        visit.ConcurrencyVersion, isRecorder, recorderAccountUserId, recorderDisplayName,
        visit.DefaultPerformedByAccountUserId, defaultPerformerDisplayName, visit.VisitNote,
        ToLineEntries(visit, performerNames));

    private static ActualWorkSubmittedVisitEntry ToSubmittedVisitEntry(
        ActualWork visit, IReadOnlyDictionary<Guid, string?> performerNames) => new(
        visit.Id, visit.Status, visit.Outcome, visit.CompletionNote, visit.SubmittedAtUtc, visit.VisitNote,
        ToLineEntries(visit, performerNames));

    /// <summary><c>Include(Lines)</c> does not guarantee collection order, so capture order is
    /// made explicit here rather than left to reload-time happenstance: <c>CreatedAtUtc ASC, Id
    /// ASC</c>, matching the order lines were actually added in.</summary>
    private static IReadOnlyList<ActualWorkLineHistoryEntry> ToLineEntries(
        ActualWork visit, IReadOnlyDictionary<Guid, string?> performerNames) =>
        visit.Lines
            .OrderBy(l => l.CreatedAtUtc)
            .ThenBy(l => l.Id)
            .Select(l => new ActualWorkLineHistoryEntry(
                l.Id, l.DisplayNameSnapshot, l.UnitOfMeasureSnapshot, l.ActualQuantity, l.Note,
                l.PerformedByAccountUserId, performerNames.GetValueOrDefault(l.PerformedByAccountUserId)))
            .ToArray();

    /// <summary>Returns the request-visibility scope and resolved authorization facts on success.
    /// Gate 1 is read-only — see class remarks.</summary>
    private async Task<Result<ActualWorkHistoryAuthorization>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<ActualWorkHistoryAuthorization>.Failure(Unauthorized);

        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<ActualWorkHistoryAuthorization>.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<ActualWorkHistoryAuthorization>.Failure(Forbidden);

        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result<ActualWorkHistoryAuthorization>.Failure(Forbidden);

        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result<ActualWorkHistoryAuthorization>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsView))
            return Result<ActualWorkHistoryAuthorization>.Failure(Forbidden);

        KeepRequestVisibilityScope scope;
        switch (roleSnapshot.Role)
        {
            case AccountUserRole.Owner:
            case AccountUserRole.Admin:
            case AccountUserRole.Viewer:
                scope = KeepRequestVisibilityScope.AccountWide;
                break;
            case AccountUserRole.Operator:
                scope = KeepRequestVisibilityScope.MyWork;
                break;
            default:
                return Result<ActualWorkHistoryAuthorization>.Failure(Forbidden);
        }

        return Result<ActualWorkHistoryAuthorization>.Success(
            new ActualWorkHistoryAuthorization(scope, roleSnapshot, accountSnapshot.Purpose));
    }

    private sealed record ActualWorkHistoryAuthorization(
        KeepRequestVisibilityScope Scope,
        FoundationAccountUserRoleSnapshot RoleSnapshot,
        AccountPurpose AccountPurpose);
}
