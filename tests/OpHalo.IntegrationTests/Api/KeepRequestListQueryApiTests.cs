using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for GET /keep/requests — Sessions 4A/4B query contract.
///
/// Covers: auth gate, response shape (pageInfo/viewCounts/listContext), all 400 error
/// codes from query validation and binder, named views, filter/search validation,
/// cursor pagination, cursor HMAC tamper detection. The real HmacKeepRequestListCursorProtector
/// is exercised here; unit tests use FakeCursorProtector (plain base64) and do not cover HMAC integrity.
///</summary>
public sealed class KeepRequestListQueryApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private string _ownerCookie    = string.Empty;
    private string _operatorCookie = string.Empty;

    public KeepRequestListQueryApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@4a-query-tests.com",
            name: "4A Owner",
            businessName: "4A Services",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess);
        var graph = provisionResult.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        var accountId = graph.Account.Id;

        // Seed a shared customer
        var customer = KeepCustomer.Create(accountId, "Cursor Test Customer", "0400000001");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Seed 3 open requests so pagination tests (limit=2) have two pages
        for (var i = 1; i <= 3; i++)
        {
            var req = KeepRequest.CreateFromCustomerIntake(
                accountId, customer.Id,
                "Cursor Test Customer", "0400000001", null,
                $"Pagination request {i}", $"4A-PAG-{i:D3}",
                $"4a_pag_token_{i}", now, 60);
            db.Set<KeepRequest>().Add(req);
            db.Set<KeepRequestEvent>().Add(
                KeepRequestEvent.CreateRequestCreated(req.Id, accountId, now));
        }

        await db.SaveChangesAsync();

        _ownerCookie = await _factory.SeedSessionAsync(graph.Owner.Id, accountId);

        // Operator for 4C unassigned-view tests
        var operatorEmail = "operator@4a-query-tests.com";
        var operatorUser  = User.CreateVerified(operatorEmail, "4A Operator", now);
        var operatorMember = AccountUser.CreatePendingInvite(
            accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_4a_query",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        await db.SaveChangesAsync();

        _operatorCookie = await _factory.SeedSessionAsync(operatorMember.Id, accountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Helpers ----------------------------------------------------------------

    private HttpRequestMessage WithCookie(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={_ownerCookie}");
        return req;
    }

    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var req = WithCookie(path);
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> GetAsAsync(string path, string rawCookie)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={rawCookie}");
        return await _client.SendAsync(req);
    }

    private async Task<string?> GetErrorCodeAsync(string path)
    {
        var res = await GetAsync(path);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // Base64Url decode (no padding) → UTF8 string
    private static string DecodeBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    // Base64Url encode (no padding)
    private static string EncodeBase64Url(string s)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // --- Auth gate --------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var res = await _client.GetAsync("/keep/requests");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // --- 200 response shape -----------------------------------------------------

    [Fact]
    public async Task Authenticated_request_returns_200_with_correct_shape()
    {
        var res = await GetAsync("/keep/requests");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(body);
        Assert.NotNull(body.PageInfo);
        Assert.Equal(50, body.PageInfo.Limit);
        Assert.False(body.PageInfo.HasMore); // 3 requests < 50
        Assert.Null(body.PageInfo.NextCursor);

        Assert.NotNull(body.ViewCounts);

        Assert.NotNull(body.ListContext);
        Assert.Equal("default", body.ListContext.View);
        Assert.True(body.ListContext.IsDefaultCommandCenter);
        Assert.False(body.ListContext.IsHistory);
        Assert.False(body.ListContext.IsSearch);
    }

    // --- View validation --------------------------------------------------------

    [Fact]
    public async Task Unknown_view_returns_400_InvalidView()
    {
        var code = await GetErrorCodeAsync("/keep/requests?view=not_a_view");
        Assert.Equal("KeepRequest.RequestListInvalidView", code);
    }

    [Fact]
    public async Task Known_active_view_returns_200_for_owner()
    {
        var res = await GetAsync("/keep/requests?view=assigned_to_me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task History_view_returns_200_for_owner()
    {
        var res = await GetAsync("/keep/requests?view=closed_history");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // --- Date format validation (must surface before filter gate) ---------------

    [Fact]
    public async Task Malformed_date_returns_400_InvalidDateFormat()
    {
        var code = await GetErrorCodeAsync("/keep/requests?createdFrom=banana");
        Assert.Equal("KeepRequest.RequestListInvalidDateFormat", code);
    }

    [Fact]
    public async Task Date_only_string_returns_400_InvalidDateFormat()
    {
        var code = await GetErrorCodeAsync("/keep/requests?createdFrom=2026-06-18");
        Assert.Equal("KeepRequest.RequestListInvalidDateFormat", code);
    }

    [Fact]
    public async Task Unzoned_datetime_returns_400_InvalidDateFormat()
    {
        var code = await GetErrorCodeAsync("/keep/requests?createdFrom=2026-06-18T00:00:00");
        Assert.Equal("KeepRequest.RequestListInvalidDateFormat", code);
    }

    [Fact]
    public async Task Valid_utc_createdFrom_returns_200()
    {
        var res = await GetAsync("/keep/requests?createdFrom=2026-06-18T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task ClosedFrom_with_non_history_view_returns_400_Contradictory()
    {
        var code = await GetErrorCodeAsync("/keep/requests?closedFrom=2026-06-18T00:00:00Z");
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", code);
    }

    // --- Contradiction detection ------------------------------------------------

    [Fact]
    public async Task Closed_history_with_active_status_returns_400_Contradictory()
    {
        var code = await GetErrorCodeAsync("/keep/requests?view=closed_history&status=received");
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", code);
    }

    [Fact]
    public async Task Default_view_with_terminal_status_returns_400_Contradictory()
    {
        var code = await GetErrorCodeAsync("/keep/requests?view=default&status=closed");
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", code);
    }

    // --- Filter/search (4B) -----------------------------------------------------

    [Fact]
    public async Task Valid_status_filter_returns_200()
    {
        var res = await GetAsync("/keep/requests?status=received");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_status_filter_returns_400_InvalidStatus()
    {
        var code = await GetErrorCodeAsync("/keep/requests?status=garbage");
        Assert.Equal("KeepRequest.RequestListInvalidStatus", code);
    }

    [Fact]
    public async Task Invalid_attentionReason_returns_400_InvalidAttentionReason()
    {
        var code = await GetErrorCodeAsync("/keep/requests?attentionReason=not_a_reason");
        Assert.Equal("KeepRequest.RequestListInvalidAttentionReason", code);
    }

    [Fact]
    public async Task Search_q_returns_200()
    {
        var res = await GetAsync("/keep/requests?q=pagination");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // --- Limit validation -------------------------------------------------------

    [Fact]
    public async Task Limit_zero_returns_400_InvalidLimit()
    {
        var code = await GetErrorCodeAsync("/keep/requests?limit=0");
        Assert.Equal("KeepRequest.RequestListInvalidLimit", code);
    }

    [Fact]
    public async Task Limit_101_returns_400_InvalidLimit()
    {
        var code = await GetErrorCodeAsync("/keep/requests?limit=101");
        Assert.Equal("KeepRequest.RequestListInvalidLimit", code);
    }

    [Fact]
    public async Task Limit_non_integer_returns_400_InvalidLimit()
    {
        var code = await GetErrorCodeAsync("/keep/requests?limit=abc");
        Assert.Equal("KeepRequest.RequestListInvalidLimit", code);
    }

    [Fact]
    public async Task Empty_limit_returns_400_InvalidLimit()
    {
        var code = await GetErrorCodeAsync("/keep/requests?limit=");
        Assert.Equal("KeepRequest.RequestListInvalidLimit", code);
    }

    // --- Binder validation ------------------------------------------------------

    [Fact]
    public async Task Unknown_parameter_returns_400_UnknownParameter()
    {
        var code = await GetErrorCodeAsync("/keep/requests?foo=bar");
        Assert.Equal("KeepRequest.RequestListUnknownParameter", code);
    }

    [Fact]
    public async Task Empty_assignedAccountUserId_returns_400_InvalidAssignedId()
    {
        var code = await GetErrorCodeAsync("/keep/requests?assignedAccountUserId=");
        Assert.Equal("KeepRequest.RequestListInvalidAssignedAccountUserId", code);
    }

    [Fact]
    public async Task Malformed_assignedAccountUserId_returns_400_InvalidAssignedId()
    {
        var code = await GetErrorCodeAsync("/keep/requests?assignedAccountUserId=not-a-guid");
        Assert.Equal("KeepRequest.RequestListInvalidAssignedAccountUserId", code);
    }

    // --- Cursor token errors ----------------------------------------------------

    [Fact]
    public async Task Junk_cursor_returns_400_InvalidCursor()
    {
        var code = await GetErrorCodeAsync("/keep/requests?cursor=totallyinvalidtoken");
        Assert.Equal("KeepRequest.RequestListInvalidCursor", code);
    }

    // --- Pagination: shape -------------------------------------------------------

    [Fact]
    public async Task Limit_2_with_3_requests_returns_hasMore_and_nextCursor()
    {
        var res = await GetAsync("/keep/requests?limit=2");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(body);
        Assert.Equal(2, body.PageInfo.Limit);
        Assert.True(body.PageInfo.HasMore);
        Assert.NotNull(body.PageInfo.NextCursor);
        Assert.Equal(2, body.Requests.Count);
    }

    [Fact]
    public async Task Cursor_follow_returns_remaining_requests_with_no_more()
    {
        // Page 1
        var page1Res = await GetAsync("/keep/requests?limit=2");
        Assert.Equal(HttpStatusCode.OK, page1Res.StatusCode);
        var page1 = await page1Res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(page1);
        var cursor = page1.PageInfo.NextCursor!;
        var page1Ids = page1.Requests.Select(r => r.Id).ToList();

        // Page 2
        var page2Res = await GetAsync($"/keep/requests?limit=2&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(HttpStatusCode.OK, page2Res.StatusCode);
        var page2 = await page2Res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(page2);

        Assert.Single(page2.Requests);
        Assert.False(page2.PageInfo.HasMore);
        Assert.Null(page2.PageInfo.NextCursor);

        // Pages are disjoint
        Assert.Empty(page1Ids.Intersect(page2.Requests.Select(r => r.Id)));
    }

    // --- HMAC integrity (real HmacKeepRequestListCursorProtector) ---------------

    [Fact]
    public async Task Cursor_with_tampered_signature_returns_400_InvalidCursor()
    {
        // Get a real cursor
        var res = await GetAsync("/keep/requests?limit=2");
        var body = await res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var cursor = body!.PageInfo.NextCursor!;

        // Cursor format: base64url(payload) + "." + base64url(sig)
        var dot = cursor.LastIndexOf('.');
        var payloadPart = cursor[..dot];

        // Replace sig with a different base64url-encoded string
        var tamperedSig = EncodeBase64Url("invalidsignature_definitely_wrong");
        var tamperedCursor = $"{payloadPart}.{tamperedSig}";

        var code = await GetErrorCodeAsync(
            $"/keep/requests?limit=2&cursor={Uri.EscapeDataString(tamperedCursor)}");
        Assert.Equal("KeepRequest.RequestListInvalidCursor", code);
    }

    [Fact]
    public async Task Cursor_with_tampered_payload_returns_400_InvalidCursor()
    {
        // Get a real cursor
        var res = await GetAsync("/keep/requests?limit=2");
        var body = await res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var cursor = body!.PageInfo.NextCursor!;

        // Cursor format: base64url(payload) + "." + base64url(sig)
        var dot = cursor.LastIndexOf('.');
        var payloadPart = cursor[..dot];
        var sigPart = cursor[(dot + 1)..];

        // Decode payload JSON and modify the ranking order
        var payloadJson = DecodeBase64Url(payloadPart);
        var doc = JsonSerializer.Deserialize<JsonElement>(payloadJson);
        var modifiedJson = payloadJson.Replace(
            $"\"rankingOrder\":{doc.GetProperty("rankingOrder").GetInt32()}",
            "\"rankingOrder\":99");
        var tamperedPayload = EncodeBase64Url(modifiedJson);
        var tamperedCursor = $"{tamperedPayload}.{sigPart}";

        var code = await GetErrorCodeAsync(
            $"/keep/requests?limit=2&cursor={Uri.EscapeDataString(tamperedCursor)}");
        Assert.Equal("KeepRequest.RequestListInvalidCursor", code);
    }

    // --- view=default equivalence -----------------------------------------------

    [Fact]
    public async Task Cursor_from_no_view_request_is_valid_with_view_default()
    {
        // Cursor issued with no view parameter must be reusable with ?view=default.
        var page1Res = await GetAsync("/keep/requests?limit=2");
        var page1 = await page1Res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var cursor = page1!.PageInfo.NextCursor!;

        var page2Res = await GetAsync(
            $"/keep/requests?view=default&limit=2&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(HttpStatusCode.OK, page2Res.StatusCode);

        var page2 = await page2Res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(page2);
        Assert.Single(page2.Requests);
    }

    // --- Session 4C: Operator unassigned view and rowContext --------------------

    [Fact]
    public async Task Operator_unassigned_view_returns_200()
    {
        var res = await GetAsAsync("/keep/requests?view=unassigned", _operatorCookie);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task rowContext_field_present_on_each_row()
    {
        var res = await GetAsync("/keep/requests");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<ListResponseBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.NotEmpty(body.Requests);
        Assert.All(body.Requests, r => Assert.False(string.IsNullOrEmpty(r.RowContext)));
    }

    // --- Response DTOs ----------------------------------------------------------

    private sealed record ListResponseBody(
        List<ListRequestBody> Requests,
        PageInfoBody PageInfo,
        object? ViewCounts,
        ListContextBody? ListContext);

    private sealed record ListRequestBody(Guid Id, string? RowContext);

    private sealed record PageInfoBody(int Limit, bool HasMore, string? NextCursor);

    private sealed record ListContextBody(
        string View,
        bool IsDefaultCommandCenter,
        bool IsHistory,
        bool IsSearch);

}
