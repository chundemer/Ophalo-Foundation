using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Thin Application-layer orchestration for submitting a <c>ProposedScope</c> (Session 3.3a.2).
/// Owns no transaction and touches no table directly — <see cref="IProposedScopeSubmissionPersistence"/>
/// owns the entire atomic submit/signal-reconciliation operation; this service only maps its
/// outcome to a <see cref="Result{TValue}"/>. Deliberately takes <c>accountId</c> as a plain
/// parameter rather than resolving it itself — ADR-480's three-gate auth composition is the
/// caller's job (Session 3.3b), matching <c>OfferingAssemblyLifecycleService</c>.
/// </summary>
public sealed class SubmitProposedScopeService(IProposedScopeSubmissionPersistence submissionPersistence, IClock clock)
{
    public async Task<Result<Guid>> SubmitAsync(Guid accountId, Guid proposedScopeId, Guid expectedVersion, CancellationToken ct)
    {
        var outcome = await submissionPersistence.SubmitAsync(accountId, proposedScopeId, expectedVersion, clock.UtcNow, ct);

        return outcome.Result switch
        {
            ProposedScopeSubmissionResult.Committed => Result<Guid>.Success(outcome.ConcurrencyVersion!.Value),
            ProposedScopeSubmissionResult.NotFound => Result<Guid>.Failure(ProposedScopeErrors.NotFound),
            ProposedScopeSubmissionResult.RequestTerminal => Result<Guid>.Failure(KeepRequestErrors.TerminalState),
            _ => Result<Guid>.Failure(ProposedScopeErrors.VersionMismatch),
        };
    }
}
