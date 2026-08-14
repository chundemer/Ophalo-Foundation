using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the atomic <c>expand-assembly</c> transaction (Session 3.4e). See
/// <see cref="IOfferingAssemblyExpansionPersistence"/> for the full contract and
/// build-log/118's "Assembly-expansion locking protocol" for the lock-order rationale. Default
/// (Read Committed) isolation: every row this operation reads for a decision is first taken under
/// an explicit <c>SELECT ... FOR UPDATE</c>, so the decision always reflects the latest committed
/// state at the moment of the check, not a snapshot from before the lock.
/// </summary>
public sealed class EfOfferingAssemblyExpansionPersistence(
    OpHaloDbContext dbContext, IOfferingAssemblyPersistence assemblyPersistence) : IOfferingAssemblyExpansionPersistence
{
    /// <summary>
    /// Test-only seam (Session 3.4e): invoked after the <c>ProposedScope</c> row lock is taken and
    /// version/status-checked, immediately before the <c>OfferingAssembly</c>/<c>CatalogItem</c>
    /// locks are acquired and eligibility is recomputed. Lets an integration test pause here, mutate
    /// and commit the assembly's primary item on a second connection (no lock on it exists yet at
    /// this point, so that write is free to proceed), then resume — proving the eligibility recheck
    /// below reads the just-committed change rather than a stale pre-transaction snapshot. No-op in
    /// production — never set by DI-resolved production code, only by a test holding a direct
    /// reference to this concrete type. Public (not internal) because no cross-project
    /// InternalsVisibleTo exists between this assembly and the integration-test project.
    /// </summary>
    public Func<CancellationToken, Task>? PostScopeLockHook { get; set; }

    public async Task<ExpandAssemblyOutcome> ExpandAsync(
        Guid accountId,
        Guid proposedScopeId,
        Guid expectedVersion,
        Guid offeringAssemblyId,
        IReadOnlyCollection<Guid> excludedOptionalItemIds,
        Guid createdByUserId,
        CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        // Step 1: lock and validate the ProposedScope — mirrors EfProposedScopeSubmissionPersistence.
        var scope = await dbContext.Set<ProposedScope>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_pricebook_proposed_scopes
                 WHERE account_id = {accountId} AND id = {proposedScopeId} AND deleted_at_utc IS NULL
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(ct);
        if (scope is null)
            return new ExpandAssemblyOutcome(ExpandAssemblyResult.ScopeNotFound);
        if (scope.ConcurrencyVersion != expectedVersion)
            return new ExpandAssemblyOutcome(ExpandAssemblyResult.VersionMismatch);
        if (scope.Status != ProposedScopeStatus.Draft)
            return new ExpandAssemblyOutcome(ExpandAssemblyResult.NotDraft);

        if (PostScopeLockHook is not null)
            await PostScopeLockHook(ct);

        // Step 2: lock the OfferingAssembly, then every referenced CatalogItem (primary + all
        // associated items) in ascending id order.
        var assembly = await dbContext.Set<OfferingAssembly>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_pricebook_offering_assemblies
                 WHERE account_id = {accountId} AND id = {offeringAssemblyId} AND deleted_at_utc IS NULL
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .Include(x => x.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);
        if (assembly is null)
            return new ExpandAssemblyOutcome(ExpandAssemblyResult.AssemblyNotFound);

        var invalidExclusion = excludedOptionalItemIds.Any(excludedId =>
            assembly.Items.FirstOrDefault(i => i.Id == excludedId) is not { IsOptional: true });
        if (invalidExclusion)
            return new ExpandAssemblyOutcome(ExpandAssemblyResult.InvalidExclusion);

        var referencedCatalogItemIds = new List<Guid> { assembly.PrimaryCatalogItemId };
        referencedCatalogItemIds.AddRange(assembly.Items.Select(i => i.CatalogItemId));
        var lockOrderedIds = referencedCatalogItemIds.Distinct().OrderBy(id => id).ToList();

        await dbContext.Set<CatalogItem>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_pricebook_catalog_items
                 WHERE account_id = {accountId} AND id = ANY({lockOrderedIds}::uuid[]) AND deleted_at_utc IS NULL
                 ORDER BY id
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        // Step 3: recompute ADR-479 eligibility from the just-locked rows, not the caller's
        // pre-transaction read.
        var isEligible = await assemblyPersistence.IsOperationallyEligibleAsync(accountId, offeringAssemblyId, ct);
        if (!isEligible)
            return new ExpandAssemblyOutcome(ExpandAssemblyResult.AssemblyNotOperationallyEligible);

        var catalogItems = await dbContext.Set<CatalogItem>()
            .Where(c => c.AccountId == accountId && lockOrderedIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        // Step 4: append lines and bump the scope's version once.
        var displayOrder = scope.Lines.Count == 0 ? 10 : scope.Lines.Max(l => l.DisplayOrder) + 10;
        var lineIds = new List<Guid>();

        var primaryItem = catalogItems[assembly.PrimaryCatalogItemId];
        var primaryAddResult = scope.AddLine(
            ProposedScopeLineType.PrimaryOffering, primaryItem.Id, assembly.Id, quantity: 1m,
            isException: false, offCatalogDescription: null, offCatalogQuantity: null, note: null,
            displayOrder, primaryItem.DisplayName, primaryItem.UnitOfMeasure,
            assembly.Name, defaultQuantitySnapshot: null, createdByUserId);
        if (primaryAddResult.IsFailure)
            return new ExpandAssemblyOutcome(ExpandAssemblyResult.NotDraft);
        lineIds.Add(primaryAddResult.Value.Id);
        displayOrder += 10;

        foreach (var item in assembly.Items.Where(i => !excludedOptionalItemIds.Contains(i.Id)))
        {
            var catalogItem = catalogItems[item.CatalogItemId];
            var addResult = scope.AddLine(
                ProposedScopeLineType.AssociatedItem, catalogItem.Id, assembly.Id, item.DefaultQuantity,
                isException: false, offCatalogDescription: null, offCatalogQuantity: null, note: null,
                displayOrder, catalogItem.DisplayName, catalogItem.UnitOfMeasure,
                assembly.Name, item.DefaultQuantity, createdByUserId);
            if (addResult.IsFailure)
                return new ExpandAssemblyOutcome(ExpandAssemblyResult.NotDraft);
            lineIds.Add(addResult.Value.Id);
            displayOrder += 10;
        }

        await dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ExpandAssemblyOutcome(ExpandAssemblyResult.Committed, lineIds, scope.ConcurrencyVersion);
    }
}
