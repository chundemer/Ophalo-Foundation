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
/// HTTP integration tests for Actual Work's field-safe nudge-read contract (build-log/129,
/// 5d-ii-c):
///   GET /keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions
///
/// Covers the required trigger-parameter shape, read-time eligibility filtering of the trigger and
/// each suggestion, catalog-item-only Draft-line dedupe (assembly suggestions are never suppressed —
/// ActualWorkLine retains no assembly provenance), and the active-Responsible row-authorization gate
/// shared with every other Actual Work Draft endpoint.
/// </summary>
public sealed class ActualWorkNudgeFieldReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkNudgeFieldReadApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetSuggestions_HappyPath_ReturnsOrderedEligibleSurvivingSuggestions()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-happy-path");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var suggestionOne = await SeedActiveCatalogItemAsync(accountId, ownerId, "Suggestion One");
        var suggestionTwo = await SeedActiveCatalogItemAsync(accountId, ownerId, "Suggestion Two");
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null,
            (suggestionOne.Id, null), (suggestionTwo.Id, null));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

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
    public async Task GetSuggestions_IneligibleSuggestionTarget_IsOmittedNotMarked()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-ineligible-suggestion");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var eligibleSuggestion = await SeedActiveCatalogItemAsync(accountId, ownerId, "Eligible Suggestion");
        var ineligibleSuggestion = await SeedActiveCatalogItemAsync(accountId, ownerId, "Ineligible Suggestion");
        await InactivateAsync(ineligibleSuggestion.Id);
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null,
            (eligibleSuggestion.Id, null), (ineligibleSuggestion.Id, null));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Single(suggestions);
        Assert.Equal(eligibleSuggestion.Id, suggestions[0].GetProperty("catalogItemId").GetGuid());
    }

    [Fact]
    public async Task GetSuggestions_CatalogSuggestionAlreadyOnDraftLine_IsOmitted()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-draft-dedupe");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, version) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var alreadyOnDraft = await SeedActiveCatalogItemAsync(accountId, ownerId, "Already On Draft");
        await AddCatalogLineAsync(ownerCookie, actualWorkId, version, alreadyOnDraft.Id);
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null, (alreadyOnDraft.Id, null));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
        Assert.True(body.GetProperty("ruleId").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSuggestions_AssemblySuggestion_NeverSuppressedByDedupe()
    {
        // ActualWorkLine retains no OfferingAssemblyId (build-log/129, 5d-ii-c lock), so an assembly
        // suggestion is always shown regardless of prior expansion — the expand-assembly endpoint's
        // skip-and-report result is the only place partial/full prior expansion is surfaced.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-assembly-suggestion");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var primaryItem = await SeedPricedCatalogItemAsync(accountId, ownerId, "Assembly Primary Item");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primaryItem.Id);
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null, (null, assemblyId));

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Single(suggestions);
        Assert.Equal(assemblyId, suggestions[0].GetProperty("offeringAssemblyId").GetGuid());
    }

    [Fact]
    public async Task GetSuggestions_IneligibleTrigger_ReturnsEmptyResult()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-ineligible-trigger");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");
        var suggestion = await SeedActiveCatalogItemAsync(accountId, ownerId, "Suggestion");
        await SeedRuleAsync(accountId, ownerId, trigger.Id, null, (suggestion.Id, null));
        await InactivateAsync(trigger.Id);

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
    }

    [Fact]
    public async Task GetSuggestions_NoRuleConfiguredForTrigger_ReturnsEmptyResult()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-no-rule");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var trigger = await SeedActiveCatalogItemAsync(accountId, ownerId, "Trigger Item");

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={trigger.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("suggestions").EnumerateArray());
    }

    [Fact]
    public async Task GetSuggestions_MissingTriggerParameter_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-missing-trigger");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TriggerQueryParameterInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_CombinedTriggerParameters_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-combined-trigger");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var catalogItemId = Guid.NewGuid();
        var offeringAssemblyId = Guid.NewGuid();

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions" +
            $"?triggerCatalogItemId={catalogItemId}&triggerOfferingAssemblyId={offeringAssemblyId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScopeNudgeRule.TriggerQueryParameterInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_NonResponsibleCaller_Returns404()
    {
        // A member without active-Responsible participation gets the same indistinguishable
        // KeepRequest.NotFound as every other Actual Work Draft mutation (build-log/129 5d-ii-c
        // lock) — never ScopeNudge's broader row-visibility read.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-non-responsible");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        var operatorId = await SeedOperatorAsync(accountId, "aw-nudge-non-responsible");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_SubmittedVisit_Returns409NotDraft()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-not-draft");
        await EnrollAsync(accountId, ownerId);
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);
        await SubmitAsync(actualWorkId, accountId);

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.NotDraft", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetSuggestions_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("aw-nudge-no-entitlement");
        var (actualWorkId, _) = await SeedDraftActualWorkAsync(accountId, ownerId, ownerCookie);

        var response = await AuthRequest(ownerCookie).GetAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions?triggerCatalogItemId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task AddCatalogLineAsync(string cookie, Guid actualWorkId, Guid version, Guid catalogItemId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new { catalogItemId, actualQuantity = 1m, note = (string?)null })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> SubmitAsync(Guid actualWorkId, Guid accountId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistence = new EfActualWorkPersistence(db);
        var actualWork = await persistence.GetByIdAsync(accountId, actualWorkId, CancellationToken.None);
        var submitResult = actualWork!.Submit(DateTime.UtcNow, ActualWorkOutcome.NoWorkAuthorized, "No work performed.");
        Assert.True(submitResult.IsSuccess);
        var commitResult = await persistence.CommitAsync(actualWork, CancellationToken.None);
        Assert.Equal(ActualWorkCommitResult.Committed, commitResult);
        return actualWork.ConcurrencyVersion;
    }

    private async Task<(Guid ActualWorkId, Guid ConcurrencyVersion)> SeedDraftActualWorkAsync(
        Guid accountId, Guid ownerId, string ownerCookie)
    {
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);

        var createResult = ActualWork.Create(accountId, requestId, ownerId);
        Assert.True(createResult.IsSuccess);
        var actualWork = createResult.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistence = new EfActualWorkPersistence(db);
        var commitResult = await persistence.AddAsync(actualWork, CancellationToken.None);
        Assert.Equal(ActualWorkCommitResult.Committed, commitResult);
        return (actualWork.Id, actualWork.ConcurrencyVersion);
    }

    private async Task SeedRuleAsync(
        Guid accountId, Guid createdByUserId, Guid? triggerCatalogItemId, Guid? triggerOfferingAssemblyId,
        params (Guid? CatalogItemId, Guid? OfferingAssemblyId)[] suggestions)
    {
        var orderedSuggestions = suggestions
            .Select((s, i) => (s.CatalogItemId, s.OfferingAssemblyId))
            .ToList();
        var createResult = ActualWorkNudgeRule.Create(
            accountId, triggerCatalogItemId, triggerOfferingAssemblyId, orderedSuggestions, createdByUserId);
        Assert.True(createResult.IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ActualWorkNudgeRule>().Add(createResult.Value);
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
            businessName: $"Actual Work Nudge Field Read Test Co {slug}",
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
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "AC not cooling",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", now, KeepRequestSource.Phone);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepCustomer>().Add(customer);
        db.Set<KeepRequest>().Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task SeedResponsibleAsync(Guid requestId, Guid accountId, Guid accountUserId)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                requestId, accountId, accountUserId, ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
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
}
