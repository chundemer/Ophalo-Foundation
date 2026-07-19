using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for the call handoff endpoints (GAP-020, ADR-448), plus the sibling
/// SMS handoff resolver's repaired Cache-Control/redaction posture fixed in the same change.
///
/// Seed layout: single account — Owner and Viewer members; one business-created KeepRequest
/// with a known customer phone.
/// </summary>
public sealed class KeepCallHandoffApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerUserId;
    private Guid _viewerUserId;
    private Guid _requestId;

    private const string CustomerPhone = "5559876543";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public KeepCallHandoffApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var provision = new AccountProvisioningService().CreateVerified(
            email: "owner@call-handoff-tests.com",
            name: "Handoff Owner",
            businessName: "Call Handoff Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provision.IsSuccess);
        var graph = provision.Value;

        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        _accountId   = graph.Account.Id;
        _ownerUserId = graph.Owner.Id;

        var viewerEmail = "viewer@call-handoff-tests.com";
        var viewerUser = User.CreateVerified(viewerEmail, "Handoff Viewer", now);
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_handoff_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);
        _viewerUserId = viewerMember.Id;

        var customer = KeepCustomer.Create(_accountId, "Jane Customer", CustomerPhone);
        db.Set<KeepCustomer>().Add(customer);

        var request = KeepRequest.CreateByBusiness(
            _accountId, customer.Id, "Jane Customer", CustomerPhone, null, "Leaky faucet",
            "REF-CALL-001", "tok_call_handoff", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(request);
        _requestId = request.Id;

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // POST /keep/requests/{requestId}/call-handoff — auth
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Post_Unauthenticated_Returns401()
    {
        var resp = await _client.PostAsync($"/keep/requests/{_requestId}/call-handoff", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Viewer_Returns403()
    {
        var cookie = await _factory.SeedSessionAsync(_viewerUserId, _accountId);
        using var req = PostAuthed(_requestId, cookie);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("KeepRequest.CallHandoffViewerBlocked", body!.Code);
    }

    [Fact]
    public async Task Post_UnknownRequestId_Returns404()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var req = PostAuthed(Guid.NewGuid(), cookie);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // -------------------------------------------------------------------------
    // POST — success and raw-token safety
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Post_Success_Returns200_WithHandoffUrl_NoRawPhone()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var req = PostAuthed(_requestId, cookie);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<HandoffCreatedBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains("/keep/share-call/", body!.HandoffUrl);
        // The opaque URL must never carry the raw customer phone.
        Assert.DoesNotContain(CustomerPhone, body.HandoffUrl!);
    }

    // -------------------------------------------------------------------------
    // GET /keep/share-call/{token} — resolve
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_ValidToken_Returns200_WithCustomerPhone()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var postReq = PostAuthed(_requestId, cookie);
        var postResp = await _client.SendAsync(postReq);
        var created = await postResp.Content.ReadFromJsonAsync<HandoffCreatedBody>(JsonOptions);

        var token = created!.HandoffUrl!.Split('/').Last();
        var getResp = await _client.GetAsync($"/keep/share-call/{token}");

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var body = await getResp.Content.ReadFromJsonAsync<HandoffResolvedBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(CustomerPhone, body.CustomerPhone);
    }

    [Fact]
    public async Task Get_ValidToken_ResponseHasCacheControlNoStore()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var postReq = PostAuthed(_requestId, cookie);
        var postResp = await _client.SendAsync(postReq);
        var created = await postResp.Content.ReadFromJsonAsync<HandoffCreatedBody>(JsonOptions);

        var token = created!.HandoffUrl!.Split('/').Last();
        var getResp = await _client.GetAsync($"/keep/share-call/{token}");

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        AssertNoStorePrivate(getResp);
    }

    [Fact]
    public async Task Get_InvalidToken_Returns404()
    {
        var getResp = await _client.GetAsync("/keep/share-call/not-a-real-token");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Sibling regression — /keep/share-sms/{token} now carries the same protections
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ShareSms_ValidToken_ResponseHasCacheControlNoStore()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var postReq = new HttpRequestMessage(HttpMethod.Post, $"/keep/requests/{_requestId}/sms-handoff")
        {
            Content = JsonContent.Create(new { messageBody = "Here is your tracker link." })
        };
        postReq.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var created = await postResp.Content.ReadFromJsonAsync<SmsHandoffCreatedBody>(JsonOptions);

        var token = created!.HandoffUrl!.Split('/').Last();
        var getResp = await _client.GetAsync($"/keep/share-sms/{token}");

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        AssertNoStorePrivate(getResp);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AssertNoStorePrivate(HttpResponseMessage resp)
    {
        Assert.True(resp.Headers.TryGetValues("Cache-Control", out var values));
        var cacheControl = string.Join(", ", values);
        Assert.Contains("no-store", cacheControl);
        Assert.Contains("private", cacheControl);
    }

    private static HttpRequestMessage PostAuthed(Guid requestId, string cookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/keep/requests/{requestId}/call-handoff");
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        return req;
    }

    private sealed record ProblemBody(string? Code, string? Detail);
    private sealed record HandoffCreatedBody(string? HandoffUrl, DateTime ExpiresAtUtc);
    private sealed record HandoffResolvedBody(string CustomerPhone, DateTime ExpiresAtUtc);
    private sealed record SmsHandoffCreatedBody(string? HandoffUrl, DateTime ExpiresAtUtc);
}
