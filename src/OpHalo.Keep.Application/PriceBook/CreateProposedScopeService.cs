using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CreateProposedScopeCommand(
    Guid AccountId,
    Guid RequestId,
    Guid CurrentAccountUserId,
    KeepRequestVisibilityScope Scope,
    Guid CreatedByUserId);

/// <summary>
/// Creates a new <c>Draft</c> <see cref="ProposedScope"/> for a request (Session 3.3b). Deliberately
/// takes accountId/actor ids as plain parameters — auth-stack composition (ADR-480's three gates)
/// is owned by the caller (<see cref="ProposedScopeApiService"/>), matching
/// <see cref="OfferingAssemblyLifecycleService"/>. Owns the terminal-request precondition itself: a
/// plain account-scoped <see cref="KeepRequest.IsTerminal"/> read via
/// <see cref="IKeepRequestDetailPersistence"/> — no row lock, unlike
/// <see cref="IProposedScopeSubmissionPersistence"/>'s submit, since create isn't racing a second
/// write to the same request row.
/// </summary>
public sealed class CreateProposedScopeService(
    IProposedScopePersistence persistence,
    IKeepRequestDetailPersistence requestPersistence)
{
    public async Task<Result<ProposedScope>> CreateAsync(CreateProposedScopeCommand command, CancellationToken ct)
    {
        var request = await requestPersistence.GetRequestAsync(
            command.RequestId, command.AccountId, command.CurrentAccountUserId, command.Scope, ct);
        if (request is null)
            return Result<ProposedScope>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<ProposedScope>.Failure(KeepRequestErrors.TerminalState);

        var createResult = ProposedScope.Create(command.AccountId, command.RequestId, command.CreatedByUserId);
        if (createResult.IsFailure)
            return createResult;

        var scope = createResult.Value;
        var commitResult = await persistence.AddAsync(scope, ct);
        return commitResult switch
        {
            ProposedScopeCommitResult.Committed => Result<ProposedScope>.Success(scope),
            ProposedScopeCommitResult.DraftAlreadyOpenForRequest =>
                Result<ProposedScope>.Failure(ProposedScopeErrors.DraftAlreadyOpenForRequest),
            _ => Result<ProposedScope>.Failure(ProposedScopeErrors.VersionMismatch),
        };
    }
}
