using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>EF implementation of the account's per-rule <see cref="ActualWorkNudgeRule"/>
/// persistence seam (5d-ii-a2, build-log/129). See <see cref="IActualWorkNudgeRulePersistence"/> for
/// the full contract.</summary>
public sealed class EfActualWorkNudgeRulePersistence(OpHaloDbContext dbContext) : IActualWorkNudgeRulePersistence
{
    public Task<ActualWorkNudgeRule?> GetByIdAsync(Guid accountId, Guid ruleId, CancellationToken ct) =>
        dbContext.Set<ActualWorkNudgeRule>()
            .Include(x => x.Suggestions)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == ruleId, ct);

    public async Task<IReadOnlyList<ActualWorkNudgeRule>> ListForAccountAsync(Guid accountId, CancellationToken ct) =>
        await dbContext.Set<ActualWorkNudgeRule>()
            .Include(x => x.Suggestions)
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<ActualWorkNudgeRule?> GetByTriggerAsync(
        Guid accountId, Guid? triggerCatalogItemId, Guid? triggerOfferingAssemblyId, CancellationToken ct) =>
        dbContext.Set<ActualWorkNudgeRule>()
            .Include(x => x.Suggestions)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.AccountId == accountId
                    && x.TriggerCatalogItemId == triggerCatalogItemId
                    && x.TriggerOfferingAssemblyId == triggerOfferingAssemblyId,
                ct);

    public async Task<ActualWorkNudgeRuleCommitResult> CreateAsync(ActualWorkNudgeRule rule, CancellationToken ct)
    {
        dbContext.Set<ActualWorkNudgeRule>().Add(rule);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ActualWorkNudgeRuleCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsDuplicateTriggerViolation(ex))
        {
            return ActualWorkNudgeRuleCommitResult.DuplicateTrigger;
        }
    }

    public Task SaveAsync(ActualWorkNudgeRule rule, CancellationToken ct) =>
        dbContext.SaveChangesAsync(ct);

    public async Task DeleteAsync(Guid accountId, Guid ruleId, CancellationToken ct)
    {
        await dbContext.Set<ActualWorkNudgeRule>()
            .Where(x => x.AccountId == accountId && x.Id == ruleId)
            .ExecuteDeleteAsync(ct);
    }

    private static bool IsDuplicateTriggerViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx &&
        (pgEx.ConstraintName == "ux_keep_aw_nudge_rules_trigger_catalog_item" ||
         pgEx.ConstraintName == "ux_keep_aw_nudge_rules_trigger_offering_assembly");
}
