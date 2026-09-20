using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for PUT /keep/setup/policy (GAP-100 6a-1, ADR-506): governed full-snapshot save,
/// missing/stale <c>settingsVersion</c> → recoverable 409, non-positive numbers and unknown
/// timing bases → 400, omitted bases preserved.
/// </summary>
public sealed class KeepSetupPolicyApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient        _client;

    private Guid _accountId;
    private Guid _ownerUserId;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public KeepSetupPolicyApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@policy-tests.com",
            name: "Policy Owner",
            businessName: "Policy Co",
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

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        _accountId   = graph.Account.Id;
        _ownerUserId = graph.Owner.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Put_Unauthenticated_Returns401()
    {
        var response = await _client.PutAsJsonAsync("/keep/setup/policy", Body(version: "x"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithCurrentVersion_Returns200WithNewVersion()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var before = await GetSetupAsync(cookie);

        var response = await PutAsync(cookie, Body(first: 30, version: before.SettingsVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
        Assert.Equal(30, after.ResponsePolicy.FirstResponseTargetMinutes);
        Assert.NotEqual(before.SettingsVersion, after.SettingsVersion);
    }

    [Fact]
    public async Task Put_MissingVersion_Returns409()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var response = await PutAsync(cookie, Body(version: null));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_StaleVersion_Returns409_AndWritesNothing()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var before = await GetSetupAsync(cookie);

        var response = await PutAsync(cookie, Body(first: 30, version: "stale-version"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await GetSetupAsync(cookie);
        Assert.Equal(before.ResponsePolicy.FirstResponseTargetMinutes, after.ResponsePolicy.FirstResponseTargetMinutes);
        Assert.Equal(before.SettingsVersion, after.SettingsVersion);
    }

    [Theory]
    [InlineData(0, 240, 60, 5)]
    [InlineData(60, -5, 60, 5)]
    [InlineData(60, 240, 0, 5)]
    [InlineData(60, 240, 60, 0)]
    public async Task Put_NonPositiveNumber_Returns400_NotAServerError(int a, int b, int c, int d)
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie, Body(a, b, c, d, version: v));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UnknownTimingBasis_Returns400()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie, Body(version: v, firstBasis: "Funday"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_OmittedBases_PreserveThePersistedBases()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v1 = (await GetSetupAsync(cookie)).SettingsVersion;
        var cal = await _client.SendAsync(CalendarRequest(cookie, v1));
        Assert.Equal(HttpStatusCode.OK, cal.StatusCode);
        var v2 = (await GetSetupAsync(cookie)).SettingsVersion;
        var staffed = await PutAsync(cookie, Body(version: v2, firstBasis: "StaffedHours"));
        Assert.Equal(HttpStatusCode.OK, staffed.StatusCode);
        var v3 = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie, Body(first: 90, version: v3));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
        Assert.Equal("StaffedHours", after.ResponsePolicy.FirstResponseTimingBasis);
        Assert.Equal(90, after.ResponsePolicy.FirstResponseTargetMinutes);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static object Body(
        int first = 60, int standard = 240, int priority = 60, int threshold = 5,
        string? version = "v", string? firstBasis = null) => new
        {
            FirstResponseTargetMinutes    = first,
            StandardResponseTargetMinutes = standard,
            PriorityResponseTargetMinutes = priority,
            StatusCheckThresholdDays      = threshold,
            FirstResponseTimingBasis      = firstBasis,
            SettingsVersion               = version
        };

    private async Task<KeepSetupResult> GetSetupAsync(string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> PutAsync(string cookie, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/keep/setup/policy");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    // A weekly interval is the precondition for a StaffedHours basis (governed structural gate).
    private static HttpRequestMessage CalendarRequest(string cookie, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/keep/setup/calendar");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        request.Content = JsonContent.Create(new
        {
            WeeklyIntervals = new[] { new { Weekday = "Monday", OpensAt = "00:00", ClosesAt = "23:59" } },
            ClosureDates    = Array.Empty<string>(),
            SettingsVersion = version
        });
        return request;
    }
}
