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
/// HTTP integration tests for ADR-494 D2's performer-candidate list:
///   GET /keep/pricebook/actual-work/performer-candidates
///
/// Unlike the Owner/Admin-only recorder-candidate list, this is callable by any active member
/// holding the performer predicate (<c>RequestsOperate</c> + <c>ActualWorkCapture</c>) so an
/// Operator office transcriber can pick a technician. The list itself is the same predicate:
/// active Owner/Admin/Operator members only — Viewers and non-active invites never appear, and no
/// membership enumeration leaks to a non-qualified caller.
/// </summary>
public sealed class ActualWorkPerformerCandidatesApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkPerformerCandidatesApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCandidates_OperatorCaller_Returns200WithOnlyActiveEligibleMembers()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("perf-operator");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedMemberAsync(accountId, "perf-operator", AccountUserRole.Operator);
        var adminId = await SeedMemberAsync(accountId, "perf-operator", AccountUserRole.Admin);
        await SeedMemberAsync(accountId, "perf-operator", AccountUserRole.Viewer);
        await SeedSuspendedMemberAsync(accountId, "perf-operator", AccountUserRole.Operator);
        await SeedPendingMemberAsync(accountId, "perf-operator", AccountUserRole.Operator);
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/actual-work/performer-candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("accountUserId").GetGuid())
            .ToHashSet();

        Assert.Equal(new[] { ownerId, adminId, operatorId }.ToHashSet(), ids);
    }

    [Fact]
    public async Task GetCandidates_OwnerCaller_Returns200()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("perf-owner");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/actual-work/performer-candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_ViewerCaller_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("perf-viewer");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedMemberAsync(accountId, "perf-viewer", AccountUserRole.Viewer);
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await AuthRequest(viewerCookie).GetAsync("/keep/pricebook/actual-work/performer-candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/keep/pricebook/actual-work/performer-candidates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_WithoutEntitlement_Returns403()
    {
        var (_, _, ownerCookie) = await SeedAccountAsync("perf-no-entitlement");

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/actual-work/performer-candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_BlockedAccount_Returns403()
    {
        // Gate 1 — account access. An expired trial is Blocked (AccountAccessPolicy.Evaluate); the
        // candidate read denies exactly like GetActualWorkRecorderCandidatesService.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("perf-blocked", trialEndsInDays: -1);
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/actual-work/performer-candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(
        string slug, int trialEndsInDays = 30)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Performer Candidates Test Co {slug}",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(trialEndsInDays));

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

    /// <summary>An active-then-suspended member whose role would otherwise qualify — a now-inactive
    /// former technician must not appear in the pick list.</summary>
    private async Task SeedSuspendedMemberAsync(Guid accountId, string slug, AccountUserRole role)
    {
        var now = DateTime.UtcNow;
        var email = $"suspended-{role.ToString().ToLowerInvariant()}@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), role,
            inviteTokenHash: $"{slug}_suspended_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        member.Activate(user.Id, now);
        Assert.True(member.Suspend().IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();
    }

    /// <summary>A still-pending invite — the role would qualify, but the non-active status excludes them.</summary>
    private async Task SeedPendingMemberAsync(Guid accountId, string slug, AccountUserRole role)
    {
        var now = DateTime.UtcNow;
        var email = $"pending-{role.ToString().ToLowerInvariant()}@{slug}.com";
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), role,
            inviteTokenHash: $"{slug}_pending_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);

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
