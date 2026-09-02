using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of <see cref="IActualWorkFinancialResolutionPersistence"/> (build-log/135 §4
/// Batch 2). Reads are <c>account_id</c>-filtered in the query and returned untracked. The append
/// methods only <c>Add</c> to the tracked set. <see cref="CreateResolutionAsync"/> (Batch 3a-ii)
/// is the transactional orchestrator that composes <see cref="AddResolutionAsync"/> inside the
/// visit load / version / review-state guard boundary; Batch 3b-i adds the disposition orchestrator.
/// </summary>
public sealed class EfActualWorkFinancialResolutionPersistence(OpHaloDbContext dbContext)
    : IActualWorkFinancialResolutionPersistence
{
    public async Task<IReadOnlyList<ActualWorkLineFinancialResolution>> GetResolutionsForVisitAsync(
        Guid accountId, Guid actualWorkId, CancellationToken ct) =>
        await dbContext.Set<ActualWorkLineFinancialResolution>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ActualWorkId == actualWorkId)
            .OrderByDescending(x => x.ResolvedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActualWorkOfficeFinancialDisposition>> GetDispositionsForVisitAsync(
        Guid accountId, Guid actualWorkId, CancellationToken ct) =>
        await dbContext.Set<ActualWorkOfficeFinancialDisposition>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ActualWorkId == actualWorkId)
            .OrderByDescending(x => x.DisposedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActualWorkLineFinancialResolution>>> GetResolutionsForVisitsAsync(
        Guid accountId, IReadOnlyCollection<Guid> actualWorkIds, CancellationToken ct)
    {
        if (actualWorkIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ActualWorkLineFinancialResolution>>();

        var ids = actualWorkIds.ToArray();
        var rows = await dbContext.Set<ActualWorkLineFinancialResolution>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.ActualWorkId))
            .OrderByDescending(x => x.ResolvedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ActualWorkId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ActualWorkLineFinancialResolution>)g.ToArray());
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActualWorkOfficeFinancialDisposition>>> GetDispositionsForVisitsAsync(
        Guid accountId, IReadOnlyCollection<Guid> actualWorkIds, CancellationToken ct)
    {
        if (actualWorkIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ActualWorkOfficeFinancialDisposition>>();

        var ids = actualWorkIds.ToArray();
        var rows = await dbContext.Set<ActualWorkOfficeFinancialDisposition>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.ActualWorkId))
            .OrderByDescending(x => x.DisposedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ActualWorkId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ActualWorkOfficeFinancialDisposition>)g.ToArray());
    }

    public async Task<ActualWorkResolutionOutcome> CreateResolutionAsync(
        ActualWorkLineFinancialResolution resolution, Guid expectedVisitVersion, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        var visit = await dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == resolution.AccountId && x.Id == resolution.ActualWorkId, ct);
        if (visit is null)
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.VisitNotFound);

        if (visit.ConcurrencyVersion != expectedVisitVersion)
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.VersionMismatch);

        // BL136 D6c (slice 4e-ii-b-2) — a superseded source is inert, checked immediately after the
        // version check so a stale client reloads for the more general reason first.
        if (visit.SupersededAtUtc is not null)
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.Superseded);

        if (visit.Status != ActualWorkStatus.Submitted)
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.VisitNotSubmitted);

        if (visit.ReviewedAtUtc is not null)
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.VisitAlreadyReviewed);

        var line = visit.Lines.FirstOrDefault(l => l.Id == resolution.ActualWorkLineId);
        if (line is null)
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.LineNotFoundOnVisit);

        if ((resolution.ResolvedUnitSellPrice is not null && line.SellPriceSnapshot is not null) ||
            (resolution.ResolvedUnitStandardExpectedDirectCost is not null && line.StandardExpectedDirectCostSnapshot is not null))
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.SnapshotComponentAlreadyValid);

        await AddResolutionAsync(resolution, ct);
        visit.RefreshConcurrencyVersionForFinancialResolution();

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.VersionMismatch);
        }

        await tx.CommitAsync(ct);
        return new ActualWorkResolutionOutcome(ActualWorkResolutionResult.Committed, visit.ConcurrencyVersion);
    }

    public async Task<ActualWorkDispositionOutcome> RecordDispositionAsync(
        ActualWorkOfficeFinancialDisposition disposition, Guid expectedVisitVersion, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        var visit = await dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == disposition.AccountId && x.Id == disposition.ActualWorkId, ct);
        if (visit is null)
            return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.VisitNotFound);

        if (visit.ConcurrencyVersion != expectedVisitVersion)
            return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.VersionMismatch);

        // BL136 D6c (slice 4e-ii-b-2) — a superseded source is inert, checked immediately after the
        // version check so a stale client reloads for the more general reason first.
        if (visit.SupersededAtUtc is not null)
            return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.Superseded);

        if (visit.Status != ActualWorkStatus.Submitted)
            return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.VisitNotSubmitted);

        if (visit.ReviewedAtUtc is not null)
            return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.VisitAlreadyReviewed);

        if (visit.Lines.Count > 0)
            return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.VisitHasLines);

        await AddDispositionAsync(disposition, ct);
        visit.RefreshConcurrencyVersionForOfficeFinancialDisposition();

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.VersionMismatch);
        }

        await tx.CommitAsync(ct);
        return new ActualWorkDispositionOutcome(ActualWorkDispositionResult.Committed, visit.ConcurrencyVersion);
    }

    public Task AddResolutionAsync(ActualWorkLineFinancialResolution resolution, CancellationToken ct)
    {
        dbContext.Set<ActualWorkLineFinancialResolution>().Add(resolution);
        return Task.CompletedTask;
    }

    public Task AddDispositionAsync(ActualWorkOfficeFinancialDisposition disposition, CancellationToken ct)
    {
        dbContext.Set<ActualWorkOfficeFinancialDisposition>().Add(disposition);
        return Task.CompletedTask;
    }
}
