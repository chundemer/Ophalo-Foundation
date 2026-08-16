using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks OfferingAssemblyLifecycleService: atomic create-with-items, activate/inactivate
/// orchestration (Session 3.2a.1), header/item live-edit orchestration (Session 3.2b),
/// account-scoped lookup, referenced-catalog-item existence pre-check, and the distinct
/// ConcurrencyConflict/PrimaryCatalogItemAlreadyClaimed commit-result mapping (Session 3.2b fixed
/// a bug where both collapsed into a single VersionMismatch). No current-user/permission/
/// entitlement gating here — that composes at the endpoint layer (OfferingAssemblyApiService).
/// </summary>
public class OfferingAssemblyLifecycleServiceTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid OtherAccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();

    static CatalogItem SeedCatalogItem(FakeCatalogItemPersistence catalogItems, Guid accountId, string name)
    {
        var item = CatalogItem.CreateDraft(accountId, CatalogItemType.Material, name, "each", "USD", null, null, false, Actor).Value;
        catalogItems.Items.Add(item);
        return item;
    }

    static (FakeOfferingAssemblyPersistence Assemblies, FakeCatalogItemPersistence CatalogItems, OfferingAssemblyLifecycleService Sut) Build()
    {
        var assemblies = new FakeOfferingAssemblyPersistence();
        var catalogItems = new FakeCatalogItemPersistence();
        return (assemblies, catalogItems, new OfferingAssemblyLifecycleService(assemblies, catalogItems));
    }

    [Fact]
    public async Task CreateWithItemsAsync_persists_the_assembly_and_every_item_in_one_call()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var labor = SeedCatalogItem(catalogItems, AccountId, "Labor");

        var command = new CreateOfferingAssemblyWithItemsCommand(
            AccountId, primary.Id, "Control Board Replacement", PriceTreatment.Summed,
            [new CreateOfferingAssemblyItemInput(labor.Id, 1m, false, 0)], Actor);

        var result = await sut.CreateWithItemsAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(assemblies.Assemblies);
        Assert.Single(result.Value.Items);
        Assert.Equal(labor.Id, result.Value.Items.First().CatalogItemId);
    }

    [Fact]
    public async Task CreateWithItemsAsync_with_unknown_primary_catalog_item_fails_without_persisting()
    {
        var (assemblies, _, sut) = Build();

        var command = new CreateOfferingAssemblyWithItemsCommand(
            AccountId, Guid.CreateVersion7(), "Control Board Replacement", PriceTreatment.Summed, [], Actor);

        var result = await sut.CreateWithItemsAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
        Assert.Empty(assemblies.Assemblies);
    }

    [Fact]
    public async Task CreateWithItemsAsync_with_another_accounts_catalog_item_fails()
    {
        var (assemblies, catalogItems, sut) = Build();
        var otherAccountItem = SeedCatalogItem(catalogItems, OtherAccountId, "Control Board");

        var command = new CreateOfferingAssemblyWithItemsCommand(
            AccountId, otherAccountItem.Id, "Control Board Replacement", PriceTreatment.Summed, [], Actor);

        var result = await sut.CreateWithItemsAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
        Assert.Empty(assemblies.Assemblies);
    }

    [Fact]
    public async Task CreateWithItemsAsync_with_unknown_item_catalog_item_fails_without_persisting()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");

        var command = new CreateOfferingAssemblyWithItemsCommand(
            AccountId, primary.Id, "Control Board Replacement", PriceTreatment.Summed,
            [new CreateOfferingAssemblyItemInput(Guid.CreateVersion7(), 1m, false, 0)], Actor);

        var result = await sut.CreateWithItemsAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
        Assert.Empty(assemblies.Assemblies);
    }

    [Fact]
    public async Task CreateWithItemsAsync_when_primary_is_already_claimed_by_an_active_assembly_fails()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        assemblies.ForceConflictOnNextAdd = true;

        var command = new CreateOfferingAssemblyWithItemsCommand(
            AccountId, primary.Id, "Control Board Replacement", PriceTreatment.Summed, [], Actor);

        var result = await sut.CreateWithItemsAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyClaimed, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_reactivates_an_inactive_assembly()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;
        created.Inactivate();

        var result = await sut.ActivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Active, created.ActiveState);
    }

    [Fact]
    public async Task InactivateAsync_with_a_wrong_account_id_resolves_to_NotFound()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var result = await sut.InactivateAsync(OtherAccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task InactivateAsync_with_a_stale_expected_version_fails_with_VersionMismatch()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var result = await sut.InactivateAsync(AccountId, created.Id, Guid.CreateVersion7(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task InactivateAsync_when_commit_reports_a_concurrent_change_fails_with_VersionMismatch()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;
        assemblies.ForceConcurrencyConflictOnNextCommit = true;

        var result = await sut.InactivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_when_reactivation_collides_with_another_active_assemblys_primary_fails_with_PrimaryCatalogItemAlreadyClaimed()
    {
        // Locks the Session 3.2b bug fix: this must not be reported as VersionMismatch.
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;
        created.Inactivate();
        assemblies.ForcePrimaryClaimedOnNextCommit = true;

        var result = await sut.ActivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyClaimed, result.Error);
    }

    [Fact]
    public async Task UpdateHeaderAsync_renames_reprices_and_repoints_the_primary()
    {
        var (assemblies, catalogItems, sut) = Build();
        var originalPrimary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var newPrimary = SeedCatalogItem(catalogItems, AccountId, "New Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, originalPrimary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var result = await sut.UpdateHeaderAsync(
            new UpdateOfferingAssemblyHeaderCommand(
                AccountId, created.Id, created.ConcurrencyVersion, newPrimary.Id, "New Name", PriceTreatment.AllInclusive),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newPrimary.Id, created.PrimaryCatalogItemId);
        Assert.Equal("New Name", created.Name);
        Assert.Equal(PriceTreatment.AllInclusive, created.PriceTreatment);
    }

    [Fact]
    public async Task UpdateHeaderAsync_with_an_unknown_new_primary_fails_without_mutating()
    {
        var (assemblies, catalogItems, sut) = Build();
        var originalPrimary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, originalPrimary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var result = await sut.UpdateHeaderAsync(
            new UpdateOfferingAssemblyHeaderCommand(
                AccountId, created.Id, created.ConcurrencyVersion, Guid.CreateVersion7(), "New Name", PriceTreatment.Summed),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
        Assert.Equal(originalPrimary.Id, created.PrimaryCatalogItemId);
    }

    [Fact]
    public async Task UpdateHeaderAsync_when_repointing_collides_with_another_assemblys_primary_fails_with_PrimaryCatalogItemAlreadyClaimed()
    {
        var (assemblies, catalogItems, sut) = Build();
        var originalPrimary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var claimedPrimary = SeedCatalogItem(catalogItems, AccountId, "Claimed Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, originalPrimary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;
        assemblies.ForcePrimaryClaimedOnNextCommit = true;

        var result = await sut.UpdateHeaderAsync(
            new UpdateOfferingAssemblyHeaderCommand(
                AccountId, created.Id, created.ConcurrencyVersion, claimedPrimary.Id, "Name", PriceTreatment.Summed),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyClaimed, result.Error);
    }

    [Fact]
    public async Task AddItemAsync_returns_the_new_item_id_and_the_new_assembly_version()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var labor = SeedCatalogItem(catalogItems, AccountId, "Labor");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var versionBeforeAdd = created.ConcurrencyVersion;

        var result = await sut.AddItemAsync(
            new AddOfferingAssemblyItemCommand(AccountId, created.Id, versionBeforeAdd, labor.Id, 2m, false, 0, Actor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(versionBeforeAdd, result.Value.AssemblyConcurrencyVersion);
        Assert.Equal(created.ConcurrencyVersion, result.Value.AssemblyConcurrencyVersion);
        Assert.Contains(created.Items, i => i.Id == result.Value.ItemId && i.CatalogItemId == labor.Id);
    }

    [Fact]
    public async Task AddItemAsync_with_an_unknown_catalog_item_fails_without_mutating()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var result = await sut.AddItemAsync(
            new AddOfferingAssemblyItemCommand(AccountId, created.Id, created.ConcurrencyVersion, Guid.CreateVersion7(), 1m, false, 0, Actor),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
        Assert.Empty(created.Items);
    }

    [Fact]
    public async Task UpdateItemAsync_updates_quantity_optional_and_display_order()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var labor = SeedCatalogItem(catalogItems, AccountId, "Labor");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed,
                [new CreateOfferingAssemblyItemInput(labor.Id, 1m, false, 0)], Actor),
            CancellationToken.None)).Value;
        var itemId = created.Items.Single().Id;

        var result = await sut.UpdateItemAsync(
            new UpdateOfferingAssemblyItemCommand(AccountId, created.Id, created.ConcurrencyVersion, itemId, 5m, true, 3),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = created.Items.Single();
        Assert.Equal(5m, item.DefaultQuantity);
        Assert.True(item.IsOptional);
        Assert.Equal(3, item.DisplayOrder);
    }

    [Fact]
    public async Task UpdateItemAsync_for_an_unknown_item_id_fails_with_ItemNotFound()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var result = await sut.UpdateItemAsync(
            new UpdateOfferingAssemblyItemCommand(AccountId, created.Id, created.ConcurrencyVersion, Guid.CreateVersion7(), 1m, false, 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public async Task RemoveItemAsync_removes_the_item()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var labor = SeedCatalogItem(catalogItems, AccountId, "Labor");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed,
                [new CreateOfferingAssemblyItemInput(labor.Id, 1m, false, 0)], Actor),
            CancellationToken.None)).Value;
        var itemId = created.Items.Single().Id;

        var result = await sut.RemoveItemAsync(AccountId, created.Id, created.ConcurrencyVersion, itemId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(created.Items);
    }

    [Fact]
    public async Task RemoveItemAsync_with_a_wrong_account_id_resolves_to_NotFound()
    {
        var (assemblies, catalogItems, sut) = Build();
        var primary = SeedCatalogItem(catalogItems, AccountId, "Control Board");
        var created = (await sut.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(AccountId, primary.Id, "Name", PriceTreatment.Summed, [], Actor),
            CancellationToken.None)).Value;

        var result = await sut.RemoveItemAsync(OtherAccountId, created.Id, created.ConcurrencyVersion, Guid.CreateVersion7(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.NotFound, result.Error);
    }

    sealed class FakeOfferingAssemblyPersistence : IOfferingAssemblyPersistence
    {
        public List<OfferingAssembly> Assemblies { get; } = [];
        public bool ForceConcurrencyConflictOnNextCommit { get; set; }
        public bool ForcePrimaryClaimedOnNextCommit { get; set; }
        public bool ForceConflictOnNextAdd { get; set; }

        public Task<OfferingAssembly?> GetByIdAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct) =>
            Task.FromResult(Assemblies.FirstOrDefault(x => x.AccountId == accountId && x.Id == offeringAssemblyId));

        public Task<OfferingAssemblyCommitResult> AddAsync(OfferingAssembly assembly, CancellationToken ct)
        {
            if (ForceConflictOnNextAdd)
            {
                ForceConflictOnNextAdd = false;
                return Task.FromResult(OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed);
            }

            Assemblies.Add(assembly);
            return Task.FromResult(OfferingAssemblyCommitResult.Committed);
        }

        public Task<OfferingAssemblyCommitResult> CommitAsync(OfferingAssembly assembly, CancellationToken ct)
        {
            if (ForceConcurrencyConflictOnNextCommit)
            {
                ForceConcurrencyConflictOnNextCommit = false;
                return Task.FromResult(OfferingAssemblyCommitResult.ConcurrencyConflict);
            }

            if (ForcePrimaryClaimedOnNextCommit)
            {
                ForcePrimaryClaimedOnNextCommit = false;
                return Task.FromResult(OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed);
            }

            return Task.FromResult(OfferingAssemblyCommitResult.Committed);
        }

        public Task<bool> IsOperationallyEligibleAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<OfferingAssemblyListRow>> ListAsync(
            Guid accountId, OfferingAssemblyListFilters filters, OfferingAssemblyListCursorPosition? cursor, int fetchCount, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by OfferingAssemblyLifecycleServiceTests.");

        public Task<OfferingAssemblyDetail?> GetDetailAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by OfferingAssemblyLifecycleServiceTests.");

        public Task<IReadOnlyList<OfferingAssemblySearchRow>> SearchAsync(
            Guid accountId, string searchTerm, OfferingAssemblySearchCursorPosition? cursor, int fetchCount, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by OfferingAssemblyLifecycleServiceTests.");

        public Task<OfferingAssemblyEligibility> GetEligibilityAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by OfferingAssemblyLifecycleServiceTests.");

        public Task<IReadOnlyList<OfferingAssemblyDependencyRow>> ListActiveAssembliesReferencingCatalogItemAsync(
            Guid accountId, Guid catalogItemId, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by OfferingAssemblyLifecycleServiceTests.");
    }

    sealed class FakeCatalogItemPersistence : ICatalogItemPersistence
    {
        public List<CatalogItem> Items { get; } = [];

        public Task<CatalogItem?> GetByIdAsync(Guid accountId, Guid catalogItemId, CancellationToken ct) =>
            Task.FromResult(Items.FirstOrDefault(x => x.AccountId == accountId && x.Id == catalogItemId));

        public Task<bool> NormalizedExternalKeyExistsAsync(Guid accountId, string normalizedExternalKey, CancellationToken ct) =>
            Task.FromResult(Items.Any(x => x.AccountId == accountId && x.NormalizedExternalKey == normalizedExternalKey));

        public Task<CatalogItemCommitResult> AddAsync(CatalogItem item, CancellationToken ct)
        {
            Items.Add(item);
            return Task.FromResult(CatalogItemCommitResult.Committed);
        }

        public Task<CatalogItemCommitResult> CommitAsync(CatalogItem item, CancellationToken ct) =>
            Task.FromResult(CatalogItemCommitResult.Committed);
    }
}
