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
/// HTTP integration tests for Session 3's Quick scope action configuration/field-read contract
/// (build-log/119):
///   GET/PUT /keep/pricebook/quick-scope-actions          (Owner/Admin, PriceBookCatalogManage)
///   GET     /keep/pricebook/field/quick-scope-actions     (technician, RequestsOperate+ScopeCapture)
///
/// Covers the replace-set write's eligibility validation, the config read's later-ineligible
/// "repairable" row versus the field read's silent omission, the price-free wire contract, and both
/// endpoints' gate composition.
/// </summary>
public sealed class QuickScopeActionApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public QuickScopeActionApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Replace_AdminWithEntitlement_PersistsAndReturnsEligibleRows()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-happy-path");
        await EnrollAsync(accountId, ownerId);
        var itemId = await CreateItemAsync(ownerCookie, "Common Filter", isCommonItem: true);

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions",
            new { slots = new[] { new { order = 1, catalogItemId = itemId, offeringAssemblyId = (Guid?)null } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("actions").EnumerateArray().ToList();
        Assert.Single(actions);
        Assert.Equal(itemId, actions[0].GetProperty("catalogItemId").GetGuid());
        Assert.True(actions[0].GetProperty("isEligible").GetBoolean());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<QuickScopeAction>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Replace_OperatorWithEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "replace-operator-denied");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var itemId = await CreateItemAsync(ownerCookie, "Common Filter", isCommonItem: true);

        var response = await AuthRequest(operatorCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions",
            new { slots = new[] { new { order = 1, catalogItemId = itemId, offeringAssemblyId = (Guid?)null } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Replace_InactiveCatalogItemTarget_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-inactive-target");
        await EnrollAsync(accountId, ownerId);
        var itemId = await CreateItemAsync(ownerCookie, "Common Filter", isCommonItem: true);
        await InactivateAsync(ownerCookie, itemId);

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions",
            new { slots = new[] { new { order = 1, catalogItemId = itemId, offeringAssemblyId = (Guid?)null } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("QuickScopeAction.TargetNotEligible", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Replace_NonCommonCatalogItemTarget_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-noncommon-target");
        await EnrollAsync(accountId, ownerId);
        var itemId = await CreateItemAsync(ownerCookie, "Special Order Part", isCommonItem: false);

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions",
            new { slots = new[] { new { order = 1, catalogItemId = itemId, offeringAssemblyId = (Guid?)null } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("QuickScopeAction.TargetNotEligible", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Replace_SevenSlots_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-too-many");
        await EnrollAsync(accountId, ownerId);

        // Seven slots with every Order valid ([1,6], reused once) so each row passes
        // QuickScopeAction.Create individually and the failure comes from the set-level
        // MaxOrder count check (QuickScopeActionSetValidator), not a per-row OrderOutOfRange.
        var slots = new List<object>();
        for (var i = 1; i <= 7; i++)
        {
            var itemId = await CreateItemAsync(ownerCookie, $"Common Item {i}", isCommonItem: true);
            slots.Add(new { order = ((i - 1) % 6) + 1, catalogItemId = itemId, offeringAssemblyId = (Guid?)null });
        }
        _ = accountId;

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions", new { slots });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("QuickScopeAction.TooManySlots", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Replace_DuplicateOrder_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-dup-order");
        await EnrollAsync(accountId, ownerId);
        var itemA = await CreateItemAsync(ownerCookie, "Item A", isCommonItem: true);
        var itemB = await CreateItemAsync(ownerCookie, "Item B", isCommonItem: true);

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions",
            new
            {
                slots = new[]
                {
                    new { order = 1, catalogItemId = itemA, offeringAssemblyId = (Guid?)null },
                    new { order = 1, catalogItemId = itemB, offeringAssemblyId = (Guid?)null },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("QuickScopeAction.DuplicateOrder", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_LaterIneligibleTarget_ShowsRepairableIsEligibleFalse()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("get-later-ineligible");
        await EnrollAsync(accountId, ownerId);
        var itemId = await CreateItemAsync(ownerCookie, "Common Filter", isCommonItem: true);

        var replace = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions",
            new { slots = new[] { new { order = 1, catalogItemId = itemId, offeringAssemblyId = (Guid?)null } } });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);

        // Deactivating after configuration must not be silently dropped from the account's
        // configured set — it stays until an explicit Owner/Admin correction (build-log/119).
        await InactivateAsync(ownerCookie, itemId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/quick-scope-actions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("actions").EnumerateArray().ToList();
        Assert.Single(actions);
        Assert.False(actions[0].GetProperty("isEligible").GetBoolean());

        // Persisted entity state is unaffected — this is a read-time computed predicate only.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<QuickScopeAction>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task FieldList_LaterIneligibleTarget_IsOmittedAndPriceFree()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("field-omits-ineligible");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "field-omits-ineligible");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var eligibleId = await CreateItemAsync(ownerCookie, "Common Filter", isCommonItem: true, sellPrice: 250m);
        var ineligibleId = await CreateItemAsync(ownerCookie, "Common Coil", isCommonItem: true, sellPrice: 500m);

        var replace = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions",
            new
            {
                slots = new[]
                {
                    new { order = 1, catalogItemId = eligibleId, offeringAssemblyId = (Guid?)null },
                    new { order = 2, catalogItemId = ineligibleId, offeringAssemblyId = (Guid?)null },
                },
            });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);

        await InactivateAsync(ownerCookie, ineligibleId);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/quick-scope-actions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isEligible", raw, StringComparison.OrdinalIgnoreCase);

        var body = JsonDocument.Parse(raw).RootElement;
        var actions = body.GetProperty("actions").EnumerateArray().ToList();
        Assert.Single(actions);
        Assert.Equal(eligibleId, actions[0].GetProperty("catalogItemId").GetGuid());
    }

    [Fact]
    public async Task FieldList_ViewerRole_LacksRequestsOperateAndScopeCapture_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("field-viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "field-viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await AuthRequest(viewerCookie).GetAsync("/keep/pricebook/field/quick-scope-actions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = ownerCookie;
    }

    [Fact]
    public async Task FieldList_BlockedAccount_Returns403()
    {
        // Gate 1 — account access. An expired trial is Blocked (AccountAccessPolicy.Evaluate);
        // the field surface denies exactly like FieldCatalogReadApiService/FieldOfferingAssemblyReadApiService.
        var (accountId, ownerId, ownerCookie) = await SeedAccountWithExpiredTrialAsync("field-blocked-account");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/field/quick-scope-actions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FieldList_WithoutEntitlement_Returns403()
    {
        // Gate 2 — Price Book entitlement. No EnrollAsync call, so the account has neither an
        // eligible plan nor an active capability-package enrollment.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("field-no-entitlement");
        var operatorId = await SeedOperatorAsync(accountId, "field-no-entitlement");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/quick-scope-actions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = ownerCookie;
        _ = ownerId;
    }

    [Fact]
    public async Task Replace_BlockedAccount_Returns403()
    {
        // Gate 1 — account access. A mutation, so both Blocked and ReadOnly deny; an expired
        // trial is Blocked, matching CatalogCategoryApiService's mutation gate.
        var (accountId, ownerId, ownerCookie) = await SeedAccountWithExpiredTrialAsync("replace-blocked-account");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions", new { slots = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Replace_WithoutEntitlement_Returns403()
    {
        // Gate 2 — Price Book entitlement. No EnrollAsync call.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-no-entitlement");

        var response = await AuthRequest(ownerCookie).PutAsJsonAsync(
            "/keep/pricebook/quick-scope-actions", new { slots = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = accountId;
        _ = ownerId;
    }

    [Fact]
    public async Task Get_BlockedAccount_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountWithExpiredTrialAsync("get-blocked-account");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/quick-scope-actions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("get-no-entitlement");

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/quick-scope-actions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = accountId;
        _ = ownerId;
    }

    [Fact]
    public async Task FieldList_OperatorRole_ExistingAdminConfigEndpoint_Still403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("field-regression-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "field-regression-operator-denied");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var adminResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/quick-scope-actions");
        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        var fieldResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/quick-scope-actions");
        Assert.Equal(HttpStatusCode.OK, fieldResponse.StatusCode);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

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
            businessName: $"Quick Scope Action Test Co {slug}",
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

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountWithExpiredTrialAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Quick Scope Action Test Co {slug}",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(-1));

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

    private async Task<Guid> SeedViewerAsync(Guid accountId, string slug)
    {
        var now = DateTime.UtcNow;
        var email = $"viewer@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), AccountUserRole.Viewer,
            inviteTokenHash: $"{slug}_viewer_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
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
