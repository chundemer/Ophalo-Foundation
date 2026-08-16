using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 2's Paired Nudges Owner/Admin rule-configuration contract
/// (build-log/123):
///   GET/POST /keep/pricebook/scope-nudge-rules            (Owner/Admin, PriceBookCatalogManage)
///   PUT/DELETE /keep/pricebook/scope-nudge-rules/{ruleId}
///
/// Covers write-time existence-only validation (inactive/ineligible targets are accepted, unlike
/// QuickScopeAction), duplicate-trigger conflict, update's trigger-immutability contract, and the
/// price-free wire shape.
/// </summary>
public sealed class ScopeNudgeRuleApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ScopeNudgeRuleApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_AdminWithEntitlement_PersistsAndReturnsPriceFreeRow()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-happy-path");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(triggerId, body.GetProperty("triggerCatalogItemId").GetGuid());
        Assert.True(body.GetProperty("triggerIsEligible").GetBoolean());
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Single(suggestions);
        Assert.Equal(suggestionId, suggestions[0].GetProperty("suggestedCatalogItemId").GetGuid());

        AssertNoPriceProperties(body);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<ScopeNudgeRule>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Create_OperatorWithEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "create-operator-denied");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);

        var response = await AuthRequest(operatorCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync("create-no-entitlement");
        _ = accountId;

        // The entitlement gate runs before any target-existence check, so unresolvable ids are
        // sufficient here — this proves the gate order, not target validation.
        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = Guid.NewGuid(),
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = Guid.NewGuid(), offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_InactiveTriggerAndSuggestion_PersistsAndReturnsIneligible()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-inactive-targets");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);
        await InactivateAsync(ownerCookie, triggerId);
        await InactivateAsync(ownerCookie, suggestionId);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("triggerIsEligible").GetBoolean());
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.False(suggestions[0].GetProperty("isEligible").GetBoolean());
    }

    [Fact]
    public async Task Create_UnknownTriggerTarget_Returns404WithTargetNotFound()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-unknown-trigger");
        await EnrollAsync(accountId, ownerId);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = Guid.NewGuid(),
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TargetNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_CrossAccountTriggerTarget_Returns404WithTargetNotFound()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-cross-account-a");
        await EnrollAsync(accountId, ownerId);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);

        var (otherAccountId, otherOwnerId, otherOwnerCookie) = await SeedAccountAsync("create-cross-account-b");
        await EnrollAsync(otherAccountId, otherOwnerId);
        var foreignItemId = await CreateItemAsync(otherOwnerCookie, "Foreign Filter", isCommonItem: true);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = foreignItemId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TargetNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_DuplicateTrigger_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-duplicate-trigger");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionAId = await CreateItemAsync(ownerCookie, "Gasket A", isCommonItem: false);
        var suggestionBId = await CreateItemAsync(ownerCookie, "Gasket B", isCommonItem: false);

        var first = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionAId, offeringAssemblyId = (Guid?)null } },
            });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionBId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.DuplicateTrigger", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_BothTriggerFieldsPresentOneUnknown_ReturnsExclusivityDomainError()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-both-trigger-fields");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);

        // The offering-assembly trigger id is unresolvable, but the domain shape check must fail
        // first: a malformed request never reaches the target lookup that would otherwise return
        // TargetNotFound (404) instead of the deterministic 400.
        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = Guid.NewGuid(),
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TriggerTargetMustBeExclusive", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_BothSuggestionFieldsPresentOneUnknown_ReturnsExclusivityDomainError()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-both-suggestion-fields");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = Guid.NewGuid(), offeringAssemblyId = Guid.NewGuid() } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.SuggestionTargetMustBeExclusive", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_UnknownSuggestionTarget_Returns404WithTargetNotFound()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-unknown-suggestion");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = Guid.NewGuid(), offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TargetNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_ForeignSuggestionTarget_Returns404WithTargetNotFound()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-foreign-suggestion-a");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);

        var (otherAccountId, otherOwnerId, otherOwnerCookie) = await SeedAccountAsync("create-foreign-suggestion-b");
        await EnrollAsync(otherAccountId, otherOwnerId);
        var foreignItemId = await CreateItemAsync(otherOwnerCookie, "Foreign Gasket", isCommonItem: false);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = foreignItemId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TargetNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_IncludesRules_GateDeniesOperator()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-happy-path");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);
        await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });

        var listResponse = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/scope-nudge-rules");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(body.GetProperty("rules").EnumerateArray());

        var operatorId = await SeedOperatorAsync(accountId, "list-happy-path");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var deniedResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/scope-nudge-rules");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }

    [Fact]
    public async Task Update_ReplacesSuggestionsOnly_IgnoresTriggerFields()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("update-suggestions-only");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var otherTriggerId = await CreateItemAsync(ownerCookie, "Other Trigger", isCommonItem: true);
        var suggestionAId = await CreateItemAsync(ownerCookie, "Gasket A", isCommonItem: false);
        var suggestionBId = await CreateItemAsync(ownerCookie, "Gasket B", isCommonItem: false);

        var createResponse = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionAId, offeringAssemblyId = (Guid?)null } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ruleId = created.GetProperty("id").GetGuid();

        var updateResponse = await AuthRequest(ownerCookie).PutAsJsonAsync(
            $"/keep/pricebook/scope-nudge-rules/{ruleId}",
            new
            {
                triggerCatalogItemId = otherTriggerId,
                suggestions = new[] { new { catalogItemId = suggestionBId, offeringAssemblyId = (Guid?)null } },
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(triggerId, updated.GetProperty("triggerCatalogItemId").GetGuid());
        var suggestions = updated.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Single(suggestions);
        Assert.Equal(suggestionBId, suggestions[0].GetProperty("suggestedCatalogItemId").GetGuid());
    }

    [Fact]
    public async Task Update_UnknownRuleId_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("update-unknown-rule");
        await EnrollAsync(accountId, ownerId);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            $"/keep/pricebook/scope-nudge-rules/{Guid.NewGuid()}",
            new { suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Update_CrossAccountRuleId_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("update-cross-account-a");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);
        var createResponse = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ruleId = created.GetProperty("id").GetGuid();

        var (otherAccountId, otherOwnerId, otherOwnerCookie) = await SeedAccountAsync("update-cross-account-b");
        await EnrollAsync(otherAccountId, otherOwnerId);

        var response = await AuthRequest(otherOwnerCookie).PutAsJsonAsync(
            $"/keep/pricebook/scope-nudge-rules/{ruleId}",
            new { suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesRuleAndSuggestions()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("delete-happy-path");
        await EnrollAsync(accountId, ownerId);
        var triggerId = await CreateItemAsync(ownerCookie, "Filter", isCommonItem: true);
        var suggestionId = await CreateItemAsync(ownerCookie, "Gasket", isCommonItem: false);
        var createResponse = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/scope-nudge-rules",
            new
            {
                triggerCatalogItemId = triggerId,
                triggerOfferingAssemblyId = (Guid?)null,
                suggestions = new[] { new { catalogItemId = suggestionId, offeringAssemblyId = (Guid?)null } },
            });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ruleId = created.GetProperty("id").GetGuid();

        var deleteResponse = await AuthRequest(ownerCookie).DeleteAsync($"/keep/pricebook/scope-nudge-rules/{ruleId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Empty(db.Set<ScopeNudgeRule>().Where(x => x.Id == ruleId));
        Assert.Empty(db.Set<ScopeNudgeSuggestion>().Where(x => x.ScopeNudgeRuleId == ruleId));
    }

    [Fact]
    public async Task Delete_UnknownRuleId_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("delete-unknown-rule");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).DeleteAsync($"/keep/pricebook/scope-nudge-rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.NotFound", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Recursively asserts no property name anywhere in the JSON tree (including nested
    /// suggestion objects) contains "price" — the wire contract must be price-free at every level,
    /// not only the root.</summary>
    private static void AssertNoPriceProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.DoesNotContain("price", property.Name, StringComparison.OrdinalIgnoreCase);
                    AssertNoPriceProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    AssertNoPriceProperties(item);
                break;
        }
    }

    private async Task<Guid> CreateItemAsync(
        string cookie, string displayName, bool isCommonItem, decimal sellPrice = 100m)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName,
                unitOfMeasure = "each",
                currency = "USD",
                isCommonItem,
                initialAliasTexts = Array.Empty<string>(),
                pricingMode = "StandalonePrice",
                cost = sellPrice / 2m,
                sellPrice,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("item").GetProperty("id").GetGuid();
    }

    private async Task InactivateAsync(string cookie, Guid itemId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var item = await db.Set<CatalogItem>().SingleAsync(x => x.Id == itemId);
        var version = item.ConcurrencyVersion;
        db.Entry(item).State = EntityState.Detached;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/keep/pricebook/catalog-items/{itemId}/inactivate");
        request.Headers.Add(CatalogItemVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Scope Nudge Rule Test Co {slug}",
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
}
