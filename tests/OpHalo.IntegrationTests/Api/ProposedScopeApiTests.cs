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
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for the ProposedScope mutation endpoints:
///   POST /keep/pricebook/proposed-scopes/create
///   POST .../{id}/field-select (Session 3.4d — replaces the retired raw POST .../{id}/lines)
///   PATCH/DELETE .../{id}/lines/{lineId}
///   POST .../{id}/submit
///
/// Covers the ADR-480 three-gate composition (account access, Price Book entitlement,
/// RequestsOperate + ScopeCapture permissions), the strict version-header contract (create carries
/// no header), the terminal-request precondition extended to create and every line edit (not just
/// submit — 3.3a.2 already proves the persistence-level submit/signal behavior directly), the
/// one-open-draft-per-request race surfaced through the API, and (3.4d) field-select's
/// server-authoritative catalog-item resolution, server-derived off-catalog display-name snapshot,
/// server-computed display order, and the retired-endpoint's gates -> visibility -> act ordering.
/// </summary>
public sealed class ProposedScopeApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ProposedScopeApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_ReturnsOkAndPersistsADraftScope()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/proposed-scopes/create", new { requestId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(requestId, body.GetProperty("requestId").GetGuid());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        var scopeId = body.GetProperty("id").GetGuid();

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<ProposedScope>().SingleAsync(x => x.Id == scopeId);
        Assert.Equal(ProposedScopeStatus.Draft, reloaded.Status);
    }

    [Fact]
    public async Task Create_ForATerminalRequest_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-terminal");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId, KeepRequestStatus.Cancelled, ownerId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/proposed-scopes/create", new { requestId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-no-entitlement");
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/proposed-scopes/create", new { requestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SecondDraftForTheSameRequest_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-duplicate-draft");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var requestId = await SeedRequestAsync(accountId);

        var first = await AuthRequest(cookie).PostAsJsonAsync("/keep/pricebook/proposed-scopes/create", new { requestId });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await AuthRequest(cookie).PostAsJsonAsync("/keep/pricebook/proposed-scopes/create", new { requestId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.DraftAlreadyOpenForRequest", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FieldSelect_KnownCatalogItem_ReturnsOkAndPersistsTheLineWithServerResolvedSnapshotAndDisplayOrder()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("field-select-known-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);
        var catalogItem = await SeedActiveCatalogItemAsync(accountId, ownerId, "Labor");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/field-select")
        {
            Content = JsonContent.Create(new
            {
                lineType = "KnownCatalogItem",
                catalogItemId = catalogItem.Id,
                quantity = 2m,
                offCatalogDescription = (string?)null,
                note = (string?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lineId = body.GetProperty("lineId").GetGuid();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(version, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<ProposedScope>().Include(x => x.Lines).SingleAsync(x => x.Id == scopeId);
        var line = reloaded.Lines.Single(l => l.Id == lineId);
        Assert.Equal(catalogItem.Id, line.CatalogItemId);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(catalogItem.DisplayName, line.DisplayNameSnapshot);
        Assert.Equal("each", line.UnitOfMeasureSnapshot);
        Assert.Equal(10, line.DisplayOrder);
    }

    [Fact]
    public async Task FieldSelect_OffCatalogItem_PreservesFullDescriptionAndDerivesTruncatedDisplayNameSnapshot()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("field-select-off-catalog-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var fullDescription = "  " + new string('x', 260) + "  ";

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/field-select")
        {
            Content = JsonContent.Create(new
            {
                lineType = "OffCatalogItem",
                catalogItemId = (Guid?)null,
                quantity = 1m,
                offCatalogDescription = fullDescription,
                note = (string?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lineId = body.GetProperty("lineId").GetGuid();

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<ProposedScope>().Include(x => x.Lines).SingleAsync(x => x.Id == scopeId);
        var line = reloaded.Lines.Single(l => l.Id == lineId);
        Assert.Equal(fullDescription, line.OffCatalogDescription);
        Assert.Equal(1m, line.OffCatalogQuantity);
        Assert.Equal(200, line.DisplayNameSnapshot.Length);
        Assert.Equal(new string('x', 200), line.DisplayNameSnapshot);
    }

    [Fact]
    public async Task FieldSelect_UnknownCatalogItemId_Returns404()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("field-select-unknown-item");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/field-select")
        {
            Content = JsonContent.Create(new
            {
                lineType = "KnownCatalogItem",
                catalogItemId = Guid.NewGuid(),
                quantity = 1m,
                offCatalogDescription = (string?)null,
                note = (string?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.LineCatalogItemNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FieldSelect_OffCatalogDescriptionWithControlCharacter_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("field-select-control-char");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/field-select")
        {
            Content = JsonContent.Create(new
            {
                lineType = "OffCatalogItem",
                catalogItemId = (Guid?)null,
                quantity = 1m,
                offCatalogDescription = "leaky pipe\tunder sink",
                note = (string?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.LineOffCatalogDescriptionInvalidCharacters", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FieldSelect_PrimaryOfferingLineType_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("field-select-line-type-invalid");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/field-select")
        {
            Content = JsonContent.Create(new
            {
                lineType = "PrimaryOffering",
                catalogItemId = (Guid?)null,
                quantity = 1m,
                offCatalogDescription = (string?)null,
                note = (string?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Validation.LineTypeInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FieldSelect_ForAScopeOnARequestTheOperatorCannotSee_WithAnUnknownCatalogItem_Returns404NotCatalogItemNotFound()
    {
        // Guards the locked gates -> request visibility -> act ordering (build-log/118): visibility
        // must be checked before the catalog item is resolved, so an invisible scope 404s as
        // KeepRequest.NotFound rather than leaking whether the referenced item exists.
        var (accountId, ownerId, _) = await SeedAccountAsync("field-select-operator-mywork");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "field-select-operator-mywork");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/field-select")
        {
            Content = JsonContent.Create(new
            {
                lineType = "KnownCatalogItem",
                catalogItemId = Guid.NewGuid(),
                quantity = 1m,
                offCatalogDescription = (string?)null,
                note = (string?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(operatorCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FieldSelect_WithoutVersionHeader_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("field-select-no-header");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);
        var catalogItem = await SeedActiveCatalogItemAsync(accountId, ownerId, "Labor");

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/field-select",
            new
            {
                lineType = "KnownCatalogItem",
                catalogItemId = catalogItem.Id,
                quantity = 1m,
                offCatalogDescription = (string?)null,
                note = (string?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExpandAssembly_ReturnsOkAndPersistsPrimaryAndAssociatedItemLines()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("expand-assembly-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Furnace");
        var child = await SeedPricedCatalogItemAsync(accountId, ownerId, "Filter");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id, (child.Id, false));

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, excludedOptionalItemIds = Array.Empty<Guid>() }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("lineIds").GetArrayLength());
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(version, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<ProposedScope>().Include(x => x.Lines).SingleAsync(x => x.Id == scopeId);
        Assert.Equal(2, reloaded.Lines.Count);
        Assert.Contains(reloaded.Lines, l => l.LineType == ProposedScopeLineType.PrimaryOffering && l.CatalogItemId == primary.Id);
        Assert.Contains(reloaded.Lines, l => l.LineType == ProposedScopeLineType.AssociatedItem && l.CatalogItemId == child.Id);
    }

    [Fact]
    public async Task ExpandAssembly_UnknownAssemblyId_Returns404()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("expand-assembly-unknown");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = Guid.NewGuid(), excludedOptionalItemIds = Array.Empty<Guid>() }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OfferingAssembly.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExpandAssembly_IneligibleAssembly_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("expand-assembly-ineligible");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);
        // Active but unpriced - fails ADR-479's required-standalone-price-for-primary rule.
        var primary = await SeedActiveCatalogItemAsync(accountId, ownerId, "Unpriced Furnace");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, excludedOptionalItemIds = Array.Empty<Guid>() }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.ExpandAssemblyNotOperationallyEligible", body.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.False(await db.Set<ProposedScopeLine>().AnyAsync(l => l.ProposedScopeId == scopeId));
    }

    [Fact]
    public async Task ExpandAssembly_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("expand-assembly-no-entitlement");
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = Guid.NewGuid(), excludedOptionalItemIds = Array.Empty<Guid>() }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExpandAssembly_ViewerRole_Returns403()
    {
        // Gate 3 (RequestsOperate AND ScopeCapture): Viewer holds neither.
        var (accountId, ownerId, _) = await SeedAccountAsync("expand-assembly-viewer");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "expand-assembly-viewer");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = Guid.NewGuid(), excludedOptionalItemIds = Array.Empty<Guid>() }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(viewerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExpandAssembly_ForAScopeOnARequestTheOperatorCannotSee_Returns404()
    {
        // Guards the locked gates -> request visibility -> act ordering (build-log/118): visibility
        // must be checked before the assembly is resolved.
        var (accountId, ownerId, _) = await SeedAccountAsync("expand-assembly-operator-mywork");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "expand-assembly-operator-mywork");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = Guid.NewGuid(), excludedOptionalItemIds = Array.Empty<Guid>() }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(operatorCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExpandAssembly_WithoutVersionHeader_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("expand-assembly-no-header");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, _) = await SeedDraftScopeAsync(accountId, ownerId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            $"/keep/pricebook/proposed-scopes/{scopeId}/expand-assembly",
            new { offeringAssemblyId = Guid.NewGuid(), excludedOptionalItemIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RetiredRawAddLineRoute_Returns404()
    {
        // Session 3.4d retired POST .../{id}/lines rather than re-gating it (build-log/118: no
        // legitimate caller exists yet). This pins the route absence itself, not just field-select's
        // presence, so a future accidental reintroduction of the caller-trusted endpoint fails here.
        var (accountId, ownerId, _) = await SeedAccountAsync("retired-raw-add-line");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);
        var catalogItem = await SeedActiveCatalogItemAsync(accountId, ownerId, "Labor");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/lines")
        {
            Content = JsonContent.Create(new
            {
                lineType = "KnownCatalogItem",
                catalogItemId = catalogItem.Id,
                offeringAssemblyId = (Guid?)null,
                quantity = 1m,
                isException = false,
                offCatalogDescription = (string?)null,
                offCatalogQuantity = (decimal?)null,
                note = (string?)null,
                displayOrder = 0,
                displayNameSnapshot = catalogItem.DisplayName,
                unitOfMeasureSnapshot = "each",
                offeringAssemblyNameSnapshot = (string?)null,
                defaultQuantitySnapshot = (decimal?)null,
            }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLine_WithAStaleVersion_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("update-line-stale");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, lineId, _) = await SeedDraftScopeWithLineAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/keep/pricebook/proposed-scopes/{scopeId}/lines/{lineId}")
        {
            Content = JsonContent.Create(new { quantity = 3m, isException = false, note = (string?)null, displayOrder = 1 }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, Guid.NewGuid().ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.VersionMismatch", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RemoveLine_UnknownLineId_Returns404()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("remove-line-unknown");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/keep/pricebook/proposed-scopes/{scopeId}/lines/{Guid.NewGuid()}");
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ProposedScope.LineNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_CorrectVersion_Returns200AndTransitionsStatus()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("submit-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/submit");
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<ProposedScope>().FindAsync(scopeId);
        Assert.Equal(ProposedScopeStatus.SubmittedToOffice, reloaded!.Status);
    }

    [Fact]
    public async Task Submit_ForAScopeOnARequestTheOperatorCannotSee_Returns404()
    {
        // Guards the authorization gap found in review: SubmitAsync must apply the Operator's
        // MyWork row-visibility scope, not just the account-level ADR-480 gates — otherwise any
        // account member could submit a scope on a request they have no participation in.
        var (accountId, ownerId, _) = await SeedAccountAsync("submit-operator-mywork");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "submit-operator-mywork");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        // The owner creates the scope; the operator is never attached to the request as
        // Responsible/Watching, so it must be invisible under MyWork.
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/submit");
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(operatorCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateLine_ForAScopeOnARequestTheOperatorCannotSee_WithAStaleVersion_Returns404NotConflict()
    {
        // Guards the ordering bug found in review: visibility must be checked before the
        // expected-version comparison, or a stale token on an invisible same-account scope would
        // 409 (revealing the row exists) instead of 404.
        var (accountId, ownerId, _) = await SeedAccountAsync("edit-operator-mywork");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "edit-operator-mywork");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (scopeId, lineId, _) = await SeedDraftScopeWithLineAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/keep/pricebook/proposed-scopes/{scopeId}/lines/{lineId}")
        {
            Content = JsonContent.Create(new { quantity = 3m, isException = false, note = (string?)null, displayOrder = 1 }),
        };
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, Guid.NewGuid().ToString("D"));
        var response = await AuthRequest(operatorCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("submit-no-entitlement");
        await EnrollAsync(accountId, ownerId);
        var (scopeId, version) = await SeedDraftScopeAsync(accountId, ownerId);

        // Re-seed the cookie for an account with no active entitlement: enrolling happens after
        // the scope is created so create/seed succeed, then the enrollment is revoked below.
        await using (var revokeScope = _factory.CreateScope())
        {
            var db = revokeScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var enrollment = await db.Set<AccountCapabilityPackageEnrollment>().SingleAsync(x => x.AccountId == accountId);
            db.Remove(enrollment);
            await db.SaveChangesAsync();
        }
        var cookie = await GetCookieAsync(ownerId, accountId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/proposed-scopes/{scopeId}/submit");
        request.Headers.Add(ProposedScopeVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Proposed Scope Test Co {slug}",
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

    private async Task<Guid> SeedRequestAsync(
        Guid accountId, KeepRequestStatus? terminalStatus = null, Guid? actorAccountUserId = null)
    {
        var now = DateTime.UtcNow;
        var customer = KeepCustomer.Create(accountId, "Jane Customer", "+15555550100");
        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "Leaky faucet",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", now, KeepRequestSource.Phone);

        if (terminalStatus.HasValue)
        {
            var changeResult = request.ChangeStatus(terminalStatus.Value, "Terminal for test", actorAccountUserId!.Value, "Test Owner", now);
            Assert.True(changeResult.IsSuccess);
        }

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

    private async Task<Guid> SeedAssemblyAsync(
        Guid accountId, Guid createdByUserId, Guid primaryCatalogItemId, params (Guid CatalogItemId, bool IsOptional)[] items)
    {
        var assembly = OfferingAssembly.Create(
            accountId, primaryCatalogItemId, "Test Assembly " + Guid.NewGuid(), PriceTreatment.AllInclusive, createdByUserId).Value;
        for (var i = 0; i < items.Length; i++)
            Assert.True(assembly.AddItem(items[i].CatalogItemId, 1, items[i].IsOptional, i, createdByUserId).IsSuccess);

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

    private async Task<(Guid ScopeId, Guid LineId, Guid ConcurrencyVersion)> SeedDraftScopeWithLineAsync(Guid accountId, Guid createdByUserId)
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
        return (scope.Id, addResult.Value.Id, scope.ConcurrencyVersion);
    }
}
