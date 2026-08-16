using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the atomic submit/signal-reconciliation operation (Session 3.3a.2). See
/// <see cref="IProposedScopeSubmissionPersistence"/> for the full contract. Default (Read
/// Committed) isolation: the scope's own optimistic-concurrency token gates its write, the ADR-463
/// signal upsert is a single atomic statement, and the request's terminal-state check takes an
/// explicit <c>SELECT ... FOR UPDATE</c> row lock rather than relying on a stronger isolation
/// level — none of the three needs Serializable to avoid a read-then-decide race.
/// </summary>
public sealed class EfProposedScopeSubmissionPersistence(OpHaloDbContext dbContext) : IProposedScopeSubmissionPersistence
{
    public async Task<ProposedScopeSubmissionOutcome> SubmitAsync(
        Guid accountId, Guid proposedScopeId, Guid expectedVersion, DateTime submittedAtUtc, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        // Include(Lines) is required: ProposedScope.Submit's EmptySubmit rule (Session 4a) reads
        // the in-memory _lines field, which a plain FirstOrDefaultAsync leaves empty regardless of
        // what's actually persisted — without this, every submit would fail as EmptySubmit
        // (Session 4b corrective fix).
        var scope = await dbContext.Set<ProposedScope>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == proposedScopeId, ct);
        if (scope is null)
            return new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.NotFound);

        if (scope.ConcurrencyVersion != expectedVersion)
            return new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.VersionMismatch);

        // SELECT ... FOR UPDATE: locks the request row for the rest of this transaction, so a
        // concurrent status change to a terminal state cannot commit between this check and our
        // own commit below — without this lock, an AsNoTracking read only proves the request
        // wasn't terminal at read time, not for the duration of the write. IgnoreQueryFilters
        // because the soft-delete filter is reproduced by hand in the raw SQL instead: composing
        // it as an outer filter around a FOR UPDATE subquery is unnecessary risk for no benefit
        // here. Client-materialized (not translated into the query) because KeepRequest.IsTerminal
        // is a pattern-match computed property EF cannot translate to SQL.
        var request = await dbContext.Set<KeepRequest>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_requests
                 WHERE account_id = {accountId} AND id = {scope.RequestId} AND deleted_at_utc IS NULL
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        if (request is null)
            return new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.NotFound);
        if (request.IsTerminal)
            return new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.RequestTerminal);

        // Submit's only failure modes are EmptySubmit (a real, reachable business rule — the
        // version check does not guard against an empty line list) and "not Draft" (defensive
        // only: unreachable in practice, since the version check above already proves nothing has
        // mutated this row since it was loaded, and ConcurrencyVersion changes on every mutation
        // including a prior Submit).
        var submitResult = scope.Submit(submittedAtUtc);
        if (submitResult.IsFailure)
        {
            return submitResult.Error == ProposedScopeErrors.EmptySubmit
                ? new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.EmptySubmit)
                : new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.VersionMismatch);
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.VersionMismatch);
        }

        await UpsertWorkSignalAsync(accountId, scope.RequestId, submittedAtUtc, ct);

        await tx.CommitAsync(ct);
        return new ProposedScopeSubmissionOutcome(ProposedScopeSubmissionResult.Committed, scope.ConcurrencyVersion);
    }

    /// <summary>
    /// Native atomic upsert (ADR-463): a currently active signal (<c>resolved_at_utc IS NULL</c>)
    /// is left completely untouched — not even <c>concurrency_version</c>/<c>updated_at_utc</c>
    /// bump — via the <c>WHERE</c> clause on the conflict target, so <c>DO UPDATE</c> never fires
    /// for an already-active row. A resolved signal is reopened by clearing
    /// <c>resolved_at_utc</c> and replacing <c>raised_at_utc</c>. One round trip, no
    /// application-level retry loop for the insert-or-reopen race.
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
                  {KeepRequestWorkSignalKeys.Signals.ProposedScopeNeedsOfficeReview},
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
