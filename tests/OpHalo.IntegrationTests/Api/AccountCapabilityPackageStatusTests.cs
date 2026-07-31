using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 1c's Owner/Admin capability-package status read:
///   GET /accounts/me/capability-packages
///
/// Test DB is reset once per class. Each test seeds what it needs in the method body.
/// </summary>
public sealed class AccountCapabilityPackageStatusTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    public AccountCapabilityPackageStatusTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/accounts/me/capability-packages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Operator_Returns403()
    {
        var (accountId, _, _) = await SeedAccountAsync();
        var (operatorId, _) = await SeedActiveMemberAsync(accountId, "operator@capability-status-tests.com", AccountUserRole.Operator);
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync("/accounts/me/capability-packages");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountUserRole.Owner)]
    [InlineData(AccountUserRole.Admin)]
    public async Task OwnerOrAdmin_ReturnsGenericStatusCollection(AccountUserRole role)
    {
        var (accountId, ownerAccountUserId, ownerCookie) = await SeedAccountAsync();

        string cookie = ownerCookie;
        if (role != AccountUserRole.Owner)
        {
            var (adminId, _) = await SeedActiveMemberAsync(accountId, $"admin@capability-status-tests.com", AccountUserRole.Admin);
            cookie = await GetCookieAsync(adminId, accountId);
        }

        var response = await AuthRequest(cookie).GetAsync("/accounts/me/capability-packages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);

        var entries = body.EnumerateArray().ToList();
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.True(entry.TryGetProperty("featureKey", out var featureKey));
            Assert.False(string.IsNullOrWhiteSpace(featureKey.GetString()));
            Assert.True(entry.TryGetProperty("enabled", out var enabled));
            Assert.True(enabled.ValueKind is JsonValueKind.True or JsonValueKind.False);
        }

        // No enrollment was seeded — every registered key is disabled by default.
        Assert.All(entries, e => Assert.False(e.GetProperty("enabled").GetBoolean()));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(
        string ownerEmail = "owner@capability-status-tests.com")
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: ownerEmail,
            name: "Owner",
            businessName: "Capability Status Test Co",
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

        var ownerCookie = await _factory.SeedSessionAsync(graph.Owner.Id, graph.Account.Id);
        return (graph.Account.Id, graph.Owner.Id, $"ophalo.sid={ownerCookie}");
    }

    private async Task<(Guid AccountUserId, Guid UserId)> SeedActiveMemberAsync(
        Guid accountId,
        string email,
        AccountUserRole role = AccountUserRole.Operator)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreateOwner(accountId, user.Id, user.Email, user.Email);
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();

        if (role != AccountUserRole.Owner)
        {
            await db.AccountUsers
                .Where(au => au.Id == member.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(au => au.Role, role));
        }

        return (member.Id, user.Id);
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
