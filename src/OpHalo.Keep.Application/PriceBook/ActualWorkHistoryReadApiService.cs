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
    Guid Id, string DisplayNameSnapshot, string? UnitOfMeasureSnapshot, decimal ActualQuantity, string? Note);

/// <summary>The open Draft for this request, returned to its current recorder (GAP-055) for
/// resume-after-reload editing, or to Owner/Admin read-only so they have grounds to decide on a
/// recorder transfer even when they are not the recorder. <see cref="IsRecorder"/> disambiguates
/// which case this is: false means the composer must render read-only, never offer the mutation
/// actions a recorder gets. Carries <see cref="ConcurrencyVersion"/> because it is the
/// resume-after-reload projection the capture composer edits against.</summary>
public sealed record ActualWorkOpenDraftEntry(
    Guid Id, ActualWorkStatus Status, ActualWorkOutcome? Outcome, string? CompletionNote,
    DateTime? SubmittedAtUtc, Guid ConcurrencyVersion, bool IsRecorder,
    IReadOnlyList<ActualWorkLineHistoryEntry> Lines);

/// <summary>A submitted, immutable visit — no <c>ConcurrencyVersion</c>, since nothing here is ever
/// mutated through this read.</summary>
public sealed record ActualWorkSubmittedVisitEntry(
    Guid Id, ActualWorkStatus Status, ActualWorkOutcome? Outcome, string? CompletionNote,
    DateTime? SubmittedAtUtc, IReadOnlyList<ActualWorkLineHistoryEntry> Lines);

/// <summary><see cref="CanCaptureActualWork"/> disambiguates a null <see cref="OpenDraft"/>: it is
/// true whenever the caller has both <c>RequestsOperate</c> and <c>ActualWorkCapture</c> (GAP-055 —
/// no longer tied to active-Responsible participation; whether or not they have opened a Draft
/// yet, and whether or not someone else already holds the open Draft), false for any other visible
/// caller (e.g. a Viewer). The field capture UI must gate its "Record completed work" action on
/// this, not on <c>OpenDraft is null</c> alone — a qualified caller who is not the current recorder
/// still sees this as true, and a create call under those conditions returns the same opaque
/// <c>DraftAlreadyOpenForRequest</c> conflict as any other caller (GAP-055's create-conflict stays
/// opaque; no recorder identity is leaked there).</summary>
public sealed record ActualWorkHistoryResult(
    bool CanCaptureActualWork, ActualWorkOpenDraftEntry? OpenDraft, IReadOnlyList<ActualWorkSubmittedVisitEntry> SubmittedVisits);

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

        ActualWorkOpenDraftEntry? openDraft = null;
        if (canCaptureActualWork || isOwnerOrAdmin)
        {
            var draft = await persistence.GetOpenDraftForRequestAsync(currentUser.AccountId, requestId, ct);
            if (draft is not null)
            {
                var isRecorder = draft.RecorderAccountUserId == currentUser.UserId;
                if (isRecorder || isOwnerOrAdmin)
                    openDraft = ToOpenDraftEntry(draft, isRecorder);
            }
        }

        var submittedVisits = await persistence.GetSubmittedVisitsForRequestAsync(currentUser.AccountId, requestId, ct);

        return Result<ActualWorkHistoryResult>.Success(
            new ActualWorkHistoryResult(canCaptureActualWork, openDraft, submittedVisits.Select(ToSubmittedVisitEntry).ToArray()));
    }

    private static ActualWorkOpenDraftEntry ToOpenDraftEntry(ActualWork visit, bool isRecorder) => new(
        visit.Id, visit.Status, visit.Outcome, visit.CompletionNote, visit.SubmittedAtUtc,
        visit.ConcurrencyVersion, isRecorder, ToLineEntries(visit));

    private static ActualWorkSubmittedVisitEntry ToSubmittedVisitEntry(ActualWork visit) => new(
        visit.Id, visit.Status, visit.Outcome, visit.CompletionNote, visit.SubmittedAtUtc, ToLineEntries(visit));

    /// <summary><c>Include(Lines)</c> does not guarantee collection order, so capture order is
    /// made explicit here rather than left to reload-time happenstance: <c>CreatedAtUtc ASC, Id
    /// ASC</c>, matching the order lines were actually added in.</summary>
    private static IReadOnlyList<ActualWorkLineHistoryEntry> ToLineEntries(ActualWork visit) =>
        visit.Lines
            .OrderBy(l => l.CreatedAtUtc)
            .ThenBy(l => l.Id)
            .Select(l => new ActualWorkLineHistoryEntry(l.Id, l.DisplayNameSnapshot, l.UnitOfMeasureSnapshot, l.ActualQuantity, l.Note))
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
