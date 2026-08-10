using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record AddProposedScopeLineCommand(
    Guid AccountId,
    Guid ProposedScopeId,
    Guid ExpectedVersion,
    Guid CurrentAccountUserId,
    KeepRequestVisibilityScope Scope,
    ProposedScopeLineType LineType,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    decimal Quantity,
    bool IsException,
    string? OffCatalogDescription,
    decimal? OffCatalogQuantity,
    string? Note,
    int DisplayOrder,
    string DisplayNameSnapshot,
    string? UnitOfMeasureSnapshot,
    string? OfferingAssemblyNameSnapshot,
    decimal? DefaultQuantitySnapshot,
    Guid CreatedByUserId);

/// <summary>The new line's id plus the parent's post-mutation ConcurrencyVersion — same
/// contract-detail reasoning as OfferingAssembly's <c>AddOfferingAssemblyItemResult</c> (Session
/// 3.2b): an add spends the parent's token, so the caller needs the new value to chain the next
/// sequential edit without a separate read.</summary>
public sealed record AddProposedScopeLineResult(Guid LineId, Guid ScopeConcurrencyVersion);

public sealed record UpdateProposedScopeLineCommand(
    Guid AccountId,
    Guid ProposedScopeId,
    Guid LineId,
    Guid ExpectedVersion,
    Guid CurrentAccountUserId,
    KeepRequestVisibilityScope Scope,
    decimal Quantity,
    bool IsException,
    string? Note,
    int DisplayOrder);

public sealed record RemoveProposedScopeLineCommand(
    Guid AccountId,
    Guid ProposedScopeId,
    Guid LineId,
    Guid ExpectedVersion,
    Guid CurrentAccountUserId,
    KeepRequestVisibilityScope Scope);

/// <summary>
/// Orchestrates <see cref="ProposedScope"/> line add/update/remove (Session 3.3b). Deliberately
/// takes accountId/actor ids as plain parameters — auth-stack composition (ADR-480's three gates)
/// is owned by the caller (<see cref="ProposedScopeApiService"/>). Every mutation re-checks the
/// terminal-request precondition (the same plain, unlocked <see cref="KeepRequest.IsTerminal"/>
/// read as <see cref="CreateProposedScopeService"/>) — extended to edit, not just submit
/// (session-log 3.3b). <see cref="ProposedScope"/> itself has no header-mutable field, so there is
/// no header-update method here, only line operations.
/// </summary>
public sealed class EditProposedScopeService(
    IProposedScopePersistence persistence,
    IKeepRequestDetailPersistence requestPersistence)
{
    public async Task<Result<AddProposedScopeLineResult>> AddLineAsync(AddProposedScopeLineCommand command, CancellationToken ct)
    {
        var loadResult = await LoadForEditAsync(
            command.AccountId, command.ProposedScopeId, command.ExpectedVersion,
            command.CurrentAccountUserId, command.Scope, ct);
        if (loadResult.IsFailure)
            return Result<AddProposedScopeLineResult>.Failure(loadResult.Error);

        var scope = loadResult.Value;
        var addResult = scope.AddLine(
            command.LineType, command.CatalogItemId, command.OfferingAssemblyId, command.Quantity,
            command.IsException, command.OffCatalogDescription, command.OffCatalogQuantity, command.Note,
            command.DisplayOrder, command.DisplayNameSnapshot, command.UnitOfMeasureSnapshot,
            command.OfferingAssemblyNameSnapshot, command.DefaultQuantitySnapshot, command.CreatedByUserId);
        if (addResult.IsFailure)
            return Result<AddProposedScopeLineResult>.Failure(addResult.Error);

        var commitResult = await persistence.CommitAsync(scope, ct);
        var transitionResult = ToTransitionResult(commitResult, scope);
        return transitionResult.IsSuccess
            ? Result<AddProposedScopeLineResult>.Success(new AddProposedScopeLineResult(addResult.Value.Id, transitionResult.Value))
            : Result<AddProposedScopeLineResult>.Failure(transitionResult.Error);
    }

    public async Task<Result<Guid>> UpdateLineAsync(UpdateProposedScopeLineCommand command, CancellationToken ct)
    {
        var loadResult = await LoadForEditAsync(
            command.AccountId, command.ProposedScopeId, command.ExpectedVersion,
            command.CurrentAccountUserId, command.Scope, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);

        var scope = loadResult.Value;
        var updateResult = scope.UpdateLine(command.LineId, command.Quantity, command.IsException, command.Note, command.DisplayOrder);
        if (updateResult.IsFailure)
            return Result<Guid>.Failure(updateResult.Error);

        return ToTransitionResult(await persistence.CommitAsync(scope, ct), scope);
    }

    public async Task<Result<Guid>> RemoveLineAsync(RemoveProposedScopeLineCommand command, CancellationToken ct)
    {
        var loadResult = await LoadForEditAsync(
            command.AccountId, command.ProposedScopeId, command.ExpectedVersion,
            command.CurrentAccountUserId, command.Scope, ct);
        if (loadResult.IsFailure)
            return Result<Guid>.Failure(loadResult.Error);

        var scope = loadResult.Value;
        var removeResult = scope.RemoveLine(command.LineId);
        if (removeResult.IsFailure)
            return Result<Guid>.Failure(removeResult.Error);

        return ToTransitionResult(await persistence.CommitAsync(scope, ct), scope);
    }

    /// <summary>
    /// Loads the tracked scope and re-checks the request-scoped visibility/terminal precondition
    /// against the scope's own <c>RequestId</c> — the shared preamble for every line mutation.
    /// Visibility is checked <em>before</em> the expected-version comparison: a stale token on a
    /// same-account scope the caller cannot see under <c>MyWork</c> must still 404, not 409 —
    /// otherwise the version-mismatch response would leak that the row exists.
    /// </summary>
    private async Task<Result<ProposedScope>> LoadForEditAsync(
        Guid accountId, Guid proposedScopeId, Guid expectedVersion,
        Guid currentAccountUserId, KeepRequestVisibilityScope scopeVisibility, CancellationToken ct)
    {
        var scope = await persistence.GetByIdAsync(accountId, proposedScopeId, ct);
        if (scope is null)
            return Result<ProposedScope>.Failure(ProposedScopeErrors.NotFound);

        var request = await requestPersistence.GetRequestAsync(scope.RequestId, accountId, currentAccountUserId, scopeVisibility, ct);
        if (request is null)
            return Result<ProposedScope>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<ProposedScope>.Failure(KeepRequestErrors.TerminalState);

        if (scope.ConcurrencyVersion != expectedVersion)
            return Result<ProposedScope>.Failure(ProposedScopeErrors.VersionMismatch);

        return Result<ProposedScope>.Success(scope);
    }

    /// <summary>
    /// Request-scoped visibility check only (no version/terminal check) — used by
    /// <see cref="ProposedScopeApiService.SubmitAsync"/> before it delegates to
    /// <see cref="IProposedScopeSubmissionPersistence"/>, which owns its own terminal-state check
    /// under a row lock. Without this, an Operator's <c>MyWork</c> scope would never be applied to
    /// submit, letting them submit any scope in the account regardless of request participation.
    /// </summary>
    public async Task<Result> VerifyRequestVisibleAsync(
        Guid accountId, Guid proposedScopeId, Guid currentAccountUserId, KeepRequestVisibilityScope scopeVisibility, CancellationToken ct)
    {
        var scope = await persistence.GetByIdAsync(accountId, proposedScopeId, ct);
        if (scope is null)
            return Result.Failure(ProposedScopeErrors.NotFound);

        var request = await requestPersistence.GetRequestAsync(scope.RequestId, accountId, currentAccountUserId, scopeVisibility, ct);
        if (request is null)
            return Result.Failure(KeepRequestErrors.NotFound);

        return Result.Success();
    }

    private static Result<Guid> ToTransitionResult(ProposedScopeCommitResult commitResult, ProposedScope scope) =>
        commitResult switch
        {
            ProposedScopeCommitResult.Committed => Result<Guid>.Success(scope.ConcurrencyVersion),
            _ => Result<Guid>.Failure(ProposedScopeErrors.VersionMismatch),
        };
}
