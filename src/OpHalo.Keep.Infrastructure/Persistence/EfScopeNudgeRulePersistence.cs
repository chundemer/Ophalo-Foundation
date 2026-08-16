using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>EF implementation of the account's per-rule <see cref="ScopeNudgeRule"/> persistence
/// seam (build-log/123). See <see cref="IScopeNudgeRulePersistence"/> for the full contract.</summary>
public sealed class EfScopeNudgeRulePersistence(OpHaloDbContext dbContext) : IScopeNudgeRulePersistence
{
    public Task<ScopeNudgeRule?> GetByIdAsync(Guid accountId, Guid ruleId, CancellationToken ct) =>
        dbContext.Set<ScopeNudgeRule>()
            .Include(x => x.Suggestions)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == ruleId, ct);

    public async Task<IReadOnlyList<ScopeNudgeRule>> ListForAccountAsync(Guid accountId, CancellationToken ct) =>
        await dbContext.Set<ScopeNudgeRule>()
            .Include(x => x.Suggestions)
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<ScopeNudgeRule?> GetByTriggerAsync(
        Guid accountId, Guid? triggerCatalogItemId, Guid? triggerOfferingAssemblyId, CancellationToken ct) =>
        dbContext.Set<ScopeNudgeRule>()
            .Include(x => x.Suggestions)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.AccountId == accountId
                    && x.TriggerCatalogItemId == triggerCatalogItemId
                    && x.TriggerOfferingAssemblyId == triggerOfferingAssemblyId,
                ct);

    public async Task<ScopeNudgeRuleCommitResult> CreateAsync(ScopeNudgeRule rule, CancellationToken ct)
    {
        dbContext.Set<ScopeNudgeRule>().Add(rule);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ScopeNudgeRuleCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsDuplicateTriggerViolation(ex))
        {
            return ScopeNudgeRuleCommitResult.DuplicateTrigger;
        }
    }

    public Task SaveAsync(ScopeNudgeRule rule, CancellationToken ct) =>
        dbContext.SaveChangesAsync(ct);

    public async Task DeleteAsync(Guid accountId, Guid ruleId, CancellationToken ct)
    {
        await dbContext.Set<ScopeNudgeRule>()
            .Where(x => x.AccountId == accountId && x.Id == ruleId)
            .ExecuteDeleteAsync(ct);
    }

    private static bool IsDuplicateTriggerViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx &&
        (pgEx.ConstraintName == "ux_keep_scope_nudge_rules_trigger_catalog_item" ||
         pgEx.ConstraintName == "ux_keep_scope_nudge_rules_trigger_offering_assembly");
}
