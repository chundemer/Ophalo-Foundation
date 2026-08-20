using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the atomic submit/signal-reconciliation operation (Batch 4,
/// build-log/129). See <see cref="IActualWorkSubmissionPersistence"/> for the full contract.
/// Mirrors <see cref="EfProposedScopeSubmissionPersistence"/> exactly: default (Read Committed)
/// isolation, the visit's own optimistic-concurrency token gates its write, the ADR-463 signal
/// upsert is a single atomic statement, and the request's terminal-state check takes an explicit
/// <c>SELECT ... FOR UPDATE</c> row lock rather than relying on a stronger isolation level.
/// </summary>
public sealed class EfActualWorkSubmissionPersistence(OpHaloDbContext dbContext) : IActualWorkSubmissionPersistence
{
    public async Task<ActualWorkSubmissionOutcome> SubmitAsync(
        Guid accountId, Guid actualWorkId, Guid expectedVersion, ActualWorkOutcome? outcome,
        string? completionNote, DateTime submittedAtUtc, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        // Include(Lines) is required: ActualWork.Submit's zero-line invariant (build-log/129) reads
        // the in-memory _lines field, which a plain FirstOrDefaultAsync leaves empty regardless of
        // what's actually persisted.
        var actualWork = await dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == actualWorkId, ct);
        if (actualWork is null)
            return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.NotFound);

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.VersionMismatch);

        // SELECT ... FOR UPDATE: locks the request row for the rest of this transaction, so a
        // concurrent status change to a terminal state cannot commit between this check and our
        // own commit below. IgnoreQueryFilters because the soft-delete filter is reproduced by hand
        // in the raw SQL. Client-materialized because KeepRequest.IsTerminal is a pattern-match
        // computed property EF cannot translate to SQL.
        var request = await dbContext.Set<KeepRequest>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_requests
                 WHERE account_id = {accountId} AND id = {actualWork.RequestId} AND deleted_at_utc IS NULL
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        if (request is null)
            return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.NotFound);
        if (request.IsTerminal)
            return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.RequestTerminal);

        var submitResult = actualWork.Submit(submittedAtUtc, outcome, completionNote);
        if (submitResult.IsFailure)
        {
            if (submitResult.Error == ActualWorkErrors.ZeroLineCompletionNoteRequired)
                return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.ZeroLineCompletionNoteRequired);
            if (submitResult.Error == ActualWorkErrors.ZeroLineOutcomeRequired)
                return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.ZeroLineOutcomeRequired);
            if (submitResult.Error == ActualWorkErrors.InvalidOutcome)
                return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.InvalidOutcome);

            // ActualWorkErrors.NotDraft: defensive only, unreachable in practice — the version
            // check above already proves nothing has mutated this row since it was loaded.
            return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.VersionMismatch);
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.VersionMismatch);
        }

        await UpsertWorkSignalAsync(accountId, actualWork.RequestId, submittedAtUtc, ct);

        await tx.CommitAsync(ct);
        return new ActualWorkSubmissionOutcome(ActualWorkSubmissionResult.Committed, actualWork.ConcurrencyVersion);
    }

    /// <summary>
    /// Native atomic upsert (ADR-463): a currently active signal (<c>resolved_at_utc IS NULL</c>)
    /// is left completely untouched via the <c>WHERE</c> clause on the conflict target, so
    /// <c>DO UPDATE</c> never fires for an already-active row. A resolved signal is reopened by
    /// clearing <c>resolved_at_utc</c> and replacing <c>raised_at_utc</c>.
    /// </summary>
    private async Task UpsertWorkSignalAsync(Guid accountId, Guid requestId, DateTime nowUtc, CancellationToken ct)
    {
        var newId = Guid.CreateVersion7();
        var newConcurrencyVersion = Guid.NewGuid();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO keep_request_work_signals
                 (id, account_id, keep_request_id, source_module_key, signal_key,
                  raised_at_utc, resolved_at_utc, concurrency_version, created_at_utc, updated_at_utc)
             VALUES
                 ({newId}, {accountId}, {requestId},
                  {KeepRequestWorkSignalKeys.Modules.PriceBookQuotesMaterials},
                  {KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview},
                  {nowUtc}, NULL, {newConcurrencyVersion}, {nowUtc}, {nowUtc})
             ON CONFLICT (account_id, keep_request_id, source_module_key, signal_key)
             DO UPDATE SET
                 raised_at_utc = EXCLUDED.raised_at_utc,
                 resolved_at_utc = NULL,
                 concurrency_version = EXCLUDED.concurrency_version,
                 updated_at_utc = EXCLUDED.updated_at_utc
             WHERE keep_request_work_signals.resolved_at_utc IS NOT NULL
             """,
            ct);
    }
}
