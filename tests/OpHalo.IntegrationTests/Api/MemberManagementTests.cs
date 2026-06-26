using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Phase 5E-C member management endpoints:
///   GET    /accounts/me/members
///   PATCH  /accounts/me/members/{id}/role
///   POST   /accounts/me/members/{id}/resend-invite
///   POST   /accounts/me/members/{id}/suspend
///   POST   /accounts/me/members/{id}/reactivate
///   DELETE /accounts/me/members/{id}
///
/// Test DB is reset once per class. Each test seeds what it needs in the method body.
/// </summary>
public sealed class MemberManagementTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    public MemberManagementTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.EmailSender.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Test 1 — List: anonymous → 401
    // =========================================================================

    [Fact]
    public async Task ListMembers_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/accounts/me/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — List: default excludes Removed, includes Active/Invited/Suspended
    // =========================================================================

    [Fact]
    public async Task ListMembers_Default_ExcludesRemoved_IncludesActiveInvitedSuspended()
    {
        var (accountId, ownerAccountUserId, ownerCookie) = await SeedAccountAsync();

        var (activeMemberId, _)    = await SeedActiveMemberAsync(accountId, "active@example.com");
        var invitedMemberId        = await SeedInvitedMemberAsync(accountId, "invited@example.com");
        var (suspendedMemberId, _) = await SeedSuspendedMemberAsync(accountId, "suspended@example.com");
        var (removedMemberId, _)   = await SeedRemovedMemberWithUserAsync(accountId, "removed@example.com");

        var response = await AuthRequest(ownerCookie).GetAsync("/accounts/me/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("accountUserId").GetGuid())
            .ToHashSet();

        Assert.Contains(ownerAccountUserId, ids);
        Assert.Contains(activeMemberId, ids);
        Assert.Contains(invitedMemberId, ids);
        Assert.Contains(suspendedMemberId, ids);
        Assert.DoesNotContain(removedMemberId, ids);
    }

    // =========================================================================
    // Test 3 — List: includeRemoved=true includes Removed
    // =========================================================================

    [Fact]
    public async Task ListMembers_IncludeRemoved_IncludesRemovedMembers()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (removedMemberId, _) = await SeedRemovedMemberWithUserAsync(accountId, "removed@example.com");

        var response = await AuthRequest(ownerCookie).GetAsync("/accounts/me/members?includeRemoved=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("accountUserId").GetGuid())
            .ToHashSet();

        Assert.Contains(removedMemberId, ids);
    }

    // =========================================================================
    // Test 4 — List: isCurrentUser marks exactly the caller's row
    // =========================================================================

    [Fact]
    public async Task ListMembers_IsCurrentUser_MarksCallerRow()
    {
        var (accountId, ownerAccountUserId, ownerCookie) = await SeedAccountAsync();
        var (memberId, _) = await SeedActiveMemberAsync(accountId, "member@example.com");

        var response = await AuthRequest(ownerCookie).GetAsync("/accounts/me/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var members = body.GetProperty("members").EnumerateArray().ToList();

        var ownerRow = members.Single(m => m.GetProperty("accountUserId").GetGuid() == ownerAccountUserId);
        Assert.True(ownerRow.GetProperty("isCurrentUser").GetBoolean());

        var memberRow = members.Single(m => m.GetProperty("accountUserId").GetGuid() == memberId);
        Assert.False(memberRow.GetProperty("isCurrentUser").GetBoolean());
    }

    // =========================================================================
    // Test 5 — Mutations: Viewer and Operator are forbidden
    // =========================================================================

    [Theory]
    [InlineData(AccountUserRole.Viewer)]
    [InlineData(AccountUserRole.Operator)]
    public async Task MemberMutation_ViewerOrOperator_Returns403(AccountUserRole callerRole)
    {
        var (accountId, _, _) = await SeedAccountAsync();
        var (targetId, _) = await SeedActiveMemberAsync(accountId, "target@example.com");
        var (callerId, _) = await SeedActiveMemberAsync(accountId, $"caller-{callerRole}@example.com", callerRole);
        var callerCookie = await GetCookieAsync(callerId, accountId);

        var response = await AuthRequest(callerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{targetId}/role",
            new { role = "viewer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertCode(response, "Member.Forbidden");
    }

    // =========================================================================
    // Test 6 — Admin can manage non-owner roles
    // =========================================================================

    [Fact]
    public async Task ChangeRole_Admin_CanManageNonOwner()
    {
        var (accountId, _, _) = await SeedAccountAsync();
        var (targetId, _) = await SeedActiveMemberAsync(accountId, "operator@example.com");
        var (adminId, _) = await SeedActiveMemberAsync(accountId, "admin@example.com", AccountUserRole.Admin);
        var adminCookie = await GetCookieAsync(adminId, accountId);

        var response = await AuthRequest(adminCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{targetId}/role",
            new { role = "viewer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.FindAsync(targetId);
        Assert.Equal(AccountUserRole.Viewer, target!.Role);
    }

    // =========================================================================
    // Test 7 — Admin cannot manage Owner-role members
    // =========================================================================

    [Fact]
    public async Task ChangeRole_Admin_CannotManageOwner_Returns422()
    {
        var (accountId, _, _) = await SeedAccountAsync();
        // Seed a second Owner (non-primary) so PrimaryOwnerProtected does not fire first.
        var (secondOwnerId, _) = await SeedActiveMemberAsync(accountId, "owner2@example.com", AccountUserRole.Owner);
        var (adminId, _) = await SeedActiveMemberAsync(accountId, "admin@example.com", AccountUserRole.Admin);
        var adminCookie = await GetCookieAsync(adminId, accountId);

        var response = await AuthRequest(adminCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{secondOwnerId}/role",
            new { role = "admin" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertCode(response, "Member.CannotModifyOwner");
    }

    // =========================================================================
    // Test 8 — Owner can promote to Owner when under the 2-owner cap
    // =========================================================================

    [Fact]
    public async Task ChangeRole_Owner_CanPromoteToOwner_WhenUnderCap()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (targetId, _) = await SeedActiveMemberAsync(accountId, "future-owner@example.com", AccountUserRole.Admin);

        var response = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{targetId}/role",
            new { role = "owner" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.FindAsync(targetId);
        Assert.Equal(AccountUserRole.Owner, target!.Role);
    }

    // =========================================================================
    // Test 9 — Owner promotion fails when cap of 2 is already reached
    // =========================================================================

    [Fact]
    public async Task ChangeRole_Owner_PromotionFailsAtOwnerCap_Returns409()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        // Promote one member to Owner so count reaches 2 (primary + second).
        var (secondOwnerId, _) = await SeedActiveMemberAsync(accountId, "owner2@example.com", AccountUserRole.Owner);
        // A third member to try promoting.
        var (thirdId, _) = await SeedActiveMemberAsync(accountId, "third@example.com", AccountUserRole.Admin);

        var response = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{thirdId}/role",
            new { role = "owner" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Member.OwnerLimitReached");
    }

    // =========================================================================
    // Test 10 — Primary owner cannot be demoted, suspended, or removed
    //           Caller must be a different Owner so CannotModifySelf does not fire first.
    // =========================================================================

    [Fact]
    public async Task PrimaryOwner_CannotBeDemotedSuspendedOrRemoved_Returns422()
    {
        var (accountId, primaryOwnerAccountUserId, _) = await SeedAccountAsync();

        // A second Owner acts as caller so CannotModifySelf does not fire first.
        var (secondOwnerId, _) = await SeedActiveMemberAsync(accountId, "owner2@example.com", AccountUserRole.Owner);
        Assert.NotEqual(primaryOwnerAccountUserId, secondOwnerId); // IDs must differ or CannotModifySelf fires instead
        var secondOwnerCookie = await GetCookieAsync(secondOwnerId, accountId);

        var demote = await AuthRequest(secondOwnerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{primaryOwnerAccountUserId}/role",
            new { role = "admin" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, demote.StatusCode);
        await AssertCode(demote, "Member.PrimaryOwnerProtected");

        var suspend = await AuthRequest(secondOwnerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{primaryOwnerAccountUserId}/suspend", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, suspend.StatusCode);
        await AssertCode(suspend, "Member.PrimaryOwnerProtected");

        var remove = await AuthRequest(secondOwnerCookie).DeleteAsync(
            $"/accounts/me/members/{primaryOwnerAccountUserId}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, remove.StatusCode);
        await AssertCode(remove, "Member.PrimaryOwnerProtected");
    }

    // =========================================================================
    // Test 11 — Last Active Owner cannot be demoted
    // =========================================================================

    [Fact]
    public async Task LastActiveOwner_CannotBeDemoted_Returns409()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        // Seed a second Active Owner (non-primary) and then suspend them via DB
        // so that ActiveOwnerCount drops to 1 (only the primary).
        var (secondOwnerId, _) = await SeedActiveMemberAsync(accountId, "owner2@example.com", AccountUserRole.Owner);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountUsers
                .Where(au => au.Id == secondOwnerId)
                .ExecuteUpdateAsync(s => s.SetProperty(au => au.MembershipStatus, MembershipStatus.Suspended));
        }

        // Demoting the Suspended Owner would leave 0 non-removed Active Owners if it succeeded,
        // which violates ADR-080. ActiveOwnerCount = 1 (only primary) → LastOwner fires.
        var response = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{secondOwnerId}/role",
            new { role = "admin" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Member.LastOwner");
    }

    // =========================================================================
    // Test 12 — Self-mutation is rejected for role change, suspend, and remove
    // =========================================================================

    [Fact]
    public async Task SelfMutation_Returns422()
    {
        var (accountId, ownerAccountUserId, ownerCookie) = await SeedAccountAsync();

        var roleChange = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{ownerAccountUserId}/role",
            new { role = "admin" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, roleChange.StatusCode);
        await AssertCode(roleChange, "Member.CannotModifySelf");

        var suspend = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{ownerAccountUserId}/suspend", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, suspend.StatusCode);
        await AssertCode(suspend, "Member.CannotModifySelf");

        var remove = await AuthRequest(ownerCookie).DeleteAsync(
            $"/accounts/me/members/{ownerAccountUserId}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, remove.StatusCode);
        await AssertCode(remove, "Member.CannotModifySelf");
    }

    // =========================================================================
    // Test 13 — Role can be changed on Invited members
    // =========================================================================

    [Fact]
    public async Task ChangeRole_InvitedMember_Succeeds()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var invitedId = await SeedInvitedMemberAsync(accountId, "pending@example.com", AccountUserRole.Operator);

        var response = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{invitedId}/role",
            new { role = "admin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.IgnoreQueryFilters().FirstOrDefaultAsync(au => au.Id == invitedId);
        Assert.Equal(AccountUserRole.Admin, target!.Role);
    }

    // =========================================================================
    // Test 14 — Role can be changed on Suspended members
    // =========================================================================

    [Fact]
    public async Task ChangeRole_SuspendedMember_Succeeds()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (suspendedId, _) = await SeedSuspendedMemberAsync(accountId, "suspended@example.com", AccountUserRole.Operator);

        var response = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{suspendedId}/role",
            new { role = "viewer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.FindAsync(suspendedId);
        Assert.Equal(AccountUserRole.Viewer, target!.Role);
    }

    // =========================================================================
    // Test 15 — Removed members cannot have role changed
    // =========================================================================

    [Fact]
    public async Task ChangeRole_RemovedMember_Returns422()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (removedId, _) = await SeedRemovedMemberWithUserAsync(accountId, "removed@example.com");

        var response = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{removedId}/role",
            new { role = "admin" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertCode(response, "Member.InvalidStatusTransition");
    }

    // =========================================================================
    // Test 16 — Suspend Active member → Suspended; existing session revoked
    // =========================================================================

    [Fact]
    public async Task Suspend_ActiveMember_SuspendsAndRevokesSession()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (memberId, _) = await SeedActiveMemberAsync(accountId, "member@example.com");
        var memberCookie = await GetCookieAsync(memberId, accountId);

        // Member can authenticate before suspension.
        var beforeSuspend = await AuthRequest(memberCookie).GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, beforeSuspend.StatusCode);

        var suspendResponse = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{memberId}/suspend", new { });
        Assert.Equal(HttpStatusCode.OK, suspendResponse.StatusCode);

        // DB state: now Suspended.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.FindAsync(memberId);
        Assert.Equal(MembershipStatus.Suspended, target!.MembershipStatus);

        // Session revoked — member's cookie is no longer valid.
        var afterSuspend = await AuthRequest(memberCookie).GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterSuspend.StatusCode);
    }

    // =========================================================================
    // Test 17 — Reactivate Suspended → Active (no seat-limit check)
    // =========================================================================

    [Fact]
    public async Task Reactivate_SuspendedMember_ReturnsActive()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (suspendedId, _) = await SeedSuspendedMemberAsync(accountId, "suspended@example.com");

        // Force seat limit to 1 so any seat check would fail — reactivate-from-Suspended
        // must bypass it because the seat is already occupied.
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountEntitlements
                .Where(e => e.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.MaxUserSeats, 1));
        }

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{suspendedId}/reactivate", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope2 = _factory.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db2.AccountUsers.FindAsync(suspendedId);
        Assert.Equal(MembershipStatus.Active, target!.MembershipStatus);
    }

    // =========================================================================
    // Test 18a — Reactivate Removed-with-UserId succeeds when seat available
    // =========================================================================

    [Fact]
    public async Task Reactivate_RemovedWithUserId_Succeeds_WhenSeatAvailable()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (removedId, _) = await SeedRemovedMemberWithUserAsync(accountId, "removed@example.com");

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{removedId}/reactivate", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.FindAsync(removedId);
        Assert.Equal(MembershipStatus.Active, target!.MembershipStatus);
    }

    // =========================================================================
    // Test 18b — Reactivate Removed-with-UserId fails at seat limit
    // =========================================================================

    [Fact]
    public async Task Reactivate_RemovedWithUserId_FailsAtSeatLimit_Returns409()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (removedId, _) = await SeedRemovedMemberWithUserAsync(accountId, "removed@example.com");

        // Seat limit = 1; owner occupies the only seat; Removed does not occupy a seat.
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountEntitlements
                .Where(e => e.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.MaxUserSeats, 1));
        }

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{removedId}/reactivate", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Member.SeatLimitReached");
    }

    // =========================================================================
    // Test 19 — Reactivate Removed-without-UserId → 422 (use resend-invite)
    // =========================================================================

    [Fact]
    public async Task Reactivate_RemovedWithoutUserId_Returns422()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var removedInviteId = await SeedRemovedInviteAsync(accountId, "never-accepted@example.com");

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{removedInviteId}/reactivate", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertCode(response, "Member.InvalidStatusTransition");
    }

    // =========================================================================
    // Test 20 — Remove Invited member clears token; old token becomes invalid
    // =========================================================================

    [Fact]
    public async Task Remove_InvitedMember_ClearsTokenAndOldTokenInvalid()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();

        // Create an invite with a known token so we can test acceptance after removal.
        var rawToken = InviteTokenGenerator.GenerateRawToken();
        var tokenHash = InviteTokenGenerator.HashToken(rawToken);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var invite = AccountUser.CreatePendingInvite(
                accountId, "pending@example.com", "pending@example.com",
                AccountUserRole.Operator, tokenHash, DateTime.UtcNow.AddDays(7), DateTime.UtcNow);
            db.AccountUsers.Add(invite);
            await db.SaveChangesAsync();
        }

        // Get the accountUserId for the invite row.
        Guid inviteId;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var invite = await db.AccountUsers
                .IgnoreQueryFilters()
                .FirstAsync(au => au.AccountId == accountId && au.InviteTokenHash == tokenHash);
            inviteId = invite.Id;
        }

        var removeResponse = await AuthRequest(ownerCookie).DeleteAsync(
            $"/accounts/me/members/{inviteId}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        // DB: status Removed, token cleared.
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var target = await db.AccountUsers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(au => au.Id == inviteId);
            Assert.Equal(MembershipStatus.Removed, target!.MembershipStatus);
            Assert.Null(target.InviteTokenHash);
            Assert.Null(target.InviteExpiresAtUtc);
        }

        // Old token is now invalid.
        var acceptResponse = await _client.PostAsJsonAsync("/accounts/invite/accept", new { token = rawToken });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, acceptResponse.StatusCode);
        await AssertCode(acceptResponse, "Invite.InvalidToken");
    }

    // =========================================================================
    // Test 21 — Remove Active/Suspended member → Removed + session revoked
    // =========================================================================

    [Fact]
    public async Task Remove_ActiveMember_SetsRemovedAndRevokesSession()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (memberId, _) = await SeedActiveMemberAsync(accountId, "member@example.com");
        var memberCookie = await GetCookieAsync(memberId, accountId);

        var beforeRemove = await AuthRequest(memberCookie).GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, beforeRemove.StatusCode);

        var removeResponse = await AuthRequest(ownerCookie).DeleteAsync(
            $"/accounts/me/members/{memberId}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.FindAsync(memberId);
        Assert.Equal(MembershipStatus.Removed, target!.MembershipStatus);

        var afterRemove = await AuthRequest(memberCookie).GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRemove.StatusCode);
    }

    [Fact]
    public async Task Remove_SuspendedMember_SetsRemovedAndRevokesSession()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (suspendedId, _) = await SeedSuspendedMemberAsync(accountId, "suspended@example.com");
        var suspendedCookie = await GetCookieAsync(suspendedId, accountId);

        // Suspended member's session is already invalid because the auth handler requires Active.
        // Seed a fresh session so we can verify it gets revoked on removal.
        // (The session itself still exists in the DB; revocation deletes it.)

        var removeResponse = await AuthRequest(ownerCookie).DeleteAsync(
            $"/accounts/me/members/{suspendedId}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.FindAsync(suspendedId);
        Assert.Equal(MembershipStatus.Removed, target!.MembershipStatus);

        // Session row revoked.
        var sessions = await db.AccountSessions
            .Where(s => s.AccountUserId == suspendedId && s.RevokedAtUtc == null)
            .ToListAsync();
        Assert.Empty(sessions);
    }

    // =========================================================================
    // Test 22 — Resend Invited: rotates token, sends email
    // =========================================================================

    [Fact]
    public async Task ResendInvite_InvitedMember_RotatesTokenAndSendsEmail()
    {
        const string inviteeEmail = "pending@example.com";
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        _factory.EmailSender.Clear();

        // Seed first invite via the send-invite endpoint so we have a known first token.
        await AuthRequest(ownerCookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });
        var firstToken = _factory.EmailSender.SentEmails.Single(e => e.To == inviteeEmail).ExtractInviteToken()!;
        _factory.EmailSender.Clear();

        // Get the invite's accountUserId.
        Guid inviteId;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var invite = await db.AccountUsers
                .IgnoreQueryFilters()
                .FirstAsync(au => au.AccountId == accountId && au.NormalizedEmail == inviteeEmail);
            inviteId = invite.Id;
        }

        // Resend via member management.
        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{inviteId}/resend-invite",
            new { delivery = "email" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // A new email was sent with a different token.
        var secondEmail = _factory.EmailSender.SentEmails.SingleOrDefault(e => e.To == inviteeEmail);
        Assert.NotNull(secondEmail);
        var secondToken = secondEmail!.ExtractInviteToken()!;
        Assert.NotEqual(firstToken, secondToken);

        // Old token is now invalid.
        var oldAccept = await _client.PostAsJsonAsync("/accounts/invite/accept", new { token = firstToken });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oldAccept.StatusCode);
    }

    // =========================================================================
    // Test 23a — Resend Removed-no-UserId: restores to Invited, sends email
    // =========================================================================

    [Fact]
    public async Task ResendInvite_RemovedNoUserId_RestoresToInvited_SendsEmail()
    {
        const string email = "removed-invite@example.com";
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var removedInviteId = await SeedRemovedInviteAsync(accountId, email);
        _factory.EmailSender.Clear();

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{removedInviteId}/resend-invite",
            new { delivery = "email" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Status restored to Invited.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var target = await db.AccountUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(au => au.Id == removedInviteId);
        Assert.Equal(MembershipStatus.Invited, target!.MembershipStatus);
        Assert.NotNull(target.InviteTokenHash);

        // Email sent.
        Assert.Single(_factory.EmailSender.SentEmails, e => e.To == email);
    }

    // =========================================================================
    // Test 23b — Resend Removed-no-UserId: blocked at seat limit
    // =========================================================================

    [Fact]
    public async Task ResendInvite_RemovedNoUserId_FailsAtSeatLimit_Returns409()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var removedInviteId = await SeedRemovedInviteAsync(accountId, "removed-invite@example.com");

        // Seat limit = 1; owner occupies the only seat.
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountEntitlements
                .Where(e => e.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.MaxUserSeats, 1));
        }

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{removedInviteId}/resend-invite",
            new { delivery = "email" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Member.SeatLimitReached");
    }

    // =========================================================================
    // Test 24 — Resend Removed-with-UserId → 422 (use reactivate instead)
    // =========================================================================

    [Fact]
    public async Task ResendInvite_RemovedWithUserId_Returns422()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        var (removedId, _) = await SeedRemovedMemberWithUserAsync(accountId, "removed@example.com");

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/accounts/me/members/{removedId}/resend-invite",
            new { delivery = "email" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertCode(response, "Member.InvalidStatusTransition");
    }

    // =========================================================================
    // Test 25a — New invite to Removed email (with UserId) → 409 code=Member.PreviouslyRemoved
    //            suggestedAction=reactivate. Public code, not internal routing code.
    // =========================================================================

    [Fact]
    public async Task SendInvite_ToRemovedEmailWithUserId_Returns409_WithReactivateSuggestedAction()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        await SeedRemovedMemberWithUserAsync(accountId, "removed@example.com");

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "removed@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Must be the PUBLIC code, not the internal routing code.
        Assert.Equal("Member.PreviouslyRemoved", body.GetProperty("code").GetString());
        Assert.Equal("reactivate", body.GetProperty("suggestedAction").GetString());
    }

    // =========================================================================
    // Test 25b — New invite to Removed email (no UserId) → 409 code=Member.PreviouslyRemoved
    //            suggestedAction=resend_invite. Public code, not internal routing code.
    // =========================================================================

    [Fact]
    public async Task SendInvite_ToRemovedEmailWithoutUserId_Returns409_WithResendInviteSuggestedAction()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        await SeedRemovedInviteAsync(accountId, "removed-invite@example.com");

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "removed-invite@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Must be the PUBLIC code, not the internal routing code.
        Assert.Equal("Member.PreviouslyRemoved", body.GetProperty("code").GetString());
        Assert.Equal("resend_invite", body.GetProperty("suggestedAction").GetString());
    }

    // =========================================================================
    // Test 26 — Role change does not revoke session; new role observed immediately
    // =========================================================================

    [Fact]
    public async Task RoleChange_DoesNotRevokeSession_NewRoleObservedOnSubsequentRequest()
    {
        var (accountId, _, ownerCookie) = await SeedAccountAsync();
        // Seed an Operator who cannot manage members.
        var (operatorId, _) = await SeedActiveMemberAsync(accountId, "operator@example.com", AccountUserRole.Operator);
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        // Seed a target for the operator to try to manage (after role change to Admin).
        var (targetId, _) = await SeedActiveMemberAsync(accountId, "target@example.com");

        // As Operator, mutation attempt is forbidden.
        var beforeChange = await AuthRequest(operatorCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{targetId}/role", new { role = "viewer" });
        Assert.Equal(HttpStatusCode.Forbidden, beforeChange.StatusCode);

        // Owner promotes the Operator to Admin.
        var promote = await AuthRequest(ownerCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{operatorId}/role", new { role = "admin" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        // Operator's original session cookie is still valid (not revoked by role change).
        var me = await AuthRequest(operatorCookie).GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // New role is observed on the next request — now Admin, can manage members.
        var afterChange = await AuthRequest(operatorCookie).PatchAsJsonAsync(
            $"/accounts/me/members/{targetId}/role", new { role = "viewer" });
        Assert.Equal(HttpStatusCode.OK, afterChange.StatusCode);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Seeds a Trial account with an Active primary owner.
    /// Returns (AccountId, OwnerAccountUserId, OwnerCookie).
    /// </summary>
    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(
        string ownerEmail = "owner@member-mgmt-tests.com")
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: ownerEmail,
            name: "Owner",
            businessName: "Member Mgmt Test Co",
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

    /// <summary>
    /// Seeds an Active member in an existing account with the given role.
    /// Uses CreateOwner (creates Active + UserId) then overrides the role via ExecuteUpdateAsync.
    /// </summary>
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

    /// <summary>
    /// Seeds an Active member then suspends them via the domain method + SaveChanges.
    /// Bypasses the service layer to set up test state directly.
    /// </summary>
    private async Task<(Guid AccountUserId, Guid UserId)> SeedSuspendedMemberAsync(
        Guid accountId,
        string email,
        AccountUserRole role = AccountUserRole.Operator)
    {
        var (memberId, userId) = await SeedActiveMemberAsync(accountId, email, role);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var member = await db.AccountUsers.FindAsync(memberId);
        member!.Suspend();
        await db.SaveChangesAsync();

        return (memberId, userId);
    }

    /// <summary>
    /// Seeds a pending invite (Invited status, no UserId, 7-day expiry) in an existing account.
    /// </summary>
    private async Task<Guid> SeedInvitedMemberAsync(
        Guid accountId,
        string email,
        AccountUserRole role = AccountUserRole.Operator)
    {
        var now = DateTime.UtcNow;
        var tokenHash = InviteTokenGenerator.HashToken(InviteTokenGenerator.GenerateRawToken());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var invite = AccountUser.CreatePendingInvite(
            accountId, email, email.ToLowerInvariant(), role,
            tokenHash, now.AddDays(7), now);
        db.AccountUsers.Add(invite);
        await db.SaveChangesAsync();

        return invite.Id;
    }

    /// <summary>
    /// Seeds an Active member then removes them via Remove() + SaveChanges.
    /// The resulting row has UserId set (removed active member).
    /// </summary>
    private async Task<(Guid AccountUserId, Guid UserId)> SeedRemovedMemberWithUserAsync(
        Guid accountId,
        string email)
    {
        var (memberId, userId) = await SeedActiveMemberAsync(accountId, email);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var member = await db.AccountUsers.FindAsync(memberId);
        member!.Remove();
        await db.SaveChangesAsync();

        return (memberId, userId);
    }

    /// <summary>
    /// Seeds a pending invite then removes it via Remove() + SaveChanges.
    /// The resulting row has no UserId (removed invite — never accepted).
    /// </summary>
    private async Task<Guid> SeedRemovedInviteAsync(Guid accountId, string email)
    {
        var inviteId = await SeedInvitedMemberAsync(accountId, email);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var invite = await db.AccountUsers.IgnoreQueryFilters().FirstAsync(au => au.Id == inviteId);
        invite.Remove();
        await db.SaveChangesAsync();

        return inviteId;
    }

    /// <summary>Seeds a real session for the given member and returns the cookie string.</summary>
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

    private static async Task AssertCode(HttpResponseMessage response, string expectedCode)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.TryGetProperty("code", out var c) ? c.GetString() : null;
        Assert.Equal(expectedCode, code);
    }
}
