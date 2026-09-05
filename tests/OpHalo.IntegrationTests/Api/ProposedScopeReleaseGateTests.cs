using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for the BL142 Session 1 / ADR-496 server-owned release gate
/// (<see cref="IReleaseGatePolicy"/>): Proposed Work reads and mutations must stay blocked while
/// the gate is closed, independent of Price Book package entitlement. Uses
/// <see cref="ReleaseGateClosedWebFactory"/>, which deliberately omits
/// Keep:ReleaseGates:ProposedWorkQuotes so the policy fails closed — unlike
/// <see cref="KeepApiWebFactory"/>, which opens the gate for its unrelated ProposedScope tests.
/// </summary>
public sealed class ProposedScopeReleaseGateTests : IClassFixture<ReleaseGateClosedWebFactory>, IAsyncLifetime
{
    private readonly ReleaseGateClosedWebFactory _factory;

    public ProposedScopeReleaseGateTests(ReleaseGateClosedWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_EntitledButGateClosed_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("gate-closed-create");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/proposed-scopes/create", new { requestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ByRequest_EntitledButGateClosed_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("gate-closed-read");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(cookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/by-request/{requestId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FieldSelect_EntitledButGateClosed_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("gate-closed-field-select");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/field-select")
        {
            Content = JsonContent.Create(new
            {
                lineType = "OffCatalogItem",
                catalogItemId = (Guid?)null,
                quantity = 1m,
                offCatalogDescription = "Gate closed test",
                note = (string?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));

        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExpandAssembly_EntitledButGateClosed_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("gate-closed-expand-assembly");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly")
        {
            Content = JsonContent.Create(new
            {
                offeringAssemblyId = Guid.NewGuid(),
                excludedOptionalItemIds = Array.Empty<Guid>(),
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));

        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NudgeSuggestions_EntitledButGateClosed_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("gate-closed-nudge-suggestions");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);

        var response = await AuthRequest(cookie).GetAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/nudge-suggestions?triggerCatalogItemId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task QuickScopeActionsFieldRead_EntitledButGateClosed_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("gate-closed-quick-scope-actions");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/field/quick-scope-actions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Release Gate Test Co {slug}",
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
}
