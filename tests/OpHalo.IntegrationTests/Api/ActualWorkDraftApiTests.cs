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
/// HTTP integration tests for Direct Actual Work's draft create/edit/discard contract (ADR-487,
/// build-log/129, Batch 3):
///   POST   /keep/pricebook/actual-work/create
///   POST   /keep/pricebook/actual-work/{id}/lines
///   PUT    /keep/pricebook/actual-work/{id}/lines/{lineId}
///   DELETE /keep/pricebook/actual-work/{id}/lines/{lineId}
///   DELETE /keep/pricebook/actual-work/{id}
///
/// Covers the four-gate stack (account access, Price Book entitlement, RequestsOperate +
/// ActualWorkCapture permissions, active-Responsible row authorization), the price-blind wire
/// contract (a catalog-backed line's sell price/cost never appear in the response), and
/// draft-already-open / version-mismatch conflicts.
/// </summary>
public sealed class ActualWorkDraftApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private int _versionNumberCounter;

    public ActualWorkDraftApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_ResponsibleOperatorWithEntitlement_Returns200AndPersistsDraft()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-happy-path");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "create-happy-path");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, operatorId);

        var response = await AuthRequest(operatorCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("status").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<ActualWork>().Where(x => x.RequestId == requestId));
    }

    [Fact]
    public async Task Create_OperatorNotResponsible_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-not-responsible");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "create-not-responsible");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        // No Responsible participation seeded for this Operator.

        var response = await AuthRequest(operatorCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_OwnerNotResponsible_Returns404()
    {
        // Owner has AccountWide row visibility but is not the request's active Responsible
        // recorder — same indistinguishable 404 as an Operator outside MyWork scope.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-owner-not-responsible");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ViewerWithoutActualWorkCapture_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "create-viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, viewerId);

        var response = await AuthRequest(viewerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-no-entitlement");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SecondDraftForSameRequest_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-already-open");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);

        var first = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AddLine_CatalogBackedWithPrice_PersistsSnapshotButResponseIsPriceBlind()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-priced");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var item = await SeedPricedCatalogItemAsync(accountId, ownerId, "1-inch Filter");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new { catalogItemId = item.Id, actualQuantity = 2m, note = (string?)null })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("sellPriceSnapshot", out _));
        Assert.False(body.TryGetProperty("standardExpectedDirectCostSnapshot", out _));

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<ActualWorkLine>().SingleAsync(x => x.ActualWorkId == actualWorkId);
        Assert.Equal(100m, line.SellPriceSnapshot);
        Assert.Equal(60m, line.StandardExpectedDirectCostSnapshot);
        Assert.NotNull(line.PriceBookVersionLineId);
    }

    [Fact]
    public async Task AddLine_CustomLine_HasNoCatalogOrPriceSnapshot()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-custom");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Replaced a worn gasket", actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<ActualWorkLine>().SingleAsync(x => x.ActualWorkId == actualWorkId);
        Assert.Null(line.CatalogItemId);
        Assert.Null(line.PriceBookVersionLineId);
        Assert.Equal("Replaced a worn gasket", line.DisplayNameSnapshot);
    }

    [Fact]
    public async Task AddLine_MissingVersionHeader_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-missing-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/lines",
            new { catalogItemId = (Guid?)null, offCatalogDescription = "No header", actualQuantity = 1m, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddLine_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-stale-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Stale", actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", Guid.NewGuid().ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Discard_ResponsibleOperator_Returns204AndRemovesDraft()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("discard-happy-path");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/keep/pricebook/actual-work/{actualWorkId}");
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Empty(db.Set<ActualWork>().Where(x => x.Id == actualWorkId));
    }

    [Fact]
    public async Task Discard_SubmittedVisit_Returns409AndPreservesRow()
    {
        // Submitted visits are immutable — Discard must reject even with a valid current token
        // and must never hard-delete the row (release-blocking, no Submit API until Batch 4).
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("discard-submitted");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/keep/pricebook/actual-work/{actualWorkId}");
        request.Headers.Add("X-Keep-ActualWork-Version", submittedVersion.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(ActualWorkStatus.Submitted, reloaded.Status);
    }

    [Fact]
    public async Task AddLine_CatalogItemIdWithOffCatalogDescription_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-both-supplied");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var item = await SeedPricedCatalogItemAsync(accountId, ownerId, "Ambiguous line item");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = item.Id, offCatalogDescription = "Should be rejected", actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddLine_EmptyCatalogItemId_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-empty-catalog-id");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = Guid.Empty, offCatalogDescription = (string?)null, actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_ResponsibleOperatorWithLine_Returns200AndTransitionsToSubmitted()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-happy-path");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        await AddOffCatalogLineAsync(ownerCookie, actualWorkId, version);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = (string?)null, completionNote = (string?)null })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", (await GetVersionAsync(actualWorkId)).ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var visit = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(ActualWorkStatus.Submitted, visit.Status);

        var signal = await db.Set<KeepRequestWorkSignal>().SingleOrDefaultAsync(x =>
            x.AccountId == accountId && x.KeepRequestId == requestId &&
            x.SignalKey == KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview);
        Assert.NotNull(signal);
        Assert.Null(signal!.ResolvedAtUtc);
    }

    [Fact]
    public async Task Submit_ZeroLinesWithNoNoteOrOutcome_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-zero-line-invalid");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = (string?)null, completionNote = (string?)null })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var visit = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(ActualWorkStatus.Draft, visit.Status);
    }

    [Fact]
    public async Task Submit_ZeroLinesWithUndefinedOutcome_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-invalid-outcome");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = "NotARealOutcome", completionNote = "Some note" })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_ZeroLinesWithNoteAndOutcome_Returns200()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-zero-line-valid");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = "NoAccess", completionNote = "Gate was locked, no one home." })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var visit = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(ActualWorkStatus.Submitted, visit.Status);
        Assert.Equal(ActualWorkOutcome.NoAccess, visit.Outcome);
    }

    [Fact]
    public async Task Submit_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-stale-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        await AddOffCatalogLineAsync(ownerCookie, actualWorkId, version);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = (string?)null, completionNote = (string?)null })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", Guid.NewGuid().ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Submit_OperatorNotResponsible_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-not-responsible");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "submit-not-responsible");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = "NoAccess", completionNote = "Gate was locked." })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(operatorCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Submit_ViewerWithoutActualWorkCapture_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "submit-viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = "NoAccess", completionNote = "Gate was locked." })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(viewerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Submit_SubmittedVisit_Returns409AndPreservesImmutableState()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("submit-already-submitted");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = "NoAccess", completionNote = "Should be rejected." })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", submittedVersion.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var visit = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(ActualWorkStatus.Submitted, visit.Status);
        // Original submission's outcome must survive untouched — proof the second submit attempt
        // never reached the domain transition.
        Assert.Equal(ActualWorkOutcome.NoWorkAuthorized, visit.Outcome);
    }

    [Fact]
    public async Task ExpandAssembly_CommitsRequiredItemsAndSkipsOptionalByDefault()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("expand-happy-path");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Condenser Unit");
        var required = await SeedPricedCatalogItemAsync(accountId, ownerId, "Refrigerant Line Set");
        var optional = await SeedPricedCatalogItemAsync(accountId, ownerId, "Optional Surge Protector");
        var assemblyId = await SeedAssemblyAsync(
            accountId, ownerId, primary.Id, (required.Id, IsOptional: false), (optional.Id, IsOptional: true));

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, includedOptionalItemIds = Array.Empty<Guid>() })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("lineIds").GetArrayLength());
        Assert.Empty(body.GetProperty("skippedCatalogItemIds").EnumerateArray());
        Assert.False(body.TryGetProperty("sellPriceSnapshot", out _));

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var lines = await db.Set<ActualWorkLine>().Where(x => x.ActualWorkId == actualWorkId).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.CatalogItemId == primary.Id);
        Assert.Contains(lines, l => l.CatalogItemId == required.Id);
        Assert.DoesNotContain(lines, l => l.CatalogItemId == optional.Id);
        Assert.All(lines, l => Assert.Equal(100m, l.SellPriceSnapshot));
    }

    [Fact]
    public async Task ExpandAssembly_IncludesExplicitlySelectedOptionalItem()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("expand-optional-included");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Condenser Unit");
        var optional = await SeedPricedCatalogItemAsync(accountId, ownerId, "Optional Surge Protector");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id, (optional.Id, IsOptional: true));

        await using var seedScope = _factory.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var optionalItemId = await seedDb.Set<OfferingAssemblyItem>()
            .Where(i => i.OfferingAssemblyId == assemblyId && i.CatalogItemId == optional.Id)
            .Select(i => i.Id)
            .SingleAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, includedOptionalItemIds = new[] { optionalItemId } })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var lines = await db.Set<ActualWorkLine>().Where(x => x.ActualWorkId == actualWorkId).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.CatalogItemId == optional.Id);
    }

    [Fact]
    public async Task ExpandAssembly_SkipsAndReportsAComponentAlreadyOnTheDraft()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("expand-skip-report");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Condenser Unit");
        var required = await SeedPricedCatalogItemAsync(accountId, ownerId, "Refrigerant Line Set");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id, (required.Id, IsOptional: false));

        // Manually add the primary item first — the expansion must skip regenerating it.
        var addLineRequest = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new { catalogItemId = primary.Id, actualQuantity = 1m, note = (string?)null })
        };
        addLineRequest.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var addLineResponse = await AuthRequest(ownerCookie).SendAsync(addLineRequest);
        Assert.Equal(HttpStatusCode.OK, addLineResponse.StatusCode);
        var addLineBody = await addLineResponse.Content.ReadFromJsonAsync<JsonElement>();
        var versionAfterAddLine = addLineBody.GetProperty("actualWorkConcurrencyVersion").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, includedOptionalItemIds = Array.Empty<Guid>() })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", versionAfterAddLine.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(body.GetProperty("lineIds").EnumerateArray());
        var skipped = body.GetProperty("skippedCatalogItemIds").EnumerateArray().Select(x => x.GetGuid()).ToList();
        Assert.Single(skipped);
        Assert.Equal(primary.Id, skipped[0]);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var lines = await db.Set<ActualWorkLine>().Where(x => x.ActualWorkId == actualWorkId).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Single(lines, l => l.CatalogItemId == primary.Id);
        Assert.Single(lines, l => l.CatalogItemId == required.Id);
    }

    [Fact]
    public async Task ExpandAssembly_UnknownInclusionId_Returns400AndWritesNoLines()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("expand-invalid-inclusion");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Condenser Unit");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, includedOptionalItemIds = new[] { Guid.NewGuid() } })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.False(await db.Set<ActualWorkLine>().AnyAsync(x => x.ActualWorkId == actualWorkId));
    }

    [Fact]
    public async Task ExpandAssembly_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("expand-stale-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Condenser Unit");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, includedOptionalItemIds = Array.Empty<Guid>() })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", Guid.NewGuid().ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ExpandAssembly_OwnerNotResponsible_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("expand-not-responsible");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "expand-not-responsible");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, operatorId);
        var (actualWorkId, version) = await CreateDraftAsync(await GetCookieAsync(operatorId, accountId), requestId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Condenser Unit");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id);

        // Owner has AccountWide row visibility but is not this request's active Responsible
        // recorder — same indistinguishable 404 as every other draft mutation gate.
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, includedOptionalItemIds = Array.Empty<Guid>() })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExpandAssembly_ViewerWithoutActualWorkCapture_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("expand-viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "expand-viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var primary = await SeedPricedCatalogItemAsync(accountId, ownerId, "Condenser Unit");
        var assemblyId = await SeedAssemblyAsync(accountId, ownerId, primary.Id);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/expand-assembly")
        {
            Content = JsonContent.Create(new { offeringAssemblyId = assemblyId, includedOptionalItemIds = Array.Empty<Guid>() })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(viewerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task AddOffCatalogLineAsync(string cookie, Guid actualWorkId, Guid version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Drain pan replacement", actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> GetVersionAsync(Guid actualWorkId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        return await db.Set<ActualWork>().Where(x => x.Id == actualWorkId).Select(x => x.ConcurrencyVersion).SingleAsync();
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

    private async Task<(Guid ActualWorkId, Guid ConcurrencyVersion)> CreateDraftAsync(string cookie, Guid requestId)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync("/keep/pricebook/actual-work/create", new { requestId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("concurrencyVersion").GetGuid());
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

    private async Task<CatalogItem> SeedPricedCatalogItemAsync(Guid accountId, Guid createdByUserId, string displayName)
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

        var versionId = Guid.NewGuid();
        var versionNumber = ++_versionNumberCounter;
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
        var createResult = OfferingAssembly.Create(
            accountId, primaryCatalogItemId, $"Assembly {Guid.NewGuid():N}", PriceTreatment.Summed, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var assembly = createResult.Value;

        var displayOrder = 1;
        foreach (var (catalogItemId, isOptional) in items)
        {
            var addResult = assembly.AddItem(catalogItemId, defaultQuantity: 1m, isOptional, displayOrder++, createdByUserId);
            Assert.True(addResult.IsSuccess);
        }

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<OfferingAssembly>().Add(assembly);
        await db.SaveChangesAsync();
        return assembly.Id;
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Actual Work Test Co {slug}",
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
}
