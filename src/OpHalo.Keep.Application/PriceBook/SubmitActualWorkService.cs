using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Thin Application-layer orchestration for submitting an <c>ActualWork</c> visit (Batch 4,
/// build-log/129). Owns no transaction and touches no table directly —
/// <see cref="IActualWorkSubmissionPersistence"/> owns the entire atomic submit/signal-reconciliation
/// operation; this service only maps its outcome to a <see cref="Result{TValue}"/>. Deliberately
/// takes <c>accountId</c> as a plain parameter rather than resolving it itself — row-authorization
/// (the active-Responsible check) is the caller's job (<c>ActualWorkDraftApiService</c>), matching
/// <c>SubmitProposedScopeService</c>.
/// </summary>
public sealed class SubmitActualWorkService(IActualWorkSubmissionPersistence submissionPersistence, IClock clock)
{
    public async Task<Result<Guid>> SubmitAsync(
        Guid accountId, Guid actualWorkId, Guid expectedVersion, ActualWorkOutcome? outcome,
        string? completionNote, CancellationToken ct)
    {
        var outcomeResult = await submissionPersistence.SubmitAsync(
            accountId, actualWorkId, expectedVersion, outcome, completionNote, clock.UtcNow, ct);

        return outcomeResult.Result switch
        {
            ActualWorkSubmissionResult.Committed => Result<Guid>.Success(outcomeResult.ConcurrencyVersion!.Value),
            ActualWorkSubmissionResult.NotFound => Result<Guid>.Failure(ActualWorkErrors.NotFound),
            ActualWorkSubmissionResult.RequestTerminal => Result<Guid>.Failure(KeepRequestErrors.TerminalState),
            ActualWorkSubmissionResult.ZeroLineCompletionNoteRequired =>
                Result<Guid>.Failure(ActualWorkErrors.ZeroLineCompletionNoteRequired),
            ActualWorkSubmissionResult.ZeroLineOutcomeRequired =>
                Result<Guid>.Failure(ActualWorkErrors.ZeroLineOutcomeRequired),
            ActualWorkSubmissionResult.InvalidOutcome => Result<Guid>.Failure(ActualWorkErrors.InvalidOutcome),
            _ => Result<Guid>.Failure(ActualWorkErrors.VersionMismatch),
        };
    }
}
