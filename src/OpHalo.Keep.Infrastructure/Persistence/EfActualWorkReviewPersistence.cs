using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the atomic mark-reviewed/signal-resolution operation (Batch 6,
/// build-log/129). See <see cref="IActualWorkReviewPersistence"/> for the full contract. Mirrors
/// <see cref="EfActualWorkSubmissionPersistence"/>'s transaction shape, inverted from raise/reopen
/// to a conditional resolve: default (Read Committed) isolation, the visit's own optimistic-
/// concurrency token gates its write, and the ADR-463 signal resolve is a single atomic statement
/// run after the review commits. No terminal-request check (build-log/129, "6 preflight" — a
/// submitted visit must remain reviewable so its signal cannot be stranded after the request
/// closes).
/// </summary>
public sealed class EfActualWorkReviewPersistence(
    OpHaloDbContext dbContext,
    IActualWorkFinancialResolutionPersistence financialResolutionPersistence) : IActualWorkReviewPersistence
{
    public async Task<ActualWorkReviewOutcome> MarkReviewedAsync(
        Guid accountId, Guid actualWorkId, Guid expectedVersion, Guid reviewedByAccountUserId,
        string? reviewNote, DateTime reviewedAtUtc, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        var actualWork = await dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == actualWorkId, ct);
        if (actualWork is null)
            return new ActualWorkReviewOutcome(ActualWorkReviewResult.NotFound);

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return new ActualWorkReviewOutcome(ActualWorkReviewResult.VersionMismatch);

        // BL135 §4 Batch 3b-ii — the hard billing-readiness gate reads the same account-scoped
        // financial facts the Owner/Admin review card renders, inside this transaction, so a
        // resolution/disposition appended after this read loses the visit concurrency-token race on
        // save and neither side commits against stale facts. Ordered after the version check and
        // before MarkReviewed so its existing state/repeat/note failures still take precedence.
        var resolutions = await financialResolutionPersistence.GetResolutionsForVisitAsync(accountId, actualWorkId, ct);
        var dispositions = await financialResolutionPersistence.GetDispositionsForVisitAsync(accountId, actualWorkId, ct);
        var financialDataComplete = AllLinesFinanciallyComplete(actualWork.Lines, resolutions);
        var zeroLineDispositionSatisfied =
            dispositions.Any(d => d.Kind == OfficeFinancialDispositionKind.NoCharge);

        var reviewResult = actualWork.MarkReviewed(
            reviewedByAccountUserId, reviewNote, reviewedAtUtc,
            financialDataComplete, zeroLineDispositionSatisfied);
        if (reviewResult.IsFailure)
        {
            if (reviewResult.Error == ActualWorkErrors.NotSubmitted)
                return new ActualWorkReviewOutcome(ActualWorkReviewResult.NotSubmitted);
            if (reviewResult.Error == ActualWorkErrors.AlreadyReviewed)
                return new ActualWorkReviewOutcome(ActualWorkReviewResult.AlreadyReviewed);
            if (reviewResult.Error == ActualWorkErrors.ReviewNoteTooLong)
                return new ActualWorkReviewOutcome(ActualWorkReviewResult.ReviewNoteTooLong);
            if (reviewResult.Error == ActualWorkErrors.ReviewBlockedIncompleteFinancials)
                return new ActualWorkReviewOutcome(ActualWorkReviewResult.BlockedIncompleteFinancials);
            if (reviewResult.Error == ActualWorkErrors.ReviewBlockedZeroLineDispositionRequired)
                return new ActualWorkReviewOutcome(ActualWorkReviewResult.BlockedZeroLineDisposition);

            return new ActualWorkReviewOutcome(ActualWorkReviewResult.VersionMismatch);
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ActualWorkReviewOutcome(ActualWorkReviewResult.VersionMismatch);
        }

        await ResolveWorkSignalIfClearAsync(accountId, actualWork.RequestId, reviewedAtUtc, ct);

        await tx.CommitAsync(ct);
        return new ActualWorkReviewOutcome(ActualWorkReviewResult.Committed, actualWork.ConcurrencyVersion);
    }

    /// <summary>
    /// Per-request aggregate resolve (ADR-463/487): clears the active signal only when no
    /// <c>Submitted</c> visit remains with a null <c>reviewed_at_utc</c> for this request. Reviewing
    /// one of several submitted visits leaves the signal active. Runs after the review commit above,
    /// so this row's own just-persisted <c>reviewed_at_utc</c> is already reflected in the
    /// <c>NOT EXISTS</c> check.
    /// </summary>
    private async Task ResolveWorkSignalIfClearAsync(Guid accountId, Guid requestId, DateTime nowUtc, CancellationToken ct)
    {
        var newConcurrencyVersion = Guid.NewGuid();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE keep_request_work_signals
             SET resolved_at_utc = {nowUtc},
                 concurrency_version = {newConcurrencyVersion},
                 updated_at_utc = {nowUtc}
             WHERE account_id = {accountId}
               AND keep_request_id = {requestId}
               AND source_module_key = {KeepRequestWorkSignalKeys.Modules.PriceBookQuotesMaterials}
               AND signal_key = {KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview}
               AND resolved_at_utc IS NULL
               AND NOT EXISTS (
                   SELECT 1 FROM keep_actual_works
                   WHERE account_id = {accountId}
                     AND request_id = {requestId}
                     AND status = {nameof(ActualWorkStatus.Submitted)}
                     AND reviewed_at_utc IS NULL
                     AND deleted_at_utc IS NULL
               )
             """,
            ct);
    }

    /// <summary>
    /// BL135 §4 Batch 3b-ii binary completeness rule for the hard review gate: every line has both an
    /// effective sell price and an effective direct cost, where "effective" means the captured
    /// snapshot or any financial-resolution row for that line supplying that component. This mirrors
    /// only the boolean <c>IsComplete</c> half of the read-side
    /// <c>ActualWorkFinancialProjection.EffectiveLineFinancials.IsComplete</c> — deliberately not the
    /// value fold, ordering, provenance, or rounding, none of which affect completeness. This is a
    /// deliberate one-way duplication: the read-side projection is an Application internal and
    /// Infrastructure must not consume Application internals, so the rule is restated here rather
    /// than shared. If the definition of a financially-complete line ever changes, the projection is
    /// the other site to keep in step (it does not point back here). A zero-line visit is vacuously
    /// complete — the zero-line no-charge disposition requirement is a separate gate.
    /// </summary>
    private static bool AllLinesFinanciallyComplete(
        IReadOnlyCollection<ActualWorkLine> lines,
        IReadOnlyList<ActualWorkLineFinancialResolution> resolutions) =>
        lines.All(line =>
            (line.SellPriceSnapshot is not null
                || resolutions.Any(r => r.ActualWorkLineId == line.Id && r.ResolvedUnitSellPrice is not null))
            && (line.StandardExpectedDirectCostSnapshot is not null
                || resolutions.Any(r => r.ActualWorkLineId == line.Id
                    && r.ResolvedUnitStandardExpectedDirectCost is not null)));
}
