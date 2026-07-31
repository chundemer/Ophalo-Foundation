using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks CatalogCategoryLifecycleService (Session 2b.1): create/activate/inactivate
/// orchestration, case-insensitive name duplicate pre-check, account-scoped lookup (a
/// wrong-account id resolves to NotFound, never another account's row), and expected-version
/// conflict handling. No current-user/permission/entitlement gating here — that composes at the
/// endpoint layer (Session 2b's API delivery).
/// </summary>
public class CatalogCategoryLifecycleServiceTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid OtherAccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();

    static CreateCatalogCategoryCommand Command(string name = "Water Heaters") =>
        new(AccountId, name, 0, Actor);

    [Fact]
    public async Task CreateAsync_persists_and_returns_the_new_category()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);

        var result = await sut.CreateAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(persistence.Categories);
        Assert.Equal(result.Value.Id, persistence.Categories[0].Id);
    }

    [Fact]
    public async Task CreateAsync_with_duplicate_name_in_account_fails()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);
        await sut.CreateAsync(Command(name: "Water Heaters"), CancellationToken.None);

        var result = await sut.CreateAsync(Command(name: " WATER HEATERS "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NameAlreadyExists, result.Error);
        Assert.Single(persistence.Categories);
    }

    [Fact]
    public async Task CreateAsync_with_same_name_in_different_account_succeeds()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);
        var existing = CatalogCategory.Create(OtherAccountId, "Water Heaters", 0, Actor).Value;
        persistence.Categories.Add(existing);

        var result = await sut.CreateAsync(Command(name: "Water Heaters"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_when_a_concurrent_insert_wins_the_name_race_fails()
    {
        var persistence = new FakeCatalogCategoryPersistence { ForceConflictOnNextAdd = true };
        var sut = new CatalogCategoryLifecycleService(persistence);

        var result = await sut.CreateAsync(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NameAlreadyExists, result.Error);
        Assert.Empty(persistence.Categories);
    }

    [Fact]
    public async Task ActivateAsync_for_unknown_category_fails_NotFound()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);

        var result = await sut.ActivateAsync(AccountId, Guid.CreateVersion7(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_for_category_in_a_different_account_fails_NotFound()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);
        var created = (await sut.CreateAsync(Command(), CancellationToken.None)).Value;
        created.Inactivate();
        await persistence.CommitAsync(created, CancellationToken.None);

        var result = await sut.ActivateAsync(OtherAccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_with_stale_expected_version_fails_VersionMismatch()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);
        var created = (await sut.CreateAsync(Command(), CancellationToken.None)).Value;
        created.Inactivate();
        await persistence.CommitAsync(created, CancellationToken.None);

        var result = await sut.ActivateAsync(AccountId, created.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_surfaces_a_true_commit_conflict_as_VersionMismatch()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);
        var created = (await sut.CreateAsync(Command(), CancellationToken.None)).Value;
        created.Inactivate();
        await persistence.CommitAsync(created, CancellationToken.None);
        persistence.ForceConflictOnNextCommit = true;

        var result = await sut.ActivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task InactivateAsync_from_Active_succeeds()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);
        var created = (await sut.CreateAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.InactivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Inactive, persistence.Categories[0].ActiveState);
    }

    [Fact]
    public async Task InactivateAsync_when_already_Inactive_fails_NotActive()
    {
        var persistence = new FakeCatalogCategoryPersistence();
        var sut = new CatalogCategoryLifecycleService(persistence);
        var created = (await sut.CreateAsync(Command(), CancellationToken.None)).Value;
        created.Inactivate();
        await persistence.CommitAsync(created, CancellationToken.None);

        var result = await sut.InactivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogCategoryErrors.NotActive, result.Error);
    }

    sealed class FakeCatalogCategoryPersistence : ICatalogCategoryPersistence
    {
        public List<CatalogCategory> Categories { get; } = [];
        public bool ForceConflictOnNextCommit { get; set; }
        public bool ForceConflictOnNextAdd { get; set; }

        public Task<CatalogCategory?> GetByIdAsync(Guid accountId, Guid categoryId, CancellationToken ct) =>
            Task.FromResult(Categories.FirstOrDefault(x => x.AccountId == accountId && x.Id == categoryId));

        public Task<bool> NameExistsAsync(Guid accountId, string normalizedName, CancellationToken ct) =>
            Task.FromResult(Categories.Any(x => x.AccountId == accountId && x.NormalizedName == normalizedName));

        public Task<CatalogCategoryCommitResult> AddAsync(CatalogCategory category, CancellationToken ct)
        {
            if (ForceConflictOnNextAdd)
            {
                ForceConflictOnNextAdd = false;
                return Task.FromResult(CatalogCategoryCommitResult.Conflict);
            }

            Categories.Add(category);
            return Task.FromResult(CatalogCategoryCommitResult.Committed);
        }

        public Task<CatalogCategoryCommitResult> CommitAsync(CatalogCategory category, CancellationToken ct)
        {
            if (ForceConflictOnNextCommit)
            {
                ForceConflictOnNextCommit = false;
                return Task.FromResult(CatalogCategoryCommitResult.Conflict);
            }

            return Task.FromResult(CatalogCategoryCommitResult.Committed);
        }
    }
}
