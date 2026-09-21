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
/// Integration tests for PUT /keep/setup/calendar (GAP-100 5c-1b, ADR-506): full-snapshot save,
/// missing/stale <c>settingsVersion</c> → recoverable 409, domain-rule violations → 422, and
/// binding/parse failures → 400.
/// </summary>
public sealed class KeepSetupCalendarApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient        _client;

    private Guid _accountId;
    private Guid _ownerUserId;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public KeepSetupCalendarApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@calendar-tests.com",
            name: "Calendar Owner",
            businessName: "Calendar Co",
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

    private static object[] Monday(string opens = "09:00", string closes = "10:00") =>
        [new { Weekday = "Monday", OpensAt = opens, ClosesAt = closes }];

    [Fact]
    public async Task Put_Unauthenticated_Returns401()
    {
        var response = await _client.PutAsJsonAsync("/keep/setup/calendar",
            new { WeeklyIntervals = Monday(), Closures = Array.Empty<object>(), SettingsVersion = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithCurrentVersion_Returns200WithRefreshedSnapshotAndNewVersion()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var before = await GetSetupAsync(cookie);

        var response = await PutAsync(cookie, Monday(), ["2026-12-25"], before.SettingsVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
        Assert.Equal("Monday", Assert.Single(after.Calendar.WeeklyIntervals).Weekday);
        Assert.Equal([("2026-12-25", (string?)null)], after.Calendar.Closures.Select(c => (c.Date, c.Reason)));
        Assert.NotEqual(before.SettingsVersion, after.SettingsVersion);
    }

    [Fact]
    public async Task Put_MissingVersion_Returns409()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var response = await PutAsync(cookie, Monday(), [], null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_StaleVersion_Returns409_AndWritesNothing()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var before = await GetSetupAsync(cookie);

        var response = await PutAsync(cookie, Monday(), [], "stale-version");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await GetSetupAsync(cookie);
        Assert.Empty(after.Calendar.WeeklyIntervals);
        Assert.Equal(before.SettingsVersion, after.SettingsVersion);
    }

    [Fact]
    public async Task Put_DuplicateWeekday_Returns422()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie,
            [new { Weekday = "Monday", OpensAt = "09:00", ClosesAt = "10:00" },
             new { Weekday = "Monday", OpensAt = "11:00", ClosesAt = "12:00" }], [], v);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_DuplicateClosureDate_Returns422()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie, Monday(), ["2026-12-25", "2026-12-25"], v);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_RemovingAllIntervalsWhileStaffedHoursTargetExists_Returns422()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v1 = (await GetSetupAsync(cookie)).SettingsVersion;
        var seeded = await PutAsync(cookie, Monday(), [], v1);
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);
        await SeedStaffedHoursPolicyAsync(targetMinutes: 10_000);
        var v2 = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie, [], [], v2);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_ShrinkingHoursSoStaffedTargetIsUnreachable_Returns422()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v1 = (await GetSetupAsync(cookie)).SettingsVersion;
        var seeded = await PutAsync(cookie, Monday(), [], v1);
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);
        await SeedStaffedHoursPolicyAsync(targetMinutes: 10_000);
        var v2 = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie, Monday("09:00", "09:30"), [], v2);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("Funday", "09:00", "10:00", "2026-12-25")]
    [InlineData("Monday", "9am", "10:00", "2026-12-25")]
    [InlineData("Monday", "09:00", "10:00", "25/12/2026")]
    public async Task Put_UnparseableValue_Returns400(string weekday, string opens, string closes, string closure)
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v = (await GetSetupAsync(cookie)).SettingsVersion;

        var response = await PutAsync(cookie,
            [new { Weekday = weekday, OpensAt = opens, ClosesAt = closes }], [closure], v);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_OmittedCollections_Returns400()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/keep/setup/calendar");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        request.Content = JsonContent.Create(new { SettingsVersion = "x" });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- ADR-507 closure reasons (GAP-100 batch 7b) ---

    private static object Closure(string date, string? reason) => new { Date = date, Reason = reason };

    [Fact]
    public async Task Put_ClosureReason_RoundTripsThroughGetAndCanBeChangedAndCleared()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        var v0 = (await GetSetupAsync(cookie)).SettingsVersion;
        var put = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", "  Thanksgiving Day ")], v0);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var afterPut = (await put.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
        Assert.Equal([("2026-11-26", (string?)"Thanksgiving Day")], afterPut.Calendar.Closures.Select(c => (c.Date, c.Reason)));

        var got = await GetSetupAsync(cookie);
        Assert.Equal(afterPut.SettingsVersion, got.SettingsVersion);
        Assert.Equal("Thanksgiving Day", Assert.Single(got.Calendar.Closures).Reason);

        var changed = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", "Annual training")], got.SettingsVersion);
        var afterChange = (await changed.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
        Assert.Equal("Annual training", Assert.Single(afterChange.Calendar.Closures).Reason);
        Assert.NotEqual(got.SettingsVersion, afterChange.SettingsVersion);

        var cleared = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", "   ")], afterChange.SettingsVersion);
        var afterClear = (await cleared.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
        Assert.Null(Assert.Single(afterClear.Calendar.Closures).Reason);
        Assert.NotEqual(afterChange.SettingsVersion, afterClear.SettingsVersion);
    }

    [Fact]
    public async Task Put_ReasonOnlyChange_WithStaleVersion_Returns409_AndKeepsTheStoredReason()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v0 = (await GetSetupAsync(cookie)).SettingsVersion;
        var first = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", "A")], v0);
        var stale = (await first.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!.SettingsVersion;
        var second = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", "B")], stale);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var response = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", "C")], stale);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("B", Assert.Single((await GetSetupAsync(cookie)).Calendar.Closures).Reason);
    }

    [Theory]
    [InlineData("line one\nline two")]
    [InlineData("tab\there")]
    public async Task Put_InvalidReason_Returns400_AndWritesNothing(string reason)
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var before = await GetSetupAsync(cookie);

        var response = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", reason)], before.SettingsVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await GetSetupAsync(cookie);
        Assert.Empty(after.Calendar.Closures);
        Assert.Equal(before.SettingsVersion, after.SettingsVersion);
    }

    [Fact]
    public async Task Put_TooLongReason_Returns400_AndSixtyCharactersIsAccepted()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var v = (await GetSetupAsync(cookie)).SettingsVersion;

        var tooLong = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", new string('x', 61))], v);
        var ok = await PutClosuresAsync(cookie, Monday(), [Closure("2026-11-26", new string('x', 60))], v);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Put_LegacyDatesOnlyBody_IsRejectedAs400()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/keep/setup/calendar");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        request.Content = JsonContent.Create(new
        {
            WeeklyIntervals = Monday(), ClosureDates = new[] { "2026-12-25" }, SettingsVersion = "x"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<KeepSetupResult> GetSetupAsync(string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KeepSetupResult>(JsonOptions))!;
    }

    private Task<HttpResponseMessage> PutAsync(
        string cookie, object[] intervals, string[] closures, string? version) =>
        PutClosuresAsync(cookie, intervals, closures.Select(d => (object)new { Date = d, Reason = (string?)null }).ToArray(), version);

    private async Task<HttpResponseMessage> PutClosuresAsync(
        string cookie, object[] intervals, object[] closures, string? version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/keep/setup/calendar");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        request.Content = JsonContent.Create(new
        {
            WeeklyIntervals = intervals,
            Closures        = closures,
            SettingsVersion = version
        });
        return await _client.SendAsync(request);
    }

    // Writes the policy directly (bypassing the governed structural gate) so the calendar PUT is
    // what trips the staffed-hours invariants.
    private async Task SeedStaffedHoursPolicyAsync(int targetMinutes)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var policy = await db.Set<KeepResponsePolicy>().SingleOrDefaultAsync(p => p.AccountId == _accountId);
        if (policy is null)
        {
            policy = KeepResponsePolicy.Create(_accountId, 60, 240, 60, 5);
            db.Set<KeepResponsePolicy>().Add(policy);
        }
        policy.UpdateTargetsAndTimingBasis(
            targetMinutes, policy.StandardResponseTargetMinutes, policy.PriorityResponseTargetMinutes,
            policy.StatusCheckThresholdDays,
            ResponseTimingBasis.StaffedHours, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous);
        await db.SaveChangesAsync();
    }
}
