using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for 1a-ii's Owner/Admin-only recorder-transfer candidate list:
///   GET /keep/pricebook/actual-work/recorder-candidates
///
/// Covers the Owner/Admin-only role gate, the entitlement gate, and the eligibility filter — only
/// active members holding the GAP-055 recorder predicate (<c>RequestsOperate</c> +
/// <c>ActualWorkCapture</c>) appear, so a Viewer and a still-pending Operator are excluded and no
/// membership enumeration leaks to a non-qualified caller.
/// </summary>
public sealed class ActualWorkRecorderCandidatesApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkRecorderCandidatesApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCandidates_Owner_ReturnsOnlyEligibleActiveMembers()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("candidates-owner");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedMemberAsync(accountId, "candidates-owner", AccountUserRole.Operator);
        var adminId = await SeedMemberAsync(accountId, "candidates-owner", AccountUserRole.Admin);
        await SeedMemberAsync(accountId, "candidates-owner", AccountUserRole.Viewer);
        await SeedPendingOperatorAsync(accountId, "candidates-owner");

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/actual-work/recorder-candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("accountUserId").GetGuid())
            .ToHashSet();

        Assert.Equal(new[] { ownerId, adminId, operatorId }.ToHashSet(), ids);
        var operatorEntry = body.GetProperty("candidates").EnumerateArray()
            .Single(c => c.GetProperty("accountUserId").GetGuid() == operatorId);
        Assert.Equal("operator", operatorEntry.GetProperty("role").GetString());
        Assert.Equal("operator@candidates-owner.com", operatorEntry.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task GetCandidates_Admin_Returns200()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("candidates-admin");
        await EnrollAsync(accountId, ownerId);
        var adminId = await SeedMemberAsync(accountId, "candidates-admin", AccountUserRole.Admin);
        var adminCookie = await GetCookieAsync(adminId, accountId);

        var response = await AuthRequest(adminCookie).GetAsync("/keep/pricebook/actual-work/recorder-candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_Operator_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("candidates-operator");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedMemberAsync(accountId, "candidates-operator", AccountUserRole.Operator);
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/actual-work/recorder-candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_Viewer_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("candidates-viewer");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedMemberAsync(accountId, "candidates-viewer", AccountUserRole.Viewer);
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await AuthRequest(viewerCookie).GetAsync("/keep/pricebook/actual-work/recorder-candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/keep/pricebook/actual-work/recorder-candidates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_WithoutEntitlement_Returns403()
    {
        var (_, _, ownerCookie) = await SeedAccountAsync("candidates-no-entitlement");

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/actual-work/recorder-candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Recorder Candidates Test Co {slug}",
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

    private async Task<Guid> SeedMemberAsync(Guid accountId, string slug, AccountUserRole role)
    {
        var now = DateTime.UtcNow;
        var label = role.ToString().ToLowerInvariant();
        var email = $"{label}@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), role,
            inviteTokenHash: $"{slug}_{label}_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        member.Activate(user.Id, now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>An Operator-role member with a still-pending invite — the role would qualify, but
    /// the non-active membership status must exclude them.</summary>
    private async Task SeedPendingOperatorAsync(Guid accountId, string slug)
    {
        var now = DateTime.UtcNow;
        var email = $"pending-operator@{slug}.com";
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), AccountUserRole.Operator,
            inviteTokenHash: $"{slug}_pending_operator_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();
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
