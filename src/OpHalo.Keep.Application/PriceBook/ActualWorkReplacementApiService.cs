using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// API-facing orchestration for ADR-494 D6 replacement-copy: an Owner/Admin corrects a still-editable
/// (pre-export) submitted Actual Work visit by creating an editable Draft successor built from it,
/// while the source is superseded and retained with its successor link. Mirrors
/// <see cref="ActualWorkReviewApiService"/>'s Owner/Admin office gate exactly (RequestsOperate + the
/// Price Book entitlement + AccountingManage + an explicit Owner/Admin role check — deliberately not
/// <c>ActualWorkCapture</c>). Owns no transaction: this service composes auth, checks the
/// no-open-Draft precondition, loads the source, and <b>constructs the successor aggregate from it</b>
/// (ADR-494 D6); <see cref="IActualWorkSupersessionPersistence"/> owns the entire atomic
/// supersede-source + insert-successor + signal-reconcile operation.
///
/// <para>Copy fidelity (ADR-494 D6): every line's factual + performer + snapshot fields, plus
/// <c>VisitNote</c>, and for a zero-line source the editable <c>Outcome</c> + <c>CompletionNote</c>.
/// No financial-resolution, disposition, or review rows are copied. Line performers are copied
/// <b>verbatim</b> — performer eligibility is not re-validated here, so a visit whose performer has
/// since left the account stays correctable; the eligibility gate applies only to <em>new</em> lines
/// added to the Draft afterward via the normal draft-edit path.</para>
/// </summary>
public sealed class ActualWorkReplacementApiService(
    IActualWorkSupersessionPersistence supersessionPersistence,
    IActualWorkPersistence actualWorkPersistence,
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

    /// <summary>Supersedes <paramref name="sourceActualWorkId"/> (guarded by
    /// <paramref name="expectedSourceVersion"/>) and returns the id of the newly created Draft
    /// successor. <paramref name="reason"/> is the truthful correction reason recorded on the source.</summary>
    public async Task<Result<Guid>> CreateReplacementAsync(
        Guid sourceActualWorkId, Guid expectedSourceVersion, string reason, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        var source = await actualWorkPersistence.GetByIdAsync(currentUser.AccountId, sourceActualWorkId, ct);
        if (source is null)
            return Result<Guid>.Failure(ActualWorkErrors.NotFound);

        // Friendly precondition: the request may hold at most one open Draft (ADR-494 D6). The
        // persistence seam's open-Draft partial unique index stays the actual race guard.
        var openDraft = await actualWorkPersistence.GetOpenDraftForRequestAsync(
            currentUser.AccountId, source.RequestId, ct);
        if (openDraft is not null)
            return Result<Guid>.Failure(ActualWorkErrors.DraftAlreadyOpenForRequest);

        var successorResult = BuildSuccessor(source);
        if (successorResult.IsFailure)
            return Result<Guid>.Failure(successorResult.Error);

        var outcome = await supersessionPersistence.SupersedeAsync(
            currentUser.AccountId, sourceActualWorkId, expectedSourceVersion, successorResult.Value,
            currentUser.UserId, reason, clock.UtcNow, ct);

        return outcome.Result switch
        {
            ActualWorkSupersessionResult.Committed => Result<Guid>.Success(outcome.SuccessorId!.Value),
            ActualWorkSupersessionResult.NotFound => Result<Guid>.Failure(ActualWorkErrors.NotFound),
            ActualWorkSupersessionResult.SourceNotSubmitted => Result<Guid>.Failure(ActualWorkErrors.NotSubmitted),
            ActualWorkSupersessionResult.SourceAlreadySuperseded => Result<Guid>.Failure(ActualWorkErrors.AlreadySuperseded),
            ActualWorkSupersessionResult.ReasonRequired => Result<Guid>.Failure(ActualWorkErrors.SupersessionReasonRequired),
            ActualWorkSupersessionResult.ReasonTooLong => Result<Guid>.Failure(ActualWorkErrors.SupersessionReasonTooLong),
            ActualWorkSupersessionResult.DraftAlreadyOpenForRequest =>
                Result<Guid>.Failure(ActualWorkErrors.DraftAlreadyOpenForRequest),
            _ => Result<Guid>.Failure(ActualWorkErrors.VersionMismatch),
        };
    }

    /// <summary>ADR-494 D6: builds the Draft successor aggregate from the loaded source — the acting
    /// user is both author and recorder. A copy step can only fail if the source itself violated a
    /// line/note invariant that has since tightened; that error is surfaced unchanged.</summary>
    private Result<ActualWork> BuildSuccessor(ActualWork source)
    {
        var createResult = ActualWork.Create(source.AccountId, source.RequestId, currentUser.UserId);
        if (createResult.IsFailure)
            return createResult;

        var successor = createResult.Value;

        foreach (var line in source.Lines)
        {
            var addResult = successor.AddLine(
                line.CatalogItemId,
                line.PriceBookVersionLineId,
                line.DisplayNameSnapshot,
                line.UnitOfMeasureSnapshot,
                line.ActualQuantity,
                line.SellPriceSnapshot,
                line.StandardExpectedDirectCostSnapshot,
                line.Note,
                line.CommercialBaselineSourceLineId,
                currentUser.UserId,
                line.PerformedByAccountUserId);
            if (addResult.IsFailure)
                return Result<ActualWork>.Failure(addResult.Error);
        }

        if (source.VisitNote is not null)
        {
            var noteResult = successor.SetVisitNote(source.VisitNote);
            if (noteResult.IsFailure)
                return Result<ActualWork>.Failure(noteResult.Error);
        }

        // Zero-line source only (ADR-494 D6): carry the editable disposition forward so the
        // replacement starts from what was recorded, not blank. Outcome is non-null on any zero-line
        // submitted visit, but skip defensively rather than dereference a violated invariant.
        if (source.Lines.Count == 0 && source.Outcome is not null)
        {
            var dispositionResult = successor.SetZeroLineDisposition(source.Outcome.Value, source.CompletionNote);
            if (dispositionResult.IsFailure)
                return Result<ActualWork>.Failure(dispositionResult.Error);
        }

        return Result<ActualWork>.Success(successor);
    }

    /// <summary>Owner/Admin office gate — identical composition to
    /// <see cref="ActualWorkReviewApiService"/>'s: authenticated, non-blocked/read-only account
    /// access, the Price Book entitlement, <c>RequestsOperate</c>, <c>AccountingManage</c>, and an
    /// explicit Owner/Admin role check. No <c>ActualWorkCapture</c> — replacement is an office
    /// correction authority, never the field recorder's own action.</summary>
    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

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
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result.Failure(Forbidden);

        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result.Failure(Forbidden);

        if (roleSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.AccountingManage))
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
