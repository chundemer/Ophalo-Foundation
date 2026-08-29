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
/// HTTP integration tests for Batch 6's Owner/Admin Actual Work review mutation:
///   POST /keep/pricebook/actual-work/{actualWorkId}/review
///
/// Covers the Owner/Admin-only gate (RequestsOperate + Price Book entitlement + role check, no
/// ActualWorkCapture — build-log/129, "6 preflight — locked decisions"), not-found, version
/// conflict, the Submitted-only invariant, and single-shot review. Mirrors
/// <see cref="ActualWorkRecorderTransferApiTests"/>'s fixture shape.
/// </summary>
public sealed class ActualWorkReviewApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkReviewApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Review_OwnerOnSubmittedVisit_Returns200AndSetsReviewFields()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-owner");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId, draftVersion);

        var response = await PostReviewAsync(ownerCookie, actualWorkId, submittedVersion, "Looks good.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(submittedVersion, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var work = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.NotNull(work.ReviewedAtUtc);
        Assert.Equal(ownerId, work.ReviewedByAccountUserId);
        Assert.Equal("Looks good.", work.ReviewNote);
    }

    [Fact]
    public async Task Review_Admin_Returns200()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-admin");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId, draftVersion);
        var adminId = await SeedAdminAsync(accountId, "review-admin");
        var adminCookie = await GetCookieAsync(adminId, accountId);

        var response = await PostReviewAsync(adminCookie, actualWorkId, submittedVersion, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Review_Operator_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-operator-forbidden");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId, draftVersion);
        var operatorId = await SeedOperatorAsync(accountId, "review-operator-forbidden");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await PostReviewAsync(operatorCookie, actualWorkId, submittedVersion, "Trying to review my own visit.");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Review_Viewer_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-viewer-forbidden");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId, draftVersion);
        var viewerId = await SeedViewerAsync(accountId, "review-viewer-forbidden");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await PostReviewAsync(viewerCookie, actualWorkId, submittedVersion, "Trying to review as a viewer.");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Review_WithoutPriceBookEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-no-entitlement");
        // Deliberately no EnrollAsync — the account lacks the Price Book entitlement.
        var requestId = await SeedRequestAsync(accountId);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var visit = ActualWork.Create(accountId, requestId, ownerId).Value;
        db.Set<ActualWork>().Add(visit);
        await db.SaveChangesAsync();

        var response = await PostReviewAsync(ownerCookie, visit.Id, visit.ConcurrencyVersion, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Review_UnknownVisit_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-unknown");
        await EnrollAsync(accountId, ownerId);

        var response = await PostReviewAsync(ownerCookie, Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Review_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-stale-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        await SubmitAsync(actualWorkId, accountId, draftVersion);

        var response = await PostReviewAsync(ownerCookie, actualWorkId, Guid.NewGuid(), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Review_DraftVisit_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-draft");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);

        var response = await PostReviewAsync(ownerCookie, actualWorkId, draftVersion, null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.NotSubmitted", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Review_Twice_Returns409AndDoesNotOverwriteTheFirstReview()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-twice");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId, draftVersion);
        var firstResponse = await PostReviewAsync(ownerCookie, actualWorkId, submittedVersion, "First review.");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var reviewedVersion = firstBody.GetProperty("concurrencyVersion").GetGuid();

        var response = await PostReviewAsync(ownerCookie, actualWorkId, reviewedVersion, "Second review.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.AlreadyReviewed", body.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var work = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal("First review.", work.ReviewNote);
    }

    [Fact]
    public async Task Review_VisitWithIncompleteLineFinancials_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-incomplete");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        var linedVersion = await AddIncompleteCustomLineAsync(actualWorkId, accountId, ownerId);
        var submittedVersion = await SubmitOnlyAsync(actualWorkId, accountId, linedVersion);

        var response = await PostReviewAsync(ownerCookie, actualWorkId, submittedVersion, null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.ReviewBlockedIncompleteFinancials", body.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var work = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Null(work.ReviewedAtUtc);
    }

    [Fact]
    public async Task Review_ZeroLineVisitWithoutDisposition_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("review-nodisp");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var (actualWorkId, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitOnlyAsync(actualWorkId, accountId, draftVersion);

        var response = await PostReviewAsync(ownerCookie, actualWorkId, submittedVersion, null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.ReviewBlockedZeroLineDispositionRequired", body.GetProperty("code").GetString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<HttpResponseMessage> PostReviewAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, string? reviewNote)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/review")
        {
            Content = JsonContent.Create(new { reviewNote })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    /// <summary>Submits the (zero-line) visit and, unless it carries lines, records a no-charge
    /// office financial disposition so it clears the BL135 §4 Batch 3b-ii hard review gate —
    /// every visit seeded here is a zero-line NoWorkAuthorized visit heading toward review.
    /// <see cref="SubmitOnlyAsync"/> is the variant that deliberately leaves the gate unsatisfied.</summary>
    private async Task<Guid> SubmitAsync(Guid actualWorkId, Guid accountId, Guid expectedVersion)
    {
        var version = await SubmitOnlyAsync(actualWorkId, accountId, expectedVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var hasLines = await db.Set<ActualWork>().Where(x => x.Id == actualWorkId).SelectMany(x => x.Lines).AnyAsync();
        if (!hasLines)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO keep_actual_work_office_financial_dispositions
                  (id, created_at_utc, updated_at_utc, account_id, actual_work_id, kind, reason,
                   disposed_by_account_user_id, disposed_at_utc)
                VALUES
                  ({Guid.NewGuid()}, {DateTime.UtcNow}, {DateTime.UtcNow}, {accountId}, {actualWorkId},
                   {nameof(OfficeFinancialDispositionKind.NoCharge)}, {"no billable work"},
                   {Guid.NewGuid()}, {DateTime.UtcNow})");
        }

        return version;
    }

    private async Task<Guid> SubmitOnlyAsync(Guid actualWorkId, Guid accountId, Guid expectedVersion)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistence = new EfActualWorkSubmissionPersistence(db);
        var outcome = await persistence.SubmitAsync(
            accountId, actualWorkId, expectedVersion, ActualWorkOutcome.NoWorkAuthorized, "No work performed.",
            DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(ActualWorkSubmissionResult.Committed, outcome.Result);
        return outcome.ConcurrencyVersion!.Value;
    }

    /// <summary>Adds one custom (off-catalog, no-snapshot) line to a Draft visit through the domain
    /// aggregate, returning the rotated concurrency version. Financially incomplete by construction,
    /// so a review is blocked until the line is resolved.</summary>
    private async Task<Guid> AddIncompleteCustomLineAsync(Guid actualWorkId, Guid accountId, Guid ownerId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var visit = await db.Set<ActualWork>().Include(x => x.Lines)
            .SingleAsync(x => x.AccountId == accountId && x.Id == actualWorkId);
        Assert.True(visit.AddLine(null, null, "Off-catalog part", "each", 1m, null, null, null, null, ownerId).IsSuccess);
        await db.SaveChangesAsync();
        return visit.ConcurrencyVersion;
    }

    private async Task<(Guid ActualWorkId, Guid ConcurrencyVersion)> CreateDraftAsync(string cookie, Guid requestId)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync("/keep/pricebook/actual-work/create", new { requestId });
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

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Actual Work Review Test Co {slug}",
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

    private async Task<Guid> SeedViewerAsync(Guid accountId, string slug)
    {
        var now = DateTime.UtcNow;
        var email = $"viewer@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), AccountUserRole.Viewer,
            inviteTokenHash: $"{slug}_viewer_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
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
