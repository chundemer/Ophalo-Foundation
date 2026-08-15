using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>EF implementation of the account's Quick scope action set persistence seam
/// (build-log/119). See <see cref="IQuickScopeActionPersistence"/> for the full contract.</summary>
public sealed class EfQuickScopeActionPersistence(OpHaloDbContext dbContext) : IQuickScopeActionPersistence
{
    public async Task<IReadOnlyList<QuickScopeAction>> ListForAccountAsync(Guid accountId, CancellationToken ct) =>
        await dbContext.Set<QuickScopeAction>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Order)
            .ToListAsync(ct);

    public async Task<QuickScopeActionCommitResult> ReplaceSetAsync(
        Guid accountId, IReadOnlyList<QuickScopeAction> actions, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        await dbContext.Set<QuickScopeAction>()
            .Where(x => x.AccountId == accountId)
            .ExecuteDeleteAsync(ct);

        if (actions.Count > 0)
            dbContext.Set<QuickScopeAction>().AddRange(actions);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            pgEx && pgEx.ConstraintName == "ux_keep_pricebook_quick_scope_actions_account_order")
        {
            return QuickScopeActionCommitResult.OrderConflict;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx &&
            (pgEx.ConstraintName == "ux_keep_pricebook_quick_scope_actions_account_catalog_item" ||
             pgEx.ConstraintName == "ux_keep_pricebook_quick_scope_actions_account_offering_assembly"))
        {
            return QuickScopeActionCommitResult.TargetConflict;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx &&
            pgEx.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return QuickScopeActionCommitResult.ExclusiveTargetViolation;
        }

        await tx.CommitAsync(ct);
        return QuickScopeActionCommitResult.Committed;
    }
}
