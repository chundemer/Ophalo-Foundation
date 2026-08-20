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
/// HTTP integration tests for GAP-055's Owner/Admin Draft recorder-ownership transfer (Batch D):
///   POST /keep/pricebook/actual-work/{actualWorkId}/transfer-recorder
///
/// Covers the Owner/Admin-only role gate (not the ordinary three-gate + recorder-ownership check
/// every other Draft mutation uses), the required-reason/required-target validation, version
/// conflict, the Draft-only invariant, and the atomic RecorderAccountUserId change plus immutable
/// <c>ActualWorkDraftRecorderTransfer</c> audit record.
/// </summary>
public sealed class ActualWorkRecorderTransferApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkRecorderTransferApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Transfer_OwnerToOperator_Returns200AndRecordsAuditEvent()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("transfer-owner-to-operator");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var operatorId = await SeedOperatorAsync(accountId, "transfer-owner-to-operator");

        var response = await PostTransferAsync(ownerCookie, actualWorkId, version, operatorId, "Reassigning to the technician on site.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(version, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var work = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(operatorId, work.RecorderAccountUserId);
        Assert.Equal(ownerId, work.CreatedByUserId);
        Assert.Equal(newVersion, work.ConcurrencyVersion);

        var audit = await db.Set<ActualWorkDraftRecorderTransfer>().SingleAsync(x => x.ActualWorkId == actualWorkId);
        Assert.Equal(ownerId, audit.ActorAccountUserId);
        Assert.Equal(ownerId, audit.PriorRecorderAccountUserId);
        Assert.Equal(operatorId, audit.NewRecorderAccountUserId);
        Assert.Equal("Reassigning to the technician on site.", audit.Reason);
    }

    [Fact]
    public async Task Transfer_Admin_Returns200()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("transfer-admin");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var operatorId = await SeedOperatorAsync(accountId, "transfer-admin");
        var adminId = await SeedAdminAsync(accountId, "transfer-admin");
        var adminCookie = await GetCookieAsync(adminId, accountId);

        var response = await PostTransferAsync(adminCookie, actualWorkId, version, operatorId, "Admin reassignment.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_Operator_Returns403()
    {
        // Even the Draft's own current recorder cannot self-service a transfer — GAP-055 locks
        // this to Owner/Admin only.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("transfer-operator-forbidden");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var operatorId = await SeedOperatorAsync(accountId, "transfer-operator-forbidden");
        await SeedWatchingAsync(requestId, accountId, operatorId);
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var (actualWorkId, version) = await CreateDraftAsync(operatorCookie, requestId);
        var otherOperatorId = await SeedOperatorAsync(accountId, "transfer-operator-forbidden-2");

        var response = await PostTransferAsync(operatorCookie, actualWorkId, version, otherOperatorId, "Trying to self-transfer.");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_BlankReason_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("transfer-blank-reason");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var operatorId = await SeedOperatorAsync(accountId, "transfer-blank-reason");

        var response = await PostTransferAsync(ownerCookie, actualWorkId, version, operatorId, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.RecorderTransferReasonRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Transfer_EmptyTargetGuid_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("transfer-empty-target");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var response = await PostTransferAsync(ownerCookie, actualWorkId, version, Guid.Empty, "No target supplied.");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.RecorderTransferTargetRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Transfer_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("transfer-stale-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);
        var operatorId = await SeedOperatorAsync(accountId, "transfer-stale-version");

        var response = await PostTransferAsync(ownerCookie, actualWorkId, Guid.NewGuid(), operatorId, "Stale token.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_SubmittedVisit_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("transfer-submitted");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);
        var operatorId = await SeedOperatorAsync(accountId, "transfer-submitted");
        var submittedVersion = await SubmitAsync(actualWorkId, accountId);

        var response = await PostTransferAsync(ownerCookie, actualWorkId, submittedVersion, operatorId, "Too late.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.NotDraft", body.GetProperty("code").GetString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<HttpResponseMessage> PostTransferAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, Guid newRecorderAccountUserId, string reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/transfer-recorder")
        {
            Content = JsonContent.Create(new { newRecorderAccountUserId, reason })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
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

    private async Task SeedWatchingAsync(Guid requestId, Guid accountId, Guid accountUserId)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                requestId, accountId, accountUserId, ParticipationType.Watching, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
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

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Actual Work Transfer Test Co {slug}",
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

    private async Task<Guid> SeedAdminAsync(Guid accountId, string slug)
    {
        var now = DateTime.UtcNow;
        var email = $"admin@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), AccountUserRole.Admin,
            inviteTokenHash: $"{slug}_admin_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
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
