using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks OfferingAssemblyLifecycleService (Session 3.2a.1): atomic create-with-items,
/// activate/inactivate orchestration, account-scoped lookup, referenced-catalog-item existence
/// pre-check, and expected-version conflict handling. No current-user/permission/entitlement
/// gating here — that composes at the endpoint layer (OfferingAssemblyApiService).
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
        assemblies.ForceConflictOnNextCommit = true;

        var result = await sut.InactivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferingAssemblyErrors.VersionMismatch, result.Error);
    }

    sealed class FakeOfferingAssemblyPersistence : IOfferingAssemblyPersistence
    {
        public List<OfferingAssembly> Assemblies { get; } = [];
        public bool ForceConflictOnNextCommit { get; set; }
        public bool ForceConflictOnNextAdd { get; set; }

        public Task<OfferingAssembly?> GetByIdAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct) =>
            Task.FromResult(Assemblies.FirstOrDefault(x => x.AccountId == accountId && x.Id == offeringAssemblyId));

        public Task<OfferingAssemblyCommitResult> AddAsync(OfferingAssembly assembly, CancellationToken ct)
        {
            if (ForceConflictOnNextAdd)
            {
                ForceConflictOnNextAdd = false;
                return Task.FromResult(OfferingAssemblyCommitResult.Conflict);
            }

            Assemblies.Add(assembly);
            return Task.FromResult(OfferingAssemblyCommitResult.Committed);
        }

        public Task<OfferingAssemblyCommitResult> CommitAsync(OfferingAssembly assembly, CancellationToken ct)
        {
            if (ForceConflictOnNextCommit)
            {
                ForceConflictOnNextCommit = false;
                return Task.FromResult(OfferingAssemblyCommitResult.Conflict);
            }

            return Task.FromResult(OfferingAssemblyCommitResult.Committed);
        }

        public Task<bool> IsOperationallyEligibleAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct) =>
            Task.FromResult(false);
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
