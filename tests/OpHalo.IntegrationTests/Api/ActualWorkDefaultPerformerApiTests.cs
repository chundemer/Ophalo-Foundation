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
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for ADR-494 D2 (4c-i-b-2), the recorder-only Draft ticket-default
/// performer gate:
///   PUT /keep/pricebook/actual-work/{actualWorkId}/default-performer
///
/// Covers set / replace / clear, the shared recorder-ownership row authorization and
/// <c>X-Keep-ActualWork-Version</c> optimistic-concurrency protocol, server-side revalidation of a
/// supplied target (inactive / cross-account / empty guid all collapse to 422 with no version
/// rotation), and the frozen-history invariant: changing the default never rewrites the performer
/// already recorded on an existing line, but it does change the default future lines inherit.
/// </summary>
public sealed class ActualWorkDefaultPerformerApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkDefaultPerformerApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetDefault_FromNone_PersistsAndRotatesVersion()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("set-from-none");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "set-from-none");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId, defaultPerformedByAccountUserId: null);

        var response = await PutDefaultPerformerAsync(ownerCookie, actualWorkId, version, operatorId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newVersion = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(version, newVersion);
        Assert.Equal(operatorId, await GetDefaultPerformerAsync(actualWorkId));
    }

    [Fact]
    public async Task SetDefault_ReplaceExisting_PersistsNewTarget()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("set-replace");
        await EnrollAsync(accountId, ownerId);
        var operatorA = await SeedOperatorAsync(accountId, "set-replace-a");
        var operatorB = await SeedOperatorAsync(accountId, "set-replace-b");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId, operatorA);

        var response = await PutDefaultPerformerAsync(ownerCookie, actualWorkId, version, operatorB);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(operatorB, await GetDefaultPerformerAsync(actualWorkId));
    }

    [Fact]
    public async Task ClearDefault_PersistsNull_AndNextLineWithoutPerformerReturnsPerformerRequired()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("clear-default");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "clear-default");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId, operatorId);

        var clearResponse = await PutDefaultPerformerAsync(ownerCookie, actualWorkId, version, performedByAccountUserId: null);

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        Assert.Null(await GetDefaultPerformerAsync(actualWorkId));

        var clearedVersion = (await clearResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();
        var addLine = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Gasket", actualQuantity = 1m, note = (string?)null
            })
        };
        addLine.Headers.Add("X-Keep-ActualWork-Version", clearedVersion.ToString("D"));
        var addLineResponse = await AuthRequest(ownerCookie).SendAsync(addLine);

        Assert.Equal(HttpStatusCode.BadRequest, addLineResponse.StatusCode);
        var body = await addLineResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.PerformerRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetDefault_CallerNotTheRecorder_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("set-not-recorder");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "set-not-recorder");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId, defaultPerformedByAccountUserId: null);

        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var response = await PutDefaultPerformerAsync(operatorCookie, actualWorkId, version, operatorId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(version, await GetVersionAsync(actualWorkId));
    }

    [Fact]
    public async Task SetDefault_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("set-stale");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "set-stale");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId, defaultPerformedByAccountUserId: null);

        var response = await PutDefaultPerformerAsync(ownerCookie, actualWorkId, Guid.NewGuid(), operatorId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await GetDefaultPerformerAsync(actualWorkId));
    }

    [Theory]
    [InlineData("empty-guid")]
    [InlineData("cross-account")]
    [InlineData("inactive")]
    public async Task SetDefault_IneligibleTarget_Returns422_DefaultAndVersionUnchanged(string kind)
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync($"set-bad-{kind}");
        await EnrollAsync(accountId, ownerId);
        var seededDefault = await SeedOperatorAsync(accountId, $"set-bad-{kind}-seed");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId, seededDefault);

        var badId = kind switch
        {
            "empty-guid" => Guid.Empty,
            "cross-account" => (await SeedAccountAsync($"other-set-bad-{kind}")).OwnerAccountUserId,
            "inactive" => await SeedInactiveOperatorAsync(accountId, $"set-bad-{kind}"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var response = await PutDefaultPerformerAsync(ownerCookie, actualWorkId, version, badId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(seededDefault, await GetDefaultPerformerAsync(actualWorkId));
        Assert.Equal(version, await GetVersionAsync(actualWorkId));
    }

    [Fact]
    public async Task SetDefault_DoesNotRewriteThePerformerAlreadyRecordedOnAnExistingLine()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("set-keeps-history");
        await EnrollAsync(accountId, ownerId);
        var operatorA = await SeedOperatorAsync(accountId, "set-keeps-history-a");
        var operatorB = await SeedOperatorAsync(accountId, "set-keeps-history-b");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId, operatorA);

        var addLine = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Coil clean", actualQuantity = 1m, note = (string?)null
            })
        };
        addLine.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var addLineResponse = await AuthRequest(ownerCookie).SendAsync(addLine);
        Assert.Equal(HttpStatusCode.OK, addLineResponse.StatusCode);
        var versionAfterLine = (await addLineResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("actualWorkConcurrencyVersion").GetGuid();

        var response = await PutDefaultPerformerAsync(ownerCookie, actualWorkId, versionAfterLine, operatorB);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<ActualWorkLine>().SingleAsync(x => x.ActualWorkId == actualWorkId);
        Assert.Equal(operatorA, line.PerformedByAccountUserId);
        var work = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(operatorB, work.DefaultPerformedByAccountUserId);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<HttpResponseMessage> PutDefaultPerformerAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, Guid? performedByAccountUserId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, $"/keep/pricebook/actual-work/{actualWorkId}/default-performer")
        {
            Content = JsonContent.Create(new { performedByAccountUserId }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    private async Task<Guid?> GetDefaultPerformerAsync(Guid actualWorkId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        return await db.Set<ActualWork>().Where(x => x.Id == actualWorkId)
            .Select(x => x.DefaultPerformedByAccountUserId).SingleAsync();
    }

    private async Task<Guid> GetVersionAsync(Guid actualWorkId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        return await db.Set<ActualWork>().Where(x => x.Id == actualWorkId)
            .Select(x => x.ConcurrencyVersion).SingleAsync();
    }

    private async Task<(Guid ActualWorkId, Guid ConcurrencyVersion)> CreateDraftAsync(
        string cookie, Guid requestId, Guid? defaultPerformedByAccountUserId)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create",
            new { requestId, defaultPerformedByAccountUserId });
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

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Actual Work Default Performer Test Co {slug}",
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

    /// <summary>An Operator whose membership is no longer active — the role would qualify, the
    /// status must not.</summary>
    private async Task<Guid> SeedInactiveOperatorAsync(Guid accountId, string slug)
    {
        var operatorId = await SeedOperatorAsync(accountId, $"{slug}-inactive");
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var member = await db.AccountUsers.SingleAsync(x => x.AccountId == accountId && x.Id == operatorId);
        Assert.True(member.Suspend().IsSuccess);
        await db.SaveChangesAsync();
        return operatorId;
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
