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

/// <summary>Either <see cref="CatalogItemId"/> (a known catalog item, price-book snapshot resolved
/// server-side from its current Price Book version-line) or <see cref="OffCatalogDescription"/> (a
/// custom line) — never both. No caller-supplied display/price fields: same "never trust a caller
/// snapshot" discipline as ProposedScope's field-select (build-log/118).</summary>
public sealed record AddActualWorkLineApiCommand(
    Guid? CatalogItemId,
    string? OffCatalogDescription,
    decimal ActualQuantity,
    string? Note,
    // ADR-494 D2 (4c-i-b): an optional explicit per-line performer. Null → the line inherits the
    // persisted ticket default (frozen at selection, never revalidated here); with neither the
    // domain returns PerformerRequired. A non-null value is revalidated server-side against the
    // account membership snapshot exactly as the ticket default is.
    Guid? PerformedByAccountUserId = null);

/// <summary>The new line's id plus the parent visit's post-mutation ConcurrencyVersion — same
/// contract-detail reasoning as ProposedScope's <c>AddProposedScopeLineResult</c>: an add spends the
/// parent's token, so the caller needs the new value to chain the next sequential edit.</summary>
public sealed record AddActualWorkLineResult(Guid LineId, Guid ActualWorkConcurrencyVersion);

/// <summary>Build-log/129's 5d-i preflight lock: <see cref="IncludedOptionalItemIds"/> names the
/// assembly's optional <c>OfferingAssemblyItem</c> ids to include — optional items default out, an
/// empty list means none of them. Required items and the primary item are always included.</summary>
public sealed record ExpandActualWorkAssemblyApiCommand(
    Guid OfferingAssemblyId, IReadOnlyList<Guid> IncludedOptionalItemIds);

/// <summary><see cref="SkippedCatalogItemIds"/> lists every candidate already present on the Draft
/// (skip-and-report) — the expansion still succeeds for the remaining components.</summary>
public sealed record ExpandActualWorkAssemblyResult(
    IReadOnlyList<Guid> LineIds, IReadOnlyList<Guid> SkippedCatalogItemIds, Guid ActualWorkConcurrencyVersion);

/// <summary>GAP-055: an Owner/Admin transfer must always state <see cref="Reason"/>, for the
/// immutable <c>ActualWorkDraftRecorderTransferred</c> audit record. A recorder-initiated hand-off
/// (BL136 Slice 4d) ignores any supplied <see cref="Reason"/> and records
/// <see cref="ActualWorkDraftApiService.RecorderInitiatedHandoffReason"/> instead.</summary>
public sealed record TransferActualWorkDraftRecorderApiCommand(Guid NewRecorderAccountUserId, string Reason);

/// <summary>
/// API-facing orchestration for Draft create/add-line/update-line/remove-line/discard/recorder-
/// transfer (ADR-487, build-log/129, Batch 3; GAP-055 for transfer) — the single three-gate owner
/// for Actual Work draft mutations: <c>RequestsOperate</c> + Price Book entitlement (ADR-462) +
/// <c>ActualWorkCapture</c>, gate composition mirroring <see cref="ProposedScopeApiService"/>
/// exactly. First-recorder ownership (GAP-055, superseding the active-Responsible-only recorder
/// rule): <see cref="CreateAsync"/> only requires the request to be visible under the caller's
/// row-authorization scope, not active Responsible participation, and sets the caller as the
/// Draft's <c>RecorderAccountUserId</c>. Every subsequent mutation instead checks that the caller
/// is still that current recorder — a plain field comparison, not a Responsible-participation
/// lookup — except <see cref="TransferRecorderAsync"/>, which is Owner/Admin-only regardless of
/// current recorder ownership. Submitted visits are immutable; every mutation here rejects a
/// non-Draft visit via <see cref="ActualWorkErrors.NotDraft"/> from the domain aggregate itself.
/// </summary>
public sealed class ActualWorkDraftApiService(
    IActualWorkPersistence persistence,
    ICatalogReadPersistence catalogPersistence,
    IKeepRequestOperatePersistence requestOperatePersistence,
    IActualWorkAssemblyExpansionPersistence assemblyExpansionPersistence,
    SubmitActualWorkService submitService,
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

    public async Task<Result<ActualWork>> CreateAsync(
        Guid requestId, Guid? defaultPerformedByAccountUserId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ActualWork>.Failure(gate.Error);

        // ADR-494 D2: a caller-supplied ticket default is revalidated server-side. Command-shape
        // check — precedes loading the request aggregate; nothing is created on failure.
        if (defaultPerformedByAccountUserId is { } defaultPerformer)
        {
            var eligibility = await ValidateSuppliedPerformerAsync(defaultPerformer, gate.Value.Purpose, ct);
            if (eligibility.IsFailure)
                return Result<ActualWork>.Failure(eligibility.Error);
        }

        var request = await requestOperatePersistence.GetVisibleRequestForUpdateAsync(
            requestId, currentUser.AccountId, currentUser.UserId, gate.Value.Scope, ct);
        if (request is null)
            return Result<ActualWork>.Failure(KeepRequestErrors.NotFound);

        var createResult = ActualWork.Create(
            currentUser.AccountId, requestId, currentUser.UserId, defaultPerformedByAccountUserId);
        if (createResult.IsFailure)
            return createResult;

        var commitResult = await persistence.AddAsync(createResult.Value, ct);
        return commitResult switch
        {
            ActualWorkCommitResult.Committed => Result<ActualWork>.Success(createResult.Value),
            ActualWorkCommitResult.DraftAlreadyOpenForRequest =>
                Result<ActualWork>.Failure(ActualWorkErrors.DraftAlreadyOpenForRequest),
            _ => throw new InvalidOperationException($"Unexpected commit result: {commitResult}"),
        };
    }

    public async Task<Result<AddActualWorkLineResult>> AddLineAsync(
        Guid actualWorkId, AddActualWorkLineApiCommand command, Guid expectedVersion, CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<AddActualWorkLineResult>.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<AddActualWorkLineResult>.Failure(ActualWorkErrors.VersionMismatch);

        // ADR-494 D2: an explicit per-line performer is revalidated server-side before any mutation
        // or commit, so an ineligible id never rotates the version. An inherited ticket default is
        // NOT rechecked here — it is frozen at selection (Christian, 2026-08-29).
        if (command.PerformedByAccountUserId is { } explicitPerformer)
        {
            var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
            if (accountSnapshot is null)
                return Result<AddActualWorkLineResult>.Failure(Forbidden);
            var eligibility = await ValidateSuppliedPerformerAsync(explicitPerformer, accountSnapshot.Purpose, ct);
            if (eligibility.IsFailure)
                return Result<AddActualWorkLineResult>.Failure(eligibility.Error);
        }

        string displayNameSnapshot;
        string? unitOfMeasureSnapshot = null;
        Guid? catalogItemId = null;
        Guid? priceBookVersionLineId = null;
        decimal? sellPriceSnapshot = null;
        decimal? standardExpectedDirectCostSnapshot = null;

        if (command.CatalogItemId is not null)
        {
            if (!string.IsNullOrWhiteSpace(command.OffCatalogDescription))
                return Result<AddActualWorkLineResult>.Failure(ActualWorkErrors.LineOffCatalogDescriptionWithCatalogItem);

            if (command.CatalogItemId.Value == Guid.Empty)
                return Result<AddActualWorkLineResult>.Failure(ActualWorkErrors.LineCatalogItemIdEmpty);

            var item = await catalogPersistence.GetItemDetailAsync(currentUser.AccountId, command.CatalogItemId.Value, ct);
            if (item is null)
                return Result<AddActualWorkLineResult>.Failure(ActualWorkErrors.LineCatalogItemNotFound);

            catalogItemId = item.Item.Id;
            if (item.CurrentPriceLine is not null)
            {
                priceBookVersionLineId = item.CurrentPriceLine.Id;
                unitOfMeasureSnapshot = item.CurrentPriceLine.UnitOfMeasureSnapshot;
                sellPriceSnapshot = item.CurrentPriceLine.SellPriceSnapshot;
                standardExpectedDirectCostSnapshot = item.CurrentPriceLine.CostSnapshot;
                displayNameSnapshot = item.CurrentPriceLine.DisplayNameSnapshot;
            }
            else
            {
                unitOfMeasureSnapshot = item.Item.UnitOfMeasure;
                displayNameSnapshot = item.Item.DisplayName;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(command.OffCatalogDescription))
                return Result<AddActualWorkLineResult>.Failure(ActualWorkErrors.LineDisplayNameSnapshotRequired);
            displayNameSnapshot = command.OffCatalogDescription.Trim();
        }

        // ADR-494 D2: an explicit per-line performer (validated above) wins; otherwise the line
        // inherits the persisted ticket default. The domain returns PerformerRequired when both are
        // absent — the gate that forces the office transcriber to pick a technician first.
        var addResult = actualWork.AddLine(
            catalogItemId, priceBookVersionLineId, displayNameSnapshot, unitOfMeasureSnapshot,
            command.ActualQuantity, sellPriceSnapshot, standardExpectedDirectCostSnapshot,
            command.Note, commercialBaselineSourceLineId: null, currentUser.UserId,
            performedByAccountUserId: command.PerformedByAccountUserId ?? actualWork.DefaultPerformedByAccountUserId);
        if (addResult.IsFailure)
            return Result<AddActualWorkLineResult>.Failure(addResult.Error);

        var commitResult = await persistence.CommitAsync(actualWork, ct);
        return commitResult switch
        {
            ActualWorkCommitResult.Committed => Result<AddActualWorkLineResult>.Success(
                new AddActualWorkLineResult(addResult.Value.Id, actualWork.ConcurrencyVersion)),
            ActualWorkCommitResult.ConcurrencyConflict =>
                Result<AddActualWorkLineResult>.Failure(ActualWorkErrors.VersionMismatch),
            _ => throw new InvalidOperationException($"Unexpected commit result: {commitResult}"),
        };
    }

    /// <summary>
    /// Build-log/129's 5d-i preflight lock: thin passthrough to
    /// <see cref="IActualWorkAssemblyExpansionPersistence"/>, which owns the entire lock/recheck/
    /// append transaction. Deliberately performs no row read of its own — the persistence seam's
    /// locked load of the Draft must be the first tracked load of that aggregate anywhere in the
    /// call path, so the recorder-ownership/version/status checks (GAP-055) all happen inside the
    /// transaction against the just-locked row, not against a separately tracked pre-check load.
    /// </summary>
    public async Task<Result<ExpandActualWorkAssemblyResult>> ExpandAssemblyAsync(
        Guid actualWorkId, ExpandActualWorkAssemblyApiCommand command, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ExpandActualWorkAssemblyResult>.Failure(gate.Error);

        var outcome = await assemblyExpansionPersistence.ExpandAsync(
            currentUser.AccountId, actualWorkId, expectedVersion, command.OfferingAssemblyId,
            command.IncludedOptionalItemIds, currentUser.UserId, ct);

        return outcome.Result switch
        {
            ActualWorkExpandAssemblyResult.Committed => Result<ExpandActualWorkAssemblyResult>.Success(
                new ExpandActualWorkAssemblyResult(
                    outcome.LineIds!, outcome.SkippedCatalogItemIds!, outcome.ConcurrencyVersion!.Value)),
            ActualWorkExpandAssemblyResult.NotFound =>
                Result<ExpandActualWorkAssemblyResult>.Failure(ActualWorkErrors.NotFound),
            ActualWorkExpandAssemblyResult.NotRecorder =>
                Result<ExpandActualWorkAssemblyResult>.Failure(ActualWorkErrors.NotFound),
            ActualWorkExpandAssemblyResult.VersionMismatch =>
                Result<ExpandActualWorkAssemblyResult>.Failure(ActualWorkErrors.VersionMismatch),
            ActualWorkExpandAssemblyResult.NotDraft =>
                Result<ExpandActualWorkAssemblyResult>.Failure(ActualWorkErrors.NotDraft),
            ActualWorkExpandAssemblyResult.PerformerRequired =>
                Result<ExpandActualWorkAssemblyResult>.Failure(ActualWorkErrors.PerformerRequired),
            ActualWorkExpandAssemblyResult.AssemblyNotFound =>
                Result<ExpandActualWorkAssemblyResult>.Failure(OfferingAssemblyErrors.NotFound),
            ActualWorkExpandAssemblyResult.AssemblyNotOperationallyEligible =>
                Result<ExpandActualWorkAssemblyResult>.Failure(ActualWorkErrors.ExpandAssemblyNotOperationallyEligible),
            ActualWorkExpandAssemblyResult.InvalidInclusion =>
                Result<ExpandActualWorkAssemblyResult>.Failure(ActualWorkErrors.ExpandInclusionItemInvalid),
            _ => throw new InvalidOperationException($"Unexpected expand result: {outcome.Result}"),
        };
    }

    public async Task<Result<Guid>> UpdateLineAsync(
        Guid actualWorkId, Guid lineId, decimal actualQuantity, string? note, Guid expectedVersion, CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(ActualWorkErrors.VersionMismatch);

        var updateResult = actualWork.UpdateLine(lineId, actualQuantity, note);
        if (updateResult.IsFailure)
            return Result<Guid>.Failure(updateResult.Error);

        return await CommitAsync(actualWork, ct);
    }

    public async Task<Result<Guid>> RemoveLineAsync(
        Guid actualWorkId, Guid lineId, Guid expectedVersion, CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(ActualWorkErrors.VersionMismatch);

        var removeResult = actualWork.RemoveLine(lineId);
        if (removeResult.IsFailure)
            return Result<Guid>.Failure(removeResult.Error);

        return await CommitAsync(actualWork, ct);
    }

    public async Task<Result> DiscardAsync(Guid actualWorkId, Guid expectedVersion, CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result.Failure(ActualWorkErrors.VersionMismatch);

        var commitResult = await persistence.DiscardAsync(actualWork, ct);
        return commitResult switch
        {
            ActualWorkCommitResult.Committed => Result.Success(),
            ActualWorkCommitResult.ConcurrencyConflict => Result.Failure(ActualWorkErrors.VersionMismatch),
            _ => throw new InvalidOperationException($"Unexpected commit result: {commitResult}"),
        };
    }

    /// <summary>
    /// Batch 4 (build-log/129). Reuses <see cref="AuthorizeAndLoadDraftAsync"/> for the three-gate
    /// auth + recorder-ownership row-authorization check (GAP-055) + Draft-status check — that load
    /// also supplies <c>Lines</c> for the zero-line pre-check below. Build Log 129 requires
    /// zero-line/outcome validation at both the domain and API boundaries:
    /// <see cref="SubmitActualWorkService"/> delegates straight to the atomic
    /// persistence seam, so this pre-check is duplicated here rather than solely relied upon inside
    /// that transaction — it fails fast with a 400 before ever opening the atomic submit
    /// transaction, while the persistence-layer domain call remains the authoritative, race-safe
    /// enforcement (a concurrent line add/remove between this load and the atomic submit is only
    /// caught there, via the same checks, returned as the matching
    /// <see cref="ActualWorkSubmissionResult"/>).
    /// </summary>
    public async Task<Result<Guid>> SubmitAsync(
        Guid actualWorkId, ActualWorkOutcome? outcome, string? completionNote, Guid expectedVersion, CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(ActualWorkErrors.VersionMismatch);

        if (outcome is not null && !Enum.IsDefined(outcome.Value))
            return Result<Guid>.Failure(ActualWorkErrors.InvalidOutcome);

        if (actualWork.Lines.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(completionNote))
                return Result<Guid>.Failure(ActualWorkErrors.ZeroLineCompletionNoteRequired);
            if (outcome is null)
                return Result<Guid>.Failure(ActualWorkErrors.ZeroLineOutcomeRequired);
        }

        return await submitService.SubmitAsync(
            currentUser.AccountId, actualWorkId, expectedVersion, outcome, completionNote, ct);
    }

    /// <summary>
    /// ADR-494 D2 (4c-i-b): recorder-only Draft mutation that sets or clears the visit's ticket
    /// default performer. Shares the ordinary <see cref="AuthorizeAndLoadDraftAsync"/> row-auth +
    /// Draft-status gate and the <c>X-Keep-ActualWork-Version</c> optimistic-concurrency protocol
    /// with the line editor. A non-null target is revalidated via
    /// <see cref="ValidateSuppliedPerformerAsync"/> before any mutation, so an ineligible id
    /// (<see cref="Guid.Empty"/> included) never rotates the version. Passing null clears the
    /// default; existing line performers are untouched (the domain method seeds new lines only).
    /// </summary>
    public async Task<Result<Guid>> SetDefaultPerformerAsync(
        Guid actualWorkId, Guid? performedByAccountUserId, Guid expectedVersion, CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(ActualWorkErrors.VersionMismatch);

        if (performedByAccountUserId is { } performer)
        {
            var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
            if (accountSnapshot is null)
                return Result<Guid>.Failure(Forbidden);
            var eligibility = await ValidateSuppliedPerformerAsync(performer, accountSnapshot.Purpose, ct);
            if (eligibility.IsFailure)
                return Result<Guid>.Failure(eligibility.Error);
        }

        var setResult = actualWork.SetDefaultPerformer(performedByAccountUserId);
        if (setResult.IsFailure)
            return Result<Guid>.Failure(setResult.Error);

        return await CommitAsync(actualWork, ct);
    }

    /// <summary>
    /// ADR-494 D5 (4c-ii): recorder-only Draft mutation that sets or clears the visit's free-text
    /// note. Shares the <see cref="AuthorizeAndLoadDraftAsync"/> row-auth + Draft-status gate and the
    /// <c>X-Keep-ActualWork-Version</c> optimistic-concurrency protocol with the line editor. The
    /// domain method (<see cref="ActualWork.SetVisitNote"/>) trims the value, treats blank as a
    /// clear, and rejects anything over 2,000 characters; a rejected value never rotates the version
    /// because the guard runs before any state change.
    /// </summary>
    public async Task<Result<Guid>> SetVisitNoteAsync(
        Guid actualWorkId, string? visitNote, Guid expectedVersion, CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(ActualWorkErrors.VersionMismatch);

        var setResult = actualWork.SetVisitNote(visitNote);
        if (setResult.IsFailure)
            return Result<Guid>.Failure(setResult.Error);

        return await CommitAsync(actualWork, ct);
    }

    /// <summary>
    /// ADR-494 D6 (4e-ii-c): recorder-only Draft mutation for the zero-line disposition copied
    /// onto a replacement Draft. This deliberately has the same authorization, Draft-state, and
    /// optimistic-concurrency contract as <see cref="SetVisitNoteAsync"/>; the aggregate remains
    /// the owner of outcome and completion-note validation.
    /// </summary>
    public async Task<Result<Guid>> SetZeroLineDispositionAsync(
        Guid actualWorkId, ActualWorkOutcome outcome, string? completionNote, Guid expectedVersion,
        CancellationToken ct)
    {
        var loadResult = await AuthorizeAndLoadDraftAsync(actualWorkId, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);
        var actualWork = loadResult.Value;

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(ActualWorkErrors.VersionMismatch);

        var setResult = actualWork.SetZeroLineDisposition(outcome, completionNote);
        if (setResult.IsFailure)
            return Result<Guid>.Failure(setResult.Error);

        return await CommitAsync(actualWork, ct);
    }

    /// <summary>System <c>Reason</c> written to the immutable transfer audit record when the current
    /// recorder hands their own Draft to the office (BL136 Slice 4d). A recorder-initiated hand-off
    /// never carries caller-supplied audit text; only the Owner/Admin path states its own reason.</summary>
    public const string RecorderInitiatedHandoffReason = "Handed off to the office by the field recorder.";

    /// <summary>
    /// GAP-055 + BL136 Slice 4d: reason-required recorder-ownership transfer of an unsubmitted Draft.
    /// Two callers are allowed: an Owner/Admin transferring any request's Draft (must state a reason),
    /// and the <b>current recorder</b> handing their own Draft to the office (fixed
    /// <see cref="RecorderInitiatedHandoffReason"/>; any caller-supplied reason is ignored). Every
    /// other caller — including a non-recorder who is not Owner/Admin — gets
    /// <see cref="ActualWorkErrors.NotFound"/>. Deliberately does not reuse
    /// <see cref="AuthorizeAndLoadDraftAsync"/> — that helper's row-authorization check requires the
    /// caller to already be the current recorder, which is exactly the constraint the Owner/Admin
    /// path must bypass. Instead this loads the Draft directly after the three-gate auth, applies the
    /// caller-shape guard, then delegates to the domain's <see cref="ActualWork.TransferRecorder"/>
    /// (Draft-only invariant) and commits the visit's <c>RecorderAccountUserId</c> change atomically
    /// with the immutable <see cref="ActualWorkDraftRecorderTransfer"/> audit record via
    /// <see cref="IActualWorkPersistence"/>'s transfer-aware <c>CommitAsync</c> overload.
    /// </summary>
    public async Task<Result<Guid>> TransferRecorderAsync(
        Guid actualWorkId, TransferActualWorkDraftRecorderApiCommand command, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        var isElevatedTransfer = gate.Value.Role is AccountUserRole.Owner or AccountUserRole.Admin;

        if (command.NewRecorderAccountUserId == Guid.Empty)
            return Result<Guid>.Failure(ActualWorkErrors.RecorderTransferTargetRequired);

        // Only an Owner/Admin transfer states its own reason; a recorder-initiated hand-off always
        // uses the fixed system reason and must not be able to inject audit text.
        if (isElevatedTransfer && string.IsNullOrWhiteSpace(command.Reason))
            return Result<Guid>.Failure(ActualWorkErrors.RecorderTransferReasonRequired);

        var actualWork = await persistence.GetByIdAsync(currentUser.AccountId, actualWorkId, ct);
        if (actualWork is null)
            return Result<Guid>.Failure(ActualWorkErrors.NotFound);

        // Caller authority, ahead of any other failure the caller shouldn't be able to observe: an
        // Owner/Admin may transfer any Draft; every other caller may transfer only the one they
        // currently hold (Slice 4d). A non-recorder non-Owner/Admin gets NotFound (mirrors
        // AuthorizeAndLoadDraftAsync) so this endpoint never reveals an unrelated Draft's existence
        // or its target-eligibility outcome.
        if (!isElevatedTransfer && actualWork.RecorderAccountUserId != currentUser.UserId)
            return Result<Guid>.Failure(ActualWorkErrors.NotFound);

        // Option C invariant: only a member who could record this work may hold its Draft. A
        // non-member and an unqualified member collapse to one error so this endpoint cannot be
        // used to enumerate account membership.
        var targetSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, command.NewRecorderAccountUserId, ct);
        if (targetSnapshot is null ||
            !userAccessPolicy.IsPermitted(
                targetSnapshot.Role, targetSnapshot.MembershipStatus, gate.Value.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                targetSnapshot.Role, targetSnapshot.MembershipStatus, gate.Value.Purpose,
                PermissionKeys.Keep.ActualWorkCapture))
        {
            return Result<Guid>.Failure(ActualWorkErrors.RecorderTransferTargetIneligible);
        }

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(ActualWorkErrors.VersionMismatch);

        var reason = isElevatedTransfer ? command.Reason : RecorderInitiatedHandoffReason;

        var priorRecorderAccountUserId = actualWork.RecorderAccountUserId;
        var transferResult = actualWork.TransferRecorder(command.NewRecorderAccountUserId);
        if (transferResult.IsFailure)
            return Result<Guid>.Failure(transferResult.Error);

        var transferEvent = ActualWorkDraftRecorderTransfer.Create(
            currentUser.AccountId, actualWork.Id, currentUser.UserId, priorRecorderAccountUserId,
            command.NewRecorderAccountUserId, reason, clock.UtcNow);

        var commitResult = await persistence.CommitAsync(actualWork, transferEvent, ct);
        return commitResult switch
        {
            ActualWorkCommitResult.Committed => Result<Guid>.Success(actualWork.ConcurrencyVersion),
            ActualWorkCommitResult.ConcurrencyConflict => Result<Guid>.Failure(ActualWorkErrors.VersionMismatch),
            _ => throw new InvalidOperationException($"Unexpected commit result: {commitResult}"),
        };
    }

    private async Task<Result<Guid>> CommitAsync(ActualWork actualWork, CancellationToken ct)
    {
        var commitResult = await persistence.CommitAsync(actualWork, ct);
        return commitResult switch
        {
            ActualWorkCommitResult.Committed => Result<Guid>.Success(actualWork.ConcurrencyVersion),
            ActualWorkCommitResult.ConcurrencyConflict => Result<Guid>.Failure(ActualWorkErrors.VersionMismatch),
            _ => throw new InvalidOperationException($"Unexpected commit result: {commitResult}"),
        };
    }

    /// <summary>ADR-494 D2: revalidate a <b>caller-supplied</b> performer id — the ticket default at
    /// create / <c>SetDefaultPerformer</c>, or an explicit per-line performer. Non-member,
    /// cross-account (the snapshot read is tenant-scoped), inactive, empty guid, and
    /// permission-ineligible all collapse to <see cref="ActualWorkErrors.PerformerIneligible"/> so
    /// this can never enumerate account membership. Never called for an inherited ticket default.</summary>
    private async Task<Result> ValidateSuppliedPerformerAsync(
        Guid performerAccountUserId, AccountPurpose accountPurpose, CancellationToken ct)
    {
        if (performerAccountUserId == Guid.Empty)
            return Result.Failure(ActualWorkErrors.PerformerIneligible);

        var snapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, performerAccountUserId, ct);
        if (snapshot is null ||
            !ActualWorkPerformerEligibility.IsEligible(
                userAccessPolicy, snapshot.Role, snapshot.MembershipStatus, accountPurpose))
        {
            return Result.Failure(ActualWorkErrors.PerformerIneligible);
        }

        return Result.Success();
    }

    /// <summary>Gate 1-3, then load the visit and confirm it is still a Draft owned by the caller's
    /// current recorder ownership (GAP-055) — the one row-authorization check every line mutation
    /// and discard shares. A submitted visit is immutable: every caller here, including Discard,
    /// relies on this returning <see cref="ActualWorkErrors.NotDraft"/> rather than separately
    /// re-checking Status.</summary>
    private async Task<Result<ActualWork>> AuthorizeAndLoadDraftAsync(Guid actualWorkId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ActualWork>.Failure(gate.Error);

        var actualWork = await persistence.GetByIdAsync(currentUser.AccountId, actualWorkId, ct);
        if (actualWork is null)
            return Result<ActualWork>.Failure(ActualWorkErrors.NotFound);

        if (actualWork.RecorderAccountUserId != currentUser.UserId)
            return Result<ActualWork>.Failure(ActualWorkErrors.NotFound);

        if (actualWork.Status != ActualWorkStatus.Draft)
            return Result<ActualWork>.Failure(ActualWorkErrors.NotDraft);

        return Result<ActualWork>.Success(actualWork);
    }

    /// <summary>Row-authorization scope (derived from role) plus the caller's <see cref="Role"/>
    /// itself — <see cref="Role"/> is needed only by <see cref="TransferRecorderAsync"/> to tell an
    /// Owner/Admin transfer from a recorder-initiated hand-off, and <see cref="Purpose"/> only by that
    /// same method's target-eligibility check; every other caller uses <see cref="Scope"/> alone.</summary>
    private sealed record ActualWorkAuthorization(
        KeepRequestVisibilityScope Scope, AccountUserRole Role, AccountPurpose Purpose);

    /// <summary>Returns the row-authorization scope (derived from role) on success, matching
    /// <see cref="ProposedScopeApiService.AuthorizeAsync"/> exactly except gate 3 checks
    /// <c>ActualWorkCapture</c> instead of <c>ScopeCapture</c>.</summary>
    private async Task<Result<ActualWorkAuthorization>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<ActualWorkAuthorization>.Failure(Unauthorized);

        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<ActualWorkAuthorization>.Failure(Forbidden);

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
            return Result<ActualWorkAuthorization>.Failure(Forbidden);

        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result<ActualWorkAuthorization>.Failure(Forbidden);

        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result<ActualWorkAuthorization>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.ActualWorkCapture))
        {
            return Result<ActualWorkAuthorization>.Failure(Forbidden);
        }

        var scope = roleSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        return Result<ActualWorkAuthorization>.Success(
            new ActualWorkAuthorization(scope, roleSnapshot.Role, accountSnapshot.Purpose));
    }
}
