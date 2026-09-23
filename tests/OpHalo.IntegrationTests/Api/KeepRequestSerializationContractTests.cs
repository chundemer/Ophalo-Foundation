using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Maintainability review item 8 interim guard: <c>GET /keep/requests</c> and
/// <c>GET /keep/requests/{requestId}</c> both return an Application-layer service result directly
/// (<c>Results.Ok(result.Value)</c>) rather than through a dedicated response DTO — a field added
/// to <see cref="OpHalo.Keep.Application.Requests.GetKeepRequestListResult"/>,
/// <see cref="OpHalo.Keep.Application.Requests.KeepRequestSummary"/>, or
/// <see cref="OpHalo.Keep.Application.Requests.KeepRequestDetailResult"/> for any reason silently
/// becomes part of the public API contract, with no deliberate decision and nothing to catch it.
/// These two representative list/detail responses (the workboard's chosen scope — see the Decision
/// Queue entry for whether broader coverage is warranted) lock their exact top-level JSON property
/// set as an explicit whitelist: a field added or removed anywhere in the returned graph fails this
/// test, forcing a deliberate update here rather than a silent leak. Depth is top-level only (not
/// recursing into every nested object like <c>attention</c>/<c>ranking</c>/<c>availableActions</c>)
/// — deeper coverage is a decision for the eventual DTO-policy ADR, not assumed here.
///
/// The expected sets below were captured from this exact seeded scenario via the live app (same
/// "discover once, lock in as literal expected data" technique as
/// <see cref="KeepEndpointsRouteInventoryTests"/>), not hand-transcribed from the C# record
/// declarations — transcribing PascalCase field names to their camelCase JSON keys by hand risks
/// exactly the kind of silent error this test exists to prevent.
/// </summary>
public sealed class KeepRequestSerializationContractTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private string _ownerCookie = string.Empty;
    private Guid _requestId;

    public KeepRequestSerializationContractTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@contract-tests.com", name: "Contract Owner", businessName: "Contract Co",
            purpose: AccountPurpose.Business, timeZone: "Australia/Sydney", plan: AccountPlan.Trial,
            classification: AccountClassification.Production, nowUtc: now, trialEndsAtUtc: now.AddDays(30));
        Assert.True(provisionResult.IsSuccess);
        var graph = provisionResult.Value;

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

        var accountId = graph.Account.Id;
        var customer = KeepCustomer.Create(accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "CTRT0001", "seed_page_token_contract", now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, accountId, now));
        await db.SaveChangesAsync();
        _requestId = request.Id;

        var rawToken = await _factory.SeedSessionAsync(graph.Owner.Id, graph.Account.Id);
        _ownerCookie = $"{AuthConstants.CookieName}={rawToken}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AuthRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _ownerCookie);
        return client;
    }

    private static IReadOnlyList<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static readonly string[] ExpectedListEnvelopeKeys =
        ["listContext", "pageInfo", "requests", "viewCounts"];

    private static readonly string[] ExpectedListRowKeys =
    [
        "actions", "attention", "businessPriority", "contactPreference", "createdAtUtc",
        "currentStatusText", "currentUserNotification", "customerEmail", "customerName",
        "customerPhone", "feedbackReviewAgeBucket", "feedbackReviewDueAtUtc", "feedbackWasResolved",
        "hasInternalNote", "id", "intakeUrgency", "isPostCloseFollowUp", "isTerminal",
        "lastBusinessActivityAtUtc", "lastCustomerActivityAtUtc", "latestActivity", "needsShare",
        "originalSummary", "participation", "pendingFinancialReviewCount", "ranking",
        "readyToClose", "referenceCode", "rowContext", "serviceAddressLine1", "serviceAddressLine2",
        "serviceCity", "serviceState", "serviceZip", "source", "status", "statusCheck", "timing",
        "updatedAtUtc", "version",
    ];

    private static readonly string[] ExpectedDetailKeys =
    [
        "attentionClearedAtUtc", "attentionClearedByAccountUserId", "attentionClearReason",
        "attentionLevel", "attentionReason", "attentionSinceUtc", "availableActions",
        "businessName", "businessPriority", "contactActions", "contactPreference", "createdAtUtc",
        "currentStatusText", "currentUserParticipation", "customerEmail", "customerName",
        "customerPageLastViewedAtUtc", "customerPageViewedAfterLatestUpdate", "customerPhone",
        "description", "effectiveAttention", "events", "expiresAtUtc", "feedbackComment",
        "feedbackCommentVisible", "feedbackReviewAgeBucket", "feedbackReviewDueAtUtc",
        "feedbackReviewedAtUtc", "feedbackReviewedByAccountUserId", "feedbackReviewNote",
        "feedbackSubmittedAtUtc", "feedbackWasResolved", "firstRespondedAtUtc",
        "firstResponderAccountUserId", "firstResponseDueAtUtc", "firstResponseEventId",
        "followUpOnDate", "followUpOnNote", "followUpOnReason", "intakeUrgency",
        "lastBusinessActivityAt", "lastCustomerActivityAt", "navigation", "needsShare",
        "nextAttentionAtUtc", "origin", "pageToken", "participants", "pendingNotification",
        "plannedForDate", "priorityBand", "referenceCode", "requestId", "serviceAddressLine1",
        "serviceAddressLine2", "serviceCity", "serviceState", "serviceZip", "source", "status",
        "terminatedAtUtc", "validation", "version", "waitingDirection",
    ];

    [Fact]
    public async Task ListResponse_envelope_matches_the_locked_field_set()
    {
        var response = await AuthRequest().GetAsync("/keep/requests");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            ExpectedListEnvelopeKeys.OrderBy(n => n, StringComparer.Ordinal),
            PropertyNames(body));
    }

    [Fact]
    public async Task ListResponse_row_matches_the_locked_field_set()
    {
        var response = await AuthRequest().GetAsync("/keep/requests");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("requests")[0];

        Assert.Equal(
            ExpectedListRowKeys.OrderBy(n => n, StringComparer.Ordinal),
            PropertyNames(row));
    }

    [Fact]
    public async Task DetailResponse_matches_the_locked_field_set()
    {
        var response = await AuthRequest().GetAsync($"/keep/requests/{_requestId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            ExpectedDetailKeys.OrderBy(n => n, StringComparer.Ordinal),
            PropertyNames(body));
    }
}
