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

/// <summary>The caller's own open Draft, only returned when they are the request's active
/// Responsible recorder (build-log/129). Carries <see cref="ConcurrencyVersion"/> because it is the
/// resume-after-reload projection the capture composer edits against.</summary>
public sealed record ActualWorkOpenDraftEntry(
    Guid Id, ActualWorkStatus Status, ActualWorkOutcome? Outcome, string? CompletionNote,
    DateTime? SubmittedAtUtc, Guid ConcurrencyVersion, IReadOnlyList<ActualWorkLineHistoryEntry> Lines);

/// <summary>A submitted, immutable visit — no <c>ConcurrencyVersion</c>, since nothing here is ever
/// mutated through this read.</summary>
public sealed record ActualWorkSubmittedVisitEntry(
    Guid Id, ActualWorkStatus Status, ActualWorkOutcome? Outcome, string? CompletionNote,
    DateTime? SubmittedAtUtc, IReadOnlyList<ActualWorkLineHistoryEntry> Lines);

/// <summary><see cref="CanCaptureActualWork"/> disambiguates a null <see cref="OpenDraft"/>: it is
/// true only when the caller is the request's active Responsible recorder and has both
/// <c>RequestsOperate</c> and <c>ActualWorkCapture</c> (whether or not they have opened a Draft
/// yet), false for any other visible caller (e.g. a Viewer or watching Operator/Owner/Admin). The
/// field capture UI must gate its "Record completed work" action on this, not on <c>OpenDraft is
/// null</c> alone, or a caller would see an action that fails 404/403 on create.</summary>
public sealed record ActualWorkHistoryResult(
    bool CanCaptureActualWork, ActualWorkOpenDraftEntry? OpenDraft, IReadOnlyList<ActualWorkSubmittedVisitEntry> SubmittedVisits);

/// <summary>
/// API-facing read orchestration for Actual Work visit history (Batch 5a, build-log/129):
/// submitted visits are visible to any normally request-visible caller; the open Draft is visible
/// only to the request's active-Responsible recorder (resume-after-reload — a bare create call
/// cannot otherwise discover an already-open Draft). Gate 2 is the Price Book entitlement and
/// gate 3 is <c>RequestsView</c>; capture permissions are separately required for the Draft
/// affordance. Gate 1 is read-only (Blocked-only denies — <c>ReadOnly</c>, e.g. OffSeason, may
/// still read), matching <see cref="ProposedScopeReadApiService"/>'s read policy rather than the
/// mutation gate.
/// </summary>
public sealed class ActualWorkHistoryReadApiService(
    IActualWorkPersistence persistence,
    IKeepRequestDetailPersistence requestPersistence,
    IActiveResponsibleCheck responsibleCheck,
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

        ActualWorkOpenDraftEntry? openDraft = null;
        var isResponsible = await responsibleCheck.IsActiveResponsibleAsync(
            requestId, currentUser.AccountId, currentUser.UserId, gate.Value.Scope, ct);
        var canCaptureActualWork = isResponsible &&
            userAccessPolicy.IsPermitted(
                gate.Value.RoleSnapshot.Role, gate.Value.RoleSnapshot.MembershipStatus,
                gate.Value.AccountPurpose, PermissionKeys.Keep.RequestsOperate) &&
            userAccessPolicy.IsPermitted(
                gate.Value.RoleSnapshot.Role, gate.Value.RoleSnapshot.MembershipStatus,
                gate.Value.AccountPurpose, PermissionKeys.Keep.ActualWorkCapture);
        if (canCaptureActualWork)
        {
            var draft = await persistence.GetOpenDraftForRequestAsync(currentUser.AccountId, requestId, ct);
            if (draft is not null)
                openDraft = ToOpenDraftEntry(draft);
        }

        var submittedVisits = await persistence.GetSubmittedVisitsForRequestAsync(currentUser.AccountId, requestId, ct);

        return Result<ActualWorkHistoryResult>.Success(
            new ActualWorkHistoryResult(canCaptureActualWork, openDraft, submittedVisits.Select(ToSubmittedVisitEntry).ToArray()));
    }

    private static ActualWorkOpenDraftEntry ToOpenDraftEntry(ActualWork visit) => new(
        visit.Id, visit.Status, visit.Outcome, visit.CompletionNote, visit.SubmittedAtUtc,
        visit.ConcurrencyVersion, ToLineEntries(visit));

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
