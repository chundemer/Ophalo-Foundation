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
/// HTTP integration tests for ADR-494 D5 (4c-ii-a), the recorder-only Draft visit note:
///   PUT /keep/pricebook/actual-work/{actualWorkId}/visit-note
///
/// Covers set / replace / trim / clear, the shared recorder-ownership row authorization and
/// <c>X-Keep-ActualWork-Version</c> optimistic-concurrency protocol, the 2,000-character bound
/// (400, value and version unchanged), and the frozen-at-submit invariant.
/// </summary>
public sealed class ActualWorkVisitNoteApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkVisitNoteApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetVisitNote_FromNone_PersistsAndRotatesVersion()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("set-from-none");

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, version, "Customer reports intermittent fault");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newVersion = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(version, newVersion);
        Assert.Equal("Customer reports intermittent fault", await GetVisitNoteAsync(actualWorkId));
    }

    [Fact]
    public async Task SetVisitNote_ReplaceExisting_PersistsNewValue()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("set-replace");
        var first = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, version, "first");
        var v2 = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, v2, "second");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("second", await GetVisitNoteAsync(actualWorkId));
    }

    [Fact]
    public async Task SetVisitNote_TrimsSurroundingWhitespace()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("set-trim");

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, version, "  padded note  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("padded note", await GetVisitNoteAsync(actualWorkId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetVisitNote_BlankValue_PersistsNull(string? blank)
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("clear");
        var set = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, version, "something");
        var v2 = (await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, v2, blank);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await GetVisitNoteAsync(actualWorkId));
    }

    [Fact]
    public async Task SetVisitNote_Over2000Chars_Returns400_ValueAndVersionUnchanged()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("too-long");

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, version, new string('x', 2001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.VisitNoteTooLong", body.GetProperty("code").GetString());
        Assert.Null(await GetVisitNoteAsync(actualWorkId));
        Assert.Equal(version, await GetVersionAsync(actualWorkId));
    }

    [Fact]
    public async Task SetVisitNote_Exactly2000Chars_Succeeds()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("exactly-2000");

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, version, new string('y', 2000));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2000, (await GetVisitNoteAsync(actualWorkId))!.Length);
    }

    [Fact]
    public async Task SetVisitNote_CallerNotTheRecorder_Returns404()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("not-recorder");
        var operatorCookie = await GetCookieAsync(ctx.OperatorId, ctx.AccountId);

        var response = await PutVisitNoteAsync(operatorCookie, actualWorkId, version, "sneaky");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await GetVisitNoteAsync(actualWorkId));
        Assert.Equal(version, await GetVersionAsync(actualWorkId));
    }

    [Fact]
    public async Task SetVisitNote_StaleVersion_Returns409()
    {
        var (ctx, actualWorkId, _) = await SeedDraftAsync("stale");

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, Guid.NewGuid(), "note");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await GetVisitNoteAsync(actualWorkId));
    }

    [Fact]
    public async Task SetVisitNote_AfterSubmit_Returns409_AndTheSubmittedNoteIsFrozen()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("frozen");

        var set = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, version, "recorded in the field");
        var versionAfterNote = (await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();

        var addLine = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Coil clean", actualQuantity = 1m, note = (string?)null
            })
        };
        addLine.Headers.Add("X-Keep-ActualWork-Version", versionAfterNote.ToString("D"));
        var addLineResponse = await AuthRequest(ctx.OwnerCookie).SendAsync(addLine);
        Assert.Equal(HttpStatusCode.OK, addLineResponse.StatusCode);
        var versionAfterLine = (await addLineResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("actualWorkConcurrencyVersion").GetGuid();

        var submit = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/submit")
        {
            Content = JsonContent.Create(new { outcome = (string?)null, completionNote = (string?)null }),
        };
        submit.Headers.Add("X-Keep-ActualWork-Version", versionAfterLine.ToString("D"));
        var submitResponse = await AuthRequest(ctx.OwnerCookie).SendAsync(submit);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submittedVersion = (await submitResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        var response = await PutVisitNoteAsync(ctx.OwnerCookie, actualWorkId, submittedVersion, "too late");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("recorded in the field", await GetVisitNoteAsync(actualWorkId));
    }

    [Fact]
    public async Task SetZeroLineDisposition_OnDraft_PersistsAndRotatesVersion()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("zero-line-set");

        var response = await PutZeroLineDispositionAsync(
            ctx.OwnerCookie, actualWorkId, version, "NoAccess", "Customer not home");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotated = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(version, rotated);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var saved = await db.Set<ActualWork>().AsNoTracking().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(ActualWorkOutcome.NoAccess, saved.Outcome);
        Assert.Equal("Customer not home", saved.CompletionNote);
        Assert.Equal(rotated, saved.ConcurrencyVersion);
    }

    [Fact]
    public async Task SetZeroLineDisposition_CallerNotTheRecorder_Returns404()
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("zero-line-not-recorder");
        var operatorCookie = await GetCookieAsync(ctx.OperatorId, ctx.AccountId);

        var response = await PutZeroLineDispositionAsync(
            operatorCookie, actualWorkId, version, "NoAccess", "Customer not home");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetZeroLineDisposition_StaleVersion_Returns409()
    {
        var (ctx, actualWorkId, _) = await SeedDraftAsync("zero-line-stale");

        var response = await PutZeroLineDispositionAsync(
            ctx.OwnerCookie, actualWorkId, Guid.NewGuid(), "NoAccess", "Customer not home");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-outcome")]
    public async Task SetZeroLineDisposition_MissingOrInvalidOutcome_Returns400(string? outcome)
    {
        var (ctx, actualWorkId, version) = await SeedDraftAsync("zero-line-invalid");

        var response = await PutZeroLineDispositionAsync(
            ctx.OwnerCookie, actualWorkId, version, outcome, "Customer not home");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.InvalidOutcome", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Replace_SubmittedVisit_CreatesDraftSuccessor_AndRepeatReturnsAlreadySuperseded()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-route");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var source = ActualWork.Create(accountId, requestId, ownerId).Value;
        Assert.True(source.Submit(DateTime.UtcNow, ActualWorkOutcome.NoAccess, "Customer not home").IsSuccess);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<ActualWork>().Add(source);
            await db.SaveChangesAsync();
        }

        var response = await PostReplacementAsync(ownerCookie, source.Id, source.ConcurrencyVersion, "Wrong visit details");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var successorId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("successorActualWorkId").GetGuid();

        Guid sourceVersion;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var reloadedSource = await db.Set<ActualWork>().AsNoTracking().SingleAsync(x => x.Id == source.Id);
            var successor = await db.Set<ActualWork>().AsNoTracking().SingleAsync(x => x.Id == successorId);
            Assert.NotNull(reloadedSource.SupersededAtUtc);
            Assert.Equal(successorId, reloadedSource.SupersededByActualWorkId);
            Assert.Equal(ActualWorkStatus.Draft, successor.Status);
            Assert.Equal(ActualWorkOutcome.NoAccess, successor.Outcome);
            Assert.Equal("Customer not home", successor.CompletionNote);
            sourceVersion = reloadedSource.ConcurrencyVersion;
        }

        var repeated = await PostReplacementAsync(ownerCookie, source.Id, sourceVersion, "Again");

        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        var error = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.AlreadySuperseded", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Replace_NonOwnerAdmin_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("replace-forbidden");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "replace-forbidden");
        var requestId = await SeedRequestAsync(accountId);
        var source = ActualWork.Create(accountId, requestId, ownerId).Value;
        Assert.True(source.Submit(DateTime.UtcNow, ActualWorkOutcome.NoAccess, "Customer not home").IsSuccess);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<ActualWork>().Add(source);
            await db.SaveChangesAsync();
        }

        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var response = await PostReplacementAsync(operatorCookie, source.Id, source.ConcurrencyVersion, "Wrong visit details");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Replace_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("replace-stale");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        var source = ActualWork.Create(accountId, requestId, ownerId).Value;
        Assert.True(source.Submit(DateTime.UtcNow, ActualWorkOutcome.NoAccess, "Customer not home").IsSuccess);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<ActualWork>().Add(source);
            await db.SaveChangesAsync();
        }

        var response = await PostReplacementAsync(ownerCookie, source.Id, Guid.NewGuid(), "Wrong visit details");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.VersionMismatch", error.GetProperty("code").GetString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed record DraftContext(Guid AccountId, Guid OwnerId, string OwnerCookie, Guid OperatorId);

    private async Task<(DraftContext Ctx, Guid ActualWorkId, Guid Version)> SeedDraftAsync(string slug)
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync(slug);
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, slug);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId, operatorId);
        return (new DraftContext(accountId, ownerId, ownerCookie, operatorId), actualWorkId, version);
    }

    private async Task<HttpResponseMessage> PutVisitNoteAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, string? visitNote)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, $"/keep/pricebook/actual-work/{actualWorkId}/visit-note")
        {
            Content = JsonContent.Create(new { visitNote }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutZeroLineDispositionAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, string? outcome, string? completionNote)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, $"/keep/pricebook/actual-work/{actualWorkId}/zero-line-disposition")
        {
            Content = JsonContent.Create(new { outcome, completionNote }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostReplacementAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, string reason)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/replace")
        {
            Content = JsonContent.Create(new { reason }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    private async Task<string?> GetVisitNoteAsync(Guid actualWorkId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        return await db.Set<ActualWork>().Where(x => x.Id == actualWorkId)
            .Select(x => x.VisitNote).SingleAsync();
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
            businessName: $"Actual Work Visit Note Test Co {slug}",
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
