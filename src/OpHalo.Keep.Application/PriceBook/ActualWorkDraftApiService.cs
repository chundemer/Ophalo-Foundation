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
    string? Note);

/// <summary>The new line's id plus the parent visit's post-mutation ConcurrencyVersion — same
/// contract-detail reasoning as ProposedScope's <c>AddProposedScopeLineResult</c>: an add spends the
/// parent's token, so the caller needs the new value to chain the next sequential edit.</summary>
public sealed record AddActualWorkLineResult(Guid LineId, Guid ActualWorkConcurrencyVersion);

/// <summary>
/// API-facing orchestration for Draft create/add-line/update-line/remove-line/discard (ADR-487,
/// build-log/129, Batch 3) — the single three-gate owner for Actual Work draft mutations:
/// <c>RequestsOperate</c> + Price Book entitlement (ADR-462) + <c>ActualWorkCapture</c>, gate
/// composition mirroring <see cref="ProposedScopeApiService"/> exactly. A fourth row-authorization
/// gate follows: <see cref="IActiveResponsibleCheck"/> confirms the caller is the request's active
/// Responsible participant — the pilot's sole field recorder (build-log/129) — not merely a member
/// with row visibility. Submitted visits are immutable; every mutation here rejects a non-Draft
/// visit via <see cref="ActualWorkErrors.NotDraft"/> from the domain aggregate itself.
/// </summary>
public sealed class ActualWorkDraftApiService(
    IActualWorkPersistence persistence,
    ICatalogReadPersistence catalogPersistence,
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

    public async Task<Result<ActualWork>> CreateAsync(Guid requestId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ActualWork>.Failure(gate.Error);

        var isResponsible = await responsibleCheck.IsActiveResponsibleAsync(
            requestId, currentUser.AccountId, currentUser.UserId, gate.Value, ct);
        if (!isResponsible)
            return Result<ActualWork>.Failure(KeepRequestErrors.NotFound);

        var createResult = ActualWork.Create(currentUser.AccountId, requestId, currentUser.UserId);
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

        var addResult = actualWork.AddLine(
            catalogItemId, priceBookVersionLineId, displayNameSnapshot, unitOfMeasureSnapshot,
            command.ActualQuantity, sellPriceSnapshot, standardExpectedDirectCostSnapshot,
            command.Note, commercialBaselineSourceLineId: null, currentUser.UserId);
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

    /// <summary>Gate 1-3, then load the visit and confirm it is still a Draft owned (by request) by
    /// the caller's active Responsible participation — the one row-authorization check every line
    /// mutation and discard shares. A submitted visit is immutable: every caller here, including
    /// Discard, relies on this returning <see cref="ActualWorkErrors.NotDraft"/> rather than
    /// separately re-checking Status.</summary>
    private async Task<Result<ActualWork>> AuthorizeAndLoadDraftAsync(Guid actualWorkId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ActualWork>.Failure(gate.Error);

        var actualWork = await persistence.GetByIdAsync(currentUser.AccountId, actualWorkId, ct);
        if (actualWork is null)
            return Result<ActualWork>.Failure(ActualWorkErrors.NotFound);

        var isResponsible = await responsibleCheck.IsActiveResponsibleAsync(
            actualWork.RequestId, currentUser.AccountId, currentUser.UserId, gate.Value, ct);
        if (!isResponsible)
            return Result<ActualWork>.Failure(KeepRequestErrors.NotFound);

        if (actualWork.Status != ActualWorkStatus.Draft)
            return Result<ActualWork>.Failure(ActualWorkErrors.NotDraft);

        return Result<ActualWork>.Success(actualWork);
    }

    /// <summary>Returns the row-authorization scope (derived from role) on success, matching
    /// <see cref="ProposedScopeApiService.AuthorizeAsync"/> exactly except gate 3 checks
    /// <c>ActualWorkCapture</c> instead of <c>ScopeCapture</c>.</summary>
    private async Task<Result<KeepRequestVisibilityScope>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestVisibilityScope>.Failure(Unauthorized);

        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

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
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.ActualWorkCapture))
        {
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);
        }

        var scope = roleSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        return Result<KeepRequestVisibilityScope>.Success(scope);
    }
}
