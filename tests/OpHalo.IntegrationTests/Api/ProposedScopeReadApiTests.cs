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
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for the ProposedScope read endpoints (Session 3.4a):
///   GET /keep/pricebook/proposed-scopes/by-request/{requestId}
///   GET /keep/pricebook/proposed-scopes/{proposedScopeId}
///
/// Covers the locked scope-display contract (build-log/118, decision 3 — open Draft takes
/// precedence, otherwise the single most recent Submitted/Reviewed row, never full history), the
/// wire-contract correction that "no scope yet" is 200 with an explicit state tag rather than an
/// ambiguous null body, and that 404 is reserved for MyWork row-visibility denial and
/// missing/cross-account scope ids.
/// </summary>
public sealed class ProposedScopeReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ProposedScopeReadApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ByRequest_NoScopeExists_Returns200WithNoScopeYetState()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("byrequest-none");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/proposed-scopes/by-request/{requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NoScopeYet", body.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("scope").ValueKind);
    }

    [Fact]
    public async Task ByRequest_OpenDraftExists_TakesPrecedenceOverAnOlderSubmittedScope()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("byrequest-draft-precedence");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        await SeedSubmittedScopeAsync(accountId, requestId, ownerId);
        var (draftId, _) = await SeedDraftScopeForRequestAsync(accountId, requestId, ownerId);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/proposed-scopes/by-request/{requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("state").GetString());
        Assert.Equal(draftId, body.GetProperty("scope").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ByRequest_NoOpenDraft_ReturnsOnlyTheSingleMostRecentSubmittedScope()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("byrequest-most-recent-only");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        await SeedSubmittedScopeAsync(accountId, requestId, ownerId);
        var (mostRecentId, _) = await SeedSubmittedScopeAsync(accountId, requestId, ownerId);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/proposed-scopes/by-request/{requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SubmittedToOffice", body.GetProperty("state").GetString());
        Assert.Equal(mostRecentId, body.GetProperty("scope").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ByRequest_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("byrequest-no-entitlement");
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/proposed-scopes/by-request/{requestId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ByRequest_ForARequestTheOperatorCannotSee_Returns404()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("byrequest-operator-mywork");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "byrequest-operator-mywork");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        // The owner's request never attaches the operator as Responsible/Watching, so it must be
        // invisible under MyWork — same guard as ProposedScopeApiTests's Submit/UpdateLine 404s.
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/proposed-scopes/by-request/{requestId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ById_UnknownId_Returns404()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("byid-unknown");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/proposed-scopes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ById_ForAScopeOnARequestTheOperatorCannotSee_Returns404()
    {
        // Guards that the by-id read loads the account-scoped scope first, then applies
        // GetRequestAsync visibility to its RequestId — folding "scope missing/cross-account" and
        // "request invisible" into the same 404, never a 403 that would confirm the row exists.
        var (accountId, ownerId, _) = await SeedAccountAsync("byid-operator-mywork");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "byid-operator-mywork");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        var (scopeId, _) = await SeedDraftScopeForRequestAsync(accountId, requestId, ownerId);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/proposed-scopes/{scopeId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ById_ReturnsTheScopeWithItsLinesOrderedByDisplayOrder()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("byid-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        var itemA = await SeedActiveCatalogItemAsync(accountId, ownerId, "Item A");
        var itemB = await SeedActiveCatalogItemAsync(accountId, ownerId, "Item B");
        var (scopeId, _) = await SeedDraftScopeWithTwoLinesAsync(accountId, requestId, ownerId, itemA, itemB);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/proposed-scopes/{scopeId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(scopeId, body.GetProperty("id").GetGuid());
        Assert.Equal(requestId, body.GetProperty("requestId").GetGuid());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        var lines = body.GetProperty("lines");
        Assert.Equal(2, lines.GetArrayLength());
        // Persisted with itemB at DisplayOrder 0 and itemA at DisplayOrder 10 (see
        // SeedDraftScopeWithTwoLinesAsync) — the response must reflect DisplayOrder, not insertion
        // order, so itemB comes first.
        Assert.Equal("Item B", lines[0].GetProperty("displayNameSnapshot").GetString());
        Assert.Equal("Item A", lines[1].GetProperty("displayNameSnapshot").GetString());

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cost\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margin", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calculatedSellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pricingMode", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("priceStatus", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_OperatorRole_ExistingAdminGatedCatalogAndAssemblyEndpoints_Still403()
    {
        // Regression: the new field-safe-adjacent ProposedScope read surface sits beside
        // PriceBookCatalogManage, not inside a loosened version of it — Operator role must still be
        // denied the Admin-gated catalog/assembly reads (build-log/118's carried-forward proof).
        var (accountId, ownerId, _) = await SeedAccountAsync("regression-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "regression-operator-denied");
        var cookie = await GetCookieAsync(operatorId, accountId);

        var catalogItemsResponse = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items");
        Assert.Equal(HttpStatusCode.Forbidden, catalogItemsResponse.StatusCode);

        var assembliesResponse = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies");
        Assert.Equal(HttpStatusCode.Forbidden, assembliesResponse.StatusCode);
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Proposed Scope Read Test Co {slug}",
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

    private async Task<(Guid ScopeId, Guid ConcurrencyVersion)> SeedDraftScopeForRequestAsync(
        Guid accountId, Guid requestId, Guid createdByUserId)
    {
        var createResult = ProposedScope.Create(accountId, requestId, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var scope = createResult.Value;

        await using var dbScope = _factory.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ProposedScope>().Add(scope);
        await db.SaveChangesAsync();
        return (scope.Id, scope.ConcurrencyVersion);
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

    /// <summary>Adds itemB at DisplayOrder 0 and itemA at DisplayOrder 10 — the reverse of
    /// insertion order — so a display-order-sorted response is distinguishable from an
    /// insertion-order one.</summary>
    private async Task<(Guid ScopeId, Guid ConcurrencyVersion)> SeedDraftScopeWithTwoLinesAsync(
        Guid accountId, Guid requestId, Guid createdByUserId, CatalogItem itemA, CatalogItem itemB)
    {
        var createResult = ProposedScope.Create(accountId, requestId, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var scope = createResult.Value;

        Assert.True(scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, itemA.Id, null, 1m, false,
            null, null, null, 10, itemA.DisplayName, "each", null, null, createdByUserId).IsSuccess);
        Assert.True(scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, itemB.Id, null, 1m, false,
            null, null, null, 0, itemB.DisplayName, "each", null, null, createdByUserId).IsSuccess);

        await using var dbScope = _factory.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ProposedScope>().Add(scope);
        await db.SaveChangesAsync();
        return (scope.Id, scope.ConcurrencyVersion);
    }

    /// <summary>Creates and immediately submits a scope (domain <c>Submit</c> only — this test
    /// doesn't need the atomic <c>KeepRequestWorkSignal</c> coordination the real submit endpoint
    /// provides) so distinct rows land at distinct <c>CreatedAtUtc</c> values for the
    /// most-recent-only assertion. Adds one line first — Session 4a's <c>EmptySubmit</c> domain
    /// rule means <c>Submit</c> can no longer succeed on an empty scope.</summary>
    private async Task<(Guid ScopeId, Guid ConcurrencyVersion)> SeedSubmittedScopeAsync(
        Guid accountId, Guid requestId, Guid createdByUserId)
    {
        var item = await SeedActiveCatalogItemAsync(accountId, createdByUserId, $"Test Item {Guid.NewGuid()}");

        var createResult = ProposedScope.Create(accountId, requestId, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var scope = createResult.Value;
        Assert.True(scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, item.Id, null, 1m, false,
            null, null, null, 0, item.DisplayName, "each", null, null, createdByUserId).IsSuccess);
        Assert.True(scope.Submit(DateTime.UtcNow).IsSuccess);

        await using var dbScope = _factory.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ProposedScope>().Add(scope);
        await db.SaveChangesAsync();
        return (scope.Id, scope.ConcurrencyVersion);
    }
}
