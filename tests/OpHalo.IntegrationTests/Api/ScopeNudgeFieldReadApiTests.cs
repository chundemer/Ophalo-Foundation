using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 3's Paired Nudges field-read contract (build-log/123):
///   GET /keep/pricebook/proposed-scopes/{proposedScopeId}/nudge-suggestions
///
/// Covers the required trigger-parameter shape (missing/duplicate/combined -> 400), read-time
/// eligibility filtering of the trigger and each suggestion, Draft-line dedupe, and the gates ->
/// request visibility -> act ordering shared with the sibling field-select/field-read endpoints.
/// </summary>
public sealed class ScopeNudgeFieldReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ScopeNudgeFieldReadApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetSuggestions_HappyPath_ReturnsOrderedEligibleSurvivingSuggestions()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-happy-path");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var suggestionOne = await SeedActiveCatalogItemAsync(accountId, ownerId, "Suggestion One");
        var suggestionTwo = await SeedActiveCatalogItemAsync(accountId, ownerId, "Suggestion Two");
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null,
            (suggestionOne.Id, null), (suggestionTwo.Id, null));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Equal(2, suggestions.Count);
        Assert.Equal(suggestionOne.Id, suggestions[0].GetProperty("catalogItemId").GetGuid());
        Assert.Equal(suggestionTwo.Id, suggestions[1].GetProperty("catalogItemId").GetGuid());
        Assert.False(suggestions[0].TryGetProperty("isEligible", out _));
        Assert.False(suggestions[0].TryGetProperty("price", out _));
    }

    [Fact]
    public async Task GetSuggestions_AssemblySuggestion_ReturnsEligibleOfferingAssemblyTarget()
    {
        // The endpoint is polymorphic (catalog item or offering/assembly on either side); this pins
        // the offering-assembly branch of both trigger resolution and suggestion-row shaping.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-assembly-suggestion");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var primaryItem = await SeedPricedCatalogItemAsync(accountId, ownerId, "Assembly Primary Item");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primaryItem.Id);
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null, (null, assemblyId));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Single(suggestions);
        Assert.Equal(assemblyId, suggestions[0].GetProperty("offeringAssemblyId").GetGuid());
        Assert.Equal(JsonValueKind.Null, suggestions[0].GetProperty("catalogItemId").ValueKind);
        Assert.Equal("OfferingAssembly", suggestions[0].GetProperty("targetKind").GetString());
    }

    [Fact]
    public async Task GetSuggestions_IneligibleSuggestionTarget_IsOmittedNotMarked()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-ineligible-suggestion");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var eligibleSuggestion = await SeedActiveCatalogItemAsync(accountId, ownerId, "Eligible Suggestion");
        var ineligibleSuggestion = await SeedActiveCatalogItemAsync(accountId, ownerId, "Ineligible Suggestion");
        await InactivateAsync(ineligibleSuggestion.Id);
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null,
            (eligibleSuggestion.Id, null), (ineligibleSuggestion.Id, null));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Single(suggestions);
        Assert.Equal(eligibleSuggestion.Id, suggestions[0].GetProperty("catalogItemId").GetGuid());
    }

    [Fact]
    public async Task GetSuggestions_SuggestionAlreadyOnAnActiveDraftLine_IsOmitted()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-draft-dedupe");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, catalogItemOnDraft, _) = await SeedDraftScopeWithLineAsync(accountId, ownerId);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null, (catalogItemOnDraft, null));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
        Assert.True(body.GetProperty("ruleId").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSuggestions_IneligibleTrigger_ReturnsEmptyResult()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-ineligible-trigger");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var suggestion = await SeedActiveCatalogItemAsync(accountId, ownerId, "Suggestion");
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null, (suggestion.Id, null));
        await InactivateAsync(trigger.Id);

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
    }

    [Fact]
    public async Task GetSuggestions_NoRuleConfiguredForTrigger_ReturnsEmptyResult()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-no-rule");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
    }

    [Fact]
    public async Task GetSuggestions_MissingTriggerParameter_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-missing-trigger");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TriggerQueryParameterInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_CombinedTriggerParameters_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-combined-trigger");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var catalogItemId = Guid.NewGuid();
        var offeringAssemblyId = Guid.NewGuid();

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions" +
            $"?triggerCatalogItemId={catalogItemId}&triggerOfferingAssemblyId={offeringAssemblyId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TriggerQueryParameterInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_DuplicateTriggerParameter_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-duplicate-trigger");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var catalogItemId = Guid.NewGuid();

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions" +
            $"?triggerCatalogItemId={catalogItemId}&triggerCatalogItemId={catalogItemId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TriggerQueryParameterInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_OperatorNotAssignedToRequest_Returns404()
    {
        // Guards the locked gates -> request visibility -> act ordering (build-log/118/123): an
        // invisible scope 404s as KeepRequest.NotFound before the trigger is ever evaluated.
        var (accountId, ownerId, _) = await SeedAccountAsync("nudge-operator-mywork");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "nudge-operator-mywork");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);

        var response = await AuthRequest(operatorCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("nudge-no-entitlement");
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedRuleAsync(
        Guid accountId, Guid createdByUserId, Guid? triggerCatalogItemId, Guid? triggerOfferingAssemblyId,
        params (Guid? CatalogItemId, Guid? OfferingAssemblyId)[] suggestions)
    {
        var orderedSuggestions = suggestions
            .Select((s, i) => (s.CatalogItemId, s.OfferingAssemblyId))
            .ToList();
        var createResult = ScopeNudgeRule.Create(
            accountId, triggerCatalogItemId, triggerOfferingAssemblyId, orderedSuggestions, createdByUserId);
        Assert.True(createResult.IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ScopeNudgeRule>().Add(createResult.Value);
        await db.SaveChangesAsync();
    }

    private async Task InactivateAsync(Guid catalogItemId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var item = await db.Set<CatalogItem>().SingleAsync(x => x.Id == catalogItemId);
        Assert.True(item.Inactivate().IsSuccess);
        await db.SaveChangesAsync();
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Nudge Field Read Test Co {slug}",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graph = result.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFkEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFkEntry.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFkEntry.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        var ownerCookie = await GetCookieAsync(graph.Owner.Id, graph.Account.Id);
        return (graph.Account.Id, graph.Owner.Id, ownerCookie);
    }

    private async Task<Guid> SeedOperatorAsync(Guid accountId, string slug)
    {
        var now = DateTime.UtcNow;
        var email = $"operator@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), AccountUserRole.Operator,
            inviteTokenHash: $"{slug}_operator_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        member.Activate(user.Id, now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task EnrollAsync(Guid accountId, Guid changedByAccountUserId)
    {
        var now = DateTime.UtcNow;
        var enrollResult = AccountCapabilityPackageEnrollment.Enroll(
            accountId, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, changedByAccountUserId, now);
        Assert.True(enrollResult.IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.AccountCapabilityPackageEnrollments.Add(enrollResult.Value);
        await db.SaveChangesAsync();
    }

    private async Task<string> GetCookieAsync(Guid accountUserId, Guid accountId)
    {
        var rawToken = await _factory.SeedSessionAsync(accountUserId, accountId);
        return $"ophalo.sid={rawToken}";
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private async Task<Guid> SeedRequestAsync(Guid accountId)
    {
        var now = DateTime.UtcNow;
        var customer = KeepCustomer.Create(accountId, "Jane Customer", "+15555550100");
        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "Leaky faucet",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", now, KeepRequestSource.Phone);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepCustomer>().Add(customer);
        db.Set<KeepRequest>().Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<CatalogItem> SeedActiveCatalogItemAsync(Guid accountId, Guid createdByUserId, string displayName)
    {
        var createResult = CatalogItem.CreateDraft(
            accountId, CatalogItemType.Material, displayName, "each", "USD",
            externalKey: null, categoryId: null, isCommonItem: false, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var item = createResult.Value;
        Assert.True(item.Activate().IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<CatalogItem>().Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private int _versionCounter;

    private async Task<CatalogItem> SeedPricedCatalogItemAsync(Guid accountId, Guid createdByUserId, string displayName)
    {
        var item = await SeedActiveCatalogItemAsync(accountId, createdByUserId, displayName);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var versionId = Guid.NewGuid();
        var versionNumber = Interlocked.Increment(ref _versionCounter);
        var nowUtc = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_versions (
                id, account_id, version_number, source_import_id,
                published_at_utc, published_by_account_user_id, status,
                created_at_utc, updated_at_utc)
            VALUES (
                {versionId}, {accountId}, {versionNumber}, NULL,
                {nowUtc}, {createdByUserId}, 'Published',
                {nowUtc}, {nowUtc})
            """);

        var lineId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_version_lines (
                id, account_id, price_book_version_id, catalog_item_id,
                display_name_snapshot, type_snapshot, unit_of_measure_snapshot, currency_snapshot,
                cost_snapshot, sell_price_snapshot, pricing_mode,
                created_at_utc, updated_at_utc)
            VALUES (
                {lineId}, {accountId}, {versionId}, {item.Id},
                {item.DisplayName}, 'Material', 'each', 'USD',
                60, 100, 'StandalonePrice',
                {nowUtc}, {nowUtc})
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE keep_pricebook_catalog_items
            SET current_price_book_version_line_id = {lineId}
            WHERE id = {item.Id}
            """);

        return item;
    }

    private async Task<Guid> SeedAssemblyAsync(Guid accountId, Guid createdByUserId, Guid primaryCatalogItemId)
    {
        var assembly = OfferingAssembly.Create(
            accountId, primaryCatalogItemId, "Test Assembly " + Guid.NewGuid(), PriceTreatment.AllInclusive, createdByUserId).Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistence = new EfOfferingAssemblyPersistence(db);
        var commitResult = await persistence.AddAsync(assembly, CancellationToken.None);
        Assert.Equal(OfferingAssemblyCommitResult.Committed, commitResult);
        return assembly.Id;
    }

    private async Task<(Guid ScopeId, Guid ConcurrencyVersion)> SeedDraftScopeAsync(Guid accountId, Guid createdByUserId)
    {
        var requestId = await SeedRequestAsync(accountId);
        var createResult = ProposedScope.Create(accountId, requestId, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var scope = createResult.Value;

        await using var dbScope = _factory.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ProposedScope>().Add(scope);
        await db.SaveChangesAsync();
        return (scope.Id, scope.ConcurrencyVersion);
    }

    private async Task<(Guid ScopeId, Guid CatalogItemId, Guid ConcurrencyVersion)> SeedDraftScopeWithLineAsync(
        Guid accountId, Guid createdByUserId)
    {
        var requestId = await SeedRequestAsync(accountId);
        var catalogItem = await SeedActiveCatalogItemAsync(accountId, createdByUserId, "Seeded Item");
        var createResult = ProposedScope.Create(accountId, requestId, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var scope = createResult.Value;

        var addResult = scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, catalogItem.Id, null, 1m, false,
            null, null, null, 0, catalogItem.DisplayName, "each", null, null, createdByUserId);
        Assert.True(addResult.IsSuccess);

        await using var dbScope = _factory.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ProposedScope>().Add(scope);
        await db.SaveChangesAsync();
        return (scope.Id, catalogItem.Id, scope.ConcurrencyVersion);
    }
}
