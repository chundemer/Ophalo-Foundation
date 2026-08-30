using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the ADR-463 signal raise/resolve for
/// <see cref="KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview"/> (ADR-494 D4). The
/// two raw statements moved here verbatim from <c>EfActualWorkSubmissionPersistence</c> and
/// <c>EfActualWorkReviewPersistence</c> — no predicate or behaviour change. Takes the
/// request-scoped <see cref="OpHaloDbContext"/> via DI so both statements auto-enlist in the
/// transaction the calling persistence class already has open.
/// </summary>
public sealed class EfActualWorkReviewSignalReconciliation(OpHaloDbContext dbContext)
    : IActualWorkReviewSignalReconciliation
{
    /// <summary>
    /// The single shared "open outstanding review" predicate (ADR-494 D4): a <c>keep_actual_works</c>
    /// row that still owes office review. Owned here and evaluated only by
    /// <see cref="ResolveIfClearAsync"/>. 4e-i widened it with <c>AND superseded_at_utc IS NULL</c>
    /// (ADR-494 D4/D8): a superseded submitted visit is never reviewed, so it must not keep the
    /// aggregate review signal raised.
    /// </summary>
    private const string OpenOutstandingReviewPredicate =
        "status = 'Submitted' AND reviewed_at_utc IS NULL AND deleted_at_utc IS NULL AND superseded_at_utc IS NULL";

    /// <summary>
    /// Native atomic upsert (ADR-463): a currently active signal (<c>resolved_at_utc IS NULL</c>)
    /// is left completely untouched via the <c>WHERE</c> clause on the conflict target, so
    /// <c>DO UPDATE</c> never fires for an already-active row. A resolved signal is reopened by
    /// clearing <c>resolved_at_utc</c> and replacing <c>raised_at_utc</c>.
    /// </summary>
    public async Task RaiseAsync(Guid accountId, Guid requestId, DateTime nowUtc, CancellationToken ct)
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

    /// <summary>
    /// Per-request aggregate resolve (ADR-463/487): clears the active signal only when no
    /// <c>Submitted</c> visit remains with a null <c>reviewed_at_utc</c> for this request. Reviewing
    /// one of several submitted visits leaves the signal active. Callers run this after their own
    /// visit write has been persisted in the same transaction, so that row's just-written state is
    /// already reflected in the <c>NOT EXISTS</c> check.
    /// </summary>
    public async Task ResolveIfClearAsync(Guid accountId, Guid requestId, DateTime nowUtc, CancellationToken ct)
    {
        var newConcurrencyVersion = Guid.NewGuid();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE keep_request_work_signals
            SET resolved_at_utc = {0},
                concurrency_version = {1},
                updated_at_utc = {0}
            WHERE account_id = {2}
              AND keep_request_id = {3}
              AND source_module_key = {4}
              AND signal_key = {5}
              AND resolved_at_utc IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM keep_actual_works
                  WHERE account_id = {2}
                    AND request_id = {3}
                    AND
            """
            + " " + OpenOutstandingReviewPredicate + ")",
            [
                nowUtc,
                newConcurrencyVersion,
                accountId,
                requestId,
                KeepRequestWorkSignalKeys.Modules.PriceBookQuotesMaterials,
                KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview,
            ],
            ct);
    }
}
