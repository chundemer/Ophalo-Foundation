using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks CatalogItemLifecycleService (Session 2a.1): create/activate/inactivate orchestration,
/// external-key duplicate pre-check, account-scoped lookup (a wrong-account id resolves to
/// NotFound, never another account's row), and expected-version conflict handling. No
/// current-user/permission/entitlement gating here — that composes at the endpoint layer
/// (Session 2a.2).
/// </summary>
public class CatalogItemLifecycleServiceTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid OtherAccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();

    static CreateCatalogItemCommand Command(string? externalKey = null) =>
        new(AccountId, CatalogItemType.Material, "Drain Pan", "each", "USD", externalKey, null, false, Actor);

    [Fact]
    public async Task CreateDraftAsync_persists_and_returns_the_new_item()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);

        var result = await sut.CreateDraftAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(persistence.Items);
        Assert.Equal(result.Value.Id, persistence.Items[0].Id);
    }

    [Fact]
    public async Task CreateDraftAsync_with_duplicate_external_key_in_account_fails()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        await sut.CreateDraftAsync(Command(externalKey: "SKU-1"), CancellationToken.None);

        var result = await sut.CreateDraftAsync(Command(externalKey: "SKU-1"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.ExternalKeyAlreadyExists, result.Error);
        Assert.Single(persistence.Items);
    }

    [Fact]
    public async Task CreateDraftAsync_with_same_external_key_in_different_account_succeeds()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var existing = CatalogItem.CreateDraft(
            OtherAccountId, CatalogItemType.Material, "Other", "each", "USD", "SKU-1", null, false, Actor).Value;
        persistence.Items.Add(existing);

        var result = await sut.CreateDraftAsync(Command(externalKey: "SKU-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateDraftAsync_when_a_concurrent_insert_wins_the_external_key_race_fails()
    {
        // Simulates two requests both passing the application-level pre-check (neither sees the
        // other's row yet) and racing to insert; the database's unique index lets only one
        // succeed, and the loser's DbUpdateException must translate to the same domain error as
        // the pre-check, not escape as an unhandled exception.
        var persistence = new FakeCatalogItemPersistence { ForceConflictOnNextAdd = true };
        var sut = new CatalogItemLifecycleService(persistence);

        var result = await sut.CreateDraftAsync(Command(externalKey: "SKU-1"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.ExternalKeyAlreadyExists, result.Error);
        Assert.Empty(persistence.Items);
    }

    [Fact]
    public async Task ActivateAsync_with_correct_expected_version_succeeds()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.ActivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogItemActiveState.Active, persistence.Items[0].ActiveState);
    }

    [Fact]
    public async Task ActivateAsync_for_unknown_item_fails_NotFound()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);

        var result = await sut.ActivateAsync(AccountId, Guid.CreateVersion7(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_for_item_in_a_different_account_fails_NotFound()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.ActivateAsync(OtherAccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_with_stale_expected_version_fails_VersionMismatch()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.ActivateAsync(AccountId, created.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task ActivateAsync_surfaces_a_true_commit_conflict_as_VersionMismatch()
    {
        var persistence = new FakeCatalogItemPersistence { ForceConflictOnNextCommit = true };
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.ActivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task InactivateAsync_from_Active_succeeds()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;
        await sut.ActivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);
        var activeVersion = persistence.Items[0].ConcurrencyVersion;

        var result = await sut.InactivateAsync(AccountId, created.Id, activeVersion, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogItemActiveState.Inactive, persistence.Items[0].ActiveState);
    }

    [Fact]
    public async Task InactivateAsync_from_Draft_fails_NotActive()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.InactivateAsync(AccountId, created.Id, created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotActive, result.Error);
    }

    // --- Aliases (Session 2b.2) ---

    [Fact]
    public async Task AddAliasAsync_with_correct_expected_version_succeeds()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.AddAliasAsync(
            AccountId, created.Id, created.ConcurrencyVersion, "Drain Tray", Actor, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(persistence.Items[0].Aliases);
        Assert.Equal("Drain Tray", result.Value.AliasText);
    }

    [Fact]
    public async Task AddAliasAsync_for_unknown_item_fails_NotFound()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);

        var result = await sut.AddAliasAsync(
            AccountId, Guid.CreateVersion7(), Guid.NewGuid(), "Drain Tray", Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task AddAliasAsync_with_stale_expected_version_fails_VersionMismatch()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.AddAliasAsync(
            AccountId, created.Id, Guid.NewGuid(), "Drain Tray", Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task AddAliasAsync_with_duplicate_text_fails_and_does_not_commit()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;
        await sut.AddAliasAsync(
            AccountId, created.Id, created.ConcurrencyVersion, "Drain Tray", Actor, CancellationToken.None);

        var result = await sut.AddAliasAsync(
            AccountId, created.Id, persistence.Items[0].ConcurrencyVersion, "drain tray", Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasAlreadyExists, result.Error);
        Assert.Single(persistence.Items[0].Aliases);
    }

    [Fact]
    public async Task InactivateAliasAsync_from_Active_succeeds()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;
        var alias = (await sut.AddAliasAsync(
            AccountId, created.Id, created.ConcurrencyVersion, "Drain Tray", Actor, CancellationToken.None)).Value;
        var afterAdd = persistence.Items[0].ConcurrencyVersion;

        var result = await sut.InactivateAliasAsync(AccountId, created.Id, alias.Id, afterAdd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Inactive, persistence.Items[0].Aliases.Single().ActiveState);
    }

    [Fact]
    public async Task InactivateAliasAsync_with_unknown_alias_id_fails_AliasNotFound()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;

        var result = await sut.InactivateAliasAsync(
            AccountId, created.Id, Guid.CreateVersion7(), created.ConcurrencyVersion, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CatalogItemErrors.AliasNotFound, result.Error);
    }

    [Fact]
    public async Task ActivateAliasAsync_from_Inactive_succeeds()
    {
        var persistence = new FakeCatalogItemPersistence();
        var sut = new CatalogItemLifecycleService(persistence);
        var created = (await sut.CreateDraftAsync(Command(), CancellationToken.None)).Value;
        var alias = (await sut.AddAliasAsync(
            AccountId, created.Id, created.ConcurrencyVersion, "Drain Tray", Actor, CancellationToken.None)).Value;
        await sut.InactivateAliasAsync(AccountId, created.Id, alias.Id, persistence.Items[0].ConcurrencyVersion, CancellationToken.None);
        var afterInactivate = persistence.Items[0].ConcurrencyVersion;

        var result = await sut.ActivateAliasAsync(AccountId, created.Id, alias.Id, afterInactivate, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogActiveState.Active, persistence.Items[0].Aliases.Single().ActiveState);
    }

    sealed class FakeCatalogItemPersistence : ICatalogItemPersistence
    {
        public List<CatalogItem> Items { get; } = [];
        public bool ForceConflictOnNextCommit { get; set; }
        public bool ForceConflictOnNextAdd { get; set; }

        public Task<CatalogItem?> GetByIdAsync(Guid accountId, Guid catalogItemId, CancellationToken ct) =>
            Task.FromResult(Items.FirstOrDefault(x => x.AccountId == accountId && x.Id == catalogItemId));

        public Task<bool> ExternalKeyExistsAsync(Guid accountId, string externalKey, CancellationToken ct) =>
            Task.FromResult(Items.Any(x => x.AccountId == accountId && x.ExternalKey == externalKey));

        public Task<CatalogItemCommitResult> AddAsync(CatalogItem item, CancellationToken ct)
        {
            if (ForceConflictOnNextAdd)
            {
                ForceConflictOnNextAdd = false;
                return Task.FromResult(CatalogItemCommitResult.Conflict);
            }

            Items.Add(item);
            return Task.FromResult(CatalogItemCommitResult.Committed);
        }

        public Task<CatalogItemCommitResult> CommitAsync(CatalogItem item, CancellationToken ct)
        {
            if (ForceConflictOnNextCommit)
            {
                ForceConflictOnNextCommit = false;
                return Task.FromResult(CatalogItemCommitResult.Conflict);
            }

            return Task.FromResult(CatalogItemCommitResult.Committed);
        }
    }
}
