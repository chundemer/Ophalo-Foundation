using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the atomic supersede/signal-reconciliation operation (ADR-494 D4/D6/D6b).
/// See <see cref="IActualWorkSupersessionPersistence"/> for the full contract. Mirrors
/// <see cref="EfActualWorkSubmissionPersistence"/>: default (Read Committed) isolation, the source
/// visit's own optimistic-concurrency token gates its write, and the ADR-463 signal resolve is
/// delegated to the shared <see cref="IActualWorkReviewSignalReconciliation"/> seam (ADR-494 D4)
/// whose single statement auto-enlists in this open transaction.
/// </summary>
public sealed class EfActualWorkSupersessionPersistence(
    OpHaloDbContext dbContext,
    IActualWorkReviewSignalReconciliation signalReconciliation) : IActualWorkSupersessionPersistence
{
    public async Task<ActualWorkSupersessionOutcome> SupersedeAsync(
        Guid accountId,
        Guid sourceActualWorkId,
        Guid expectedSourceVersion,
        ActualWork successor,
        Guid bySupersedingAccountUserId,
        string reason,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        var source = await dbContext.Set<ActualWork>()
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == sourceActualWorkId, ct);
        if (source is null)
            return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.NotFound);

        if (source.ConcurrencyVersion != expectedSourceVersion)
            return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.VersionMismatch);

        var supersedeResult = source.Supersede(successor.Id, bySupersedingAccountUserId, reason, nowUtc);
        if (supersedeResult.IsFailure)
        {
            var error = supersedeResult.Error;
            if (error == ActualWorkErrors.SupersessionReasonRequired)
                return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.ReasonRequired);
            if (error == ActualWorkErrors.SupersessionReasonTooLong)
                return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.ReasonTooLong);
            if (error == ActualWorkErrors.NotSubmitted)
                return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.SourceNotSubmitted);
            if (error == ActualWorkErrors.AlreadyReviewed)
                return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.SourceAlreadyReviewed);

            // ActualWorkErrors.AlreadySuperseded — the only remaining Supersede failure.
            return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.SourceAlreadySuperseded);
        }

        dbContext.Set<ActualWork>().Add(successor);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.VersionMismatch);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // The request already has an open Draft — the replacement Draft violates the open-Draft
            // partial unique index (ADR-494 D6).
            return new ActualWorkSupersessionOutcome(ActualWorkSupersessionResult.DraftAlreadyOpenForRequest);
        }

        // Resolve-if-clear runs after the source's superseded marker is persisted, so the widened
        // "open outstanding review" predicate (4e-i: AND superseded_at_utc IS NULL) already sees it.
        await signalReconciliation.ResolveIfClearAsync(accountId, source.RequestId, nowUtc, ct);

        await tx.CommitAsync(ct);
        return new ActualWorkSupersessionOutcome(
            ActualWorkSupersessionResult.Committed, source.ConcurrencyVersion, successor.Id);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
