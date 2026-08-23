using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// ADR-489/ADR-490: KeepRequestDetailResult.EffectiveAttention must fold the three Needs Attention
/// queue-membership conditions (persisted attention, due/overdue Follow Up On, first-response
/// overdue) into one server-ranked verdict — persisted attention &gt; Follow Up On &gt;
/// first-response overdue. Every pairwise overlap and the triple overlap are covered explicitly
/// per the locked decision, not just the three isolated cases.
/// </summary>
public class KeepRequestEffectiveAttentionTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();

    // Request created at T0 with a 60-minute first-response target: due at T0+60.
    private static readonly DateTime T0 = new(2026, 6, 21, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<KeepRequestDetailResult> ExecuteAsync(KeepRequest request, DateTime nowUtc)
    {
        var persistence = new FakeDetailPersistence
        {
            UserSnapshot = new AccountUserSnapshot(UserId, AccountId, AccountUserRole.Owner, MembershipStatus.Active),
            AccountSnapshot = new AccountAccessSnapshot(
                AccountId, AccountLifecycleState.Active, AccountPurpose.Business, AccountPlan.Starter,
                AccountCommercialState.Active, AccountOperatingMode.Standard, null, null),
            Request = request
        };
        var sut = new GetKeepRequestDetailService(
            persistence,
            new FakeCurrentUser(UserId, AccountId),
            new FakeUserAccessPolicy(),
            new FakeAccountAccessPolicy(AccountAccessPosture.FullAccess),
            new FakeFeatureAccessPolicy(),
            new FakeClock(nowUtc));

        var result = await sut.ExecuteAsync(RequestId);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static KeepRequest MakeRequest() =>
        KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Alice", "555-0001", null, "A description",
            "REF001", "tok_" + Guid.NewGuid().ToString("N"), T0, firstResponseTargetMinutes: 60);

    // -----------------------------------------------------------------------
    // Isolated cases
    // -----------------------------------------------------------------------

    [Fact]
    public async Task No_condition_active_yields_none()
    {
        var request = MakeRequest();

        var detail = await ExecuteAsync(request, T0.AddMinutes(30)); // before first-response due

        Assert.Equal("none", detail.EffectiveAttention.Level);
        Assert.Null(detail.EffectiveAttention.Reason);
        Assert.Null(detail.EffectiveAttention.DueAtUtc);
        Assert.Null(detail.EffectiveAttention.GuidanceKey);
    }

    [Fact]
    public async Task Case1_only_persisted_attention_surfaces_as_is()
    {
        var request = MakeRequest();
        request.AddCustomerMessage(MessageIntent.GeneralMessage, "Any update?", 60, 240, 60, T0.AddMinutes(5));

        var detail = await ExecuteAsync(request, T0.AddMinutes(10)); // well before first-response due

        Assert.Equal("waiting", detail.EffectiveAttention.Level);
        Assert.Equal("customer_message", detail.EffectiveAttention.Reason);
        Assert.Equal("respond_to_customer", detail.EffectiveAttention.GuidanceKey);
    }

    [Theory]
    [InlineData(MessageIntent.GeneralMessage, "respond_to_customer")]
    [InlineData(MessageIntent.UpdateRequest, "respond_to_customer")]
    [InlineData(MessageIntent.ScheduleChangeRequest, "log_external_contact")]
    [InlineData(MessageIntent.ChangeOrCancelRequest, "respond_to_customer")]
    [InlineData(MessageIntent.Complaint, "respond_to_customer")]
    [InlineData(MessageIntent.CallRequested, "log_external_contact")]
    [InlineData(MessageIntent.TimingChangeRequested, "log_external_contact")]
    [InlineData(MessageIntent.CancellationRequested, "respond_to_customer")]
    public async Task Case1_persisted_customer_attention_routes_to_the_reason_specific_guidance_key(
        MessageIntent intent,
        string expectedGuidanceKey)
    {
        var request = MakeRequest();
        request.AddCustomerMessage(intent, "Customer request", 60, 240, 60, T0.AddMinutes(5));

        var detail = await ExecuteAsync(request, T0.AddMinutes(10));

        Assert.Equal(expectedGuidanceKey, detail.EffectiveAttention.GuidanceKey);
    }

    [Fact]
    public async Task Case2_due_today_outranks_latent_case3_and_is_needs_attention()
    {
        var request = MakeRequest();
        var now = T0.AddDays(3);
        // First-response SLA (case 3) is also latently overdue by `now` — case 2 must still win.
        var set = request.SetFollowUpOn(DateOnly.FromDateTime(now), FollowUpReason.Other, "note", UserId, "Actor", now);
        Assert.True(set.IsSuccess);

        var detail = await ExecuteAsync(request, now);

        Assert.Equal("needs_attention", detail.EffectiveAttention.Level);
        Assert.Equal("follow_up_due", detail.EffectiveAttention.Reason);
        Assert.Equal("resolve_follow_up", detail.EffectiveAttention.GuidanceKey);
        Assert.Equal(DateOnly.FromDateTime(now), detail.EffectiveAttention.DueOnDate);
        Assert.Null(detail.EffectiveAttention.DueAtUtc); // date-only promise — never a synthesized instant
    }

    [Fact]
    public async Task Case2_follow_up_date_in_past_is_overdue()
    {
        var request = MakeRequest();
        var set = request.SetFollowUpOn(DateOnly.FromDateTime(T0), FollowUpReason.Other, "note", UserId, "Actor", T0.AddMinutes(2));
        Assert.True(set.IsSuccess);
        var now = T0.AddDays(3);

        var detail = await ExecuteAsync(request, now);

        Assert.Equal("overdue", detail.EffectiveAttention.Level);
        Assert.Equal("follow_up_due", detail.EffectiveAttention.Reason);
        Assert.Equal(DateOnly.FromDateTime(T0), detail.EffectiveAttention.DueOnDate);
        Assert.Null(detail.EffectiveAttention.DueAtUtc);
    }

    [Fact]
    public async Task Case3_only_first_response_overdue_surfaces_dormant_reason()
    {
        var request = MakeRequest();

        var detail = await ExecuteAsync(request, T0.AddMinutes(61)); // past the 60-minute target

        Assert.Equal("overdue", detail.EffectiveAttention.Level);
        Assert.Equal("first_response_due", detail.EffectiveAttention.Reason);
        Assert.Equal(T0.AddMinutes(60), detail.EffectiveAttention.DueAtUtc);
        Assert.Null(detail.EffectiveAttention.DueOnDate);
        Assert.Equal("log_external_contact", detail.EffectiveAttention.GuidanceKey);
    }

    // -----------------------------------------------------------------------
    // Overlap precedence (ADR-489: case 1 > case 2 > case 3)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Case1_and_case2_overlap_persisted_attention_wins()
    {
        var request = MakeRequest();
        request.SetFollowUpOn(DateOnly.FromDateTime(T0), FollowUpReason.Other, "note", UserId, "Actor", T0.AddMinutes(1));
        request.AddCustomerMessage(MessageIntent.GeneralMessage, "Any update?", 60, 240, 60, T0.AddMinutes(5));

        var detail = await ExecuteAsync(request, T0.AddDays(3)); // follow-up now well overdue too

        Assert.Equal("customer_message", detail.EffectiveAttention.Reason);
        Assert.Equal("respond_to_customer", detail.EffectiveAttention.GuidanceKey);
    }

    [Fact]
    public async Task Case1_and_case3_overlap_persisted_attention_wins()
    {
        var request = MakeRequest();
        // Customer message before first response ever happens: attention active, no first response yet.
        request.AddCustomerMessage(MessageIntent.GeneralMessage, "Any update?", 60, 240, 60, T0.AddMinutes(5));

        var detail = await ExecuteAsync(request, T0.AddMinutes(90)); // also past first-response target

        Assert.Equal("customer_message", detail.EffectiveAttention.Reason);
        Assert.Equal("respond_to_customer", detail.EffectiveAttention.GuidanceKey);
    }

    [Fact]
    public async Task Case2_and_case3_overlap_follow_up_wins_over_generic_sla()
    {
        var request = MakeRequest();
        request.SetFollowUpOn(DateOnly.FromDateTime(T0), FollowUpReason.Other, "note", UserId, "Actor", T0.AddMinutes(1));

        var detail = await ExecuteAsync(request, T0.AddMinutes(90)); // follow-up due AND first response overdue

        Assert.Equal("follow_up_due", detail.EffectiveAttention.Reason);
        Assert.Equal("resolve_follow_up", detail.EffectiveAttention.GuidanceKey);
    }

    [Fact]
    public async Task Triple_overlap_persisted_attention_still_wins()
    {
        var request = MakeRequest();
        request.SetFollowUpOn(DateOnly.FromDateTime(T0), FollowUpReason.Other, "note", UserId, "Actor", T0.AddMinutes(1));
        request.AddCustomerMessage(MessageIntent.GeneralMessage, "Any update?", 60, 240, 60, T0.AddMinutes(5));

        var detail = await ExecuteAsync(request, T0.AddMinutes(90)); // all three conditions active

        Assert.Equal("customer_message", detail.EffectiveAttention.Reason);
    }

    [Fact]
    public async Task Persisted_attention_resolving_falls_through_to_next_ranked_reason()
    {
        var request = MakeRequest();
        request.AddCustomerMessage(MessageIntent.GeneralMessage, "Any update?", 60, 240, 60, T0.AddMinutes(5));
        var ack = request.AcknowledgeAttention("handled by phone", UserId, "Actor", T0.AddMinutes(20));
        Assert.True(ack.IsSuccess);

        // Persisted attention is now cleared; first response is still missing and now overdue.
        var detail = await ExecuteAsync(request, T0.AddMinutes(90));

        Assert.Equal("overdue", detail.EffectiveAttention.Level);
        Assert.Equal("first_response_due", detail.EffectiveAttention.Reason);
    }

    /// <summary>
    /// SubmitFeedback sets persisted attention (AttentionReason.UnresolvedFeedback) on a Closed
    /// request as an explicit ADR-138 exception to the terminal-no-attention posture. That row is
    /// never in Needs Attention (the list predicate excludes Closed outright — it belongs to the
    /// separate FeedbackReview queue), so EffectiveAttention must stay "none" here, not surface a
    /// reason case 1 was never meant to report to this specific contract.
    /// </summary>
    [Fact]
    public async Task Closed_unresolved_feedback_attention_does_not_leak_into_effective_attention()
    {
        var request = MakeRequest();
        request.ChangeStatus(KeepRequestStatus.Resolved, null, Guid.NewGuid(), "Actor", T0.AddMinutes(1));
        request.ChangeStatus(KeepRequestStatus.Closed, null, Guid.NewGuid(), "Actor", T0.AddMinutes(2));
        var feedback = request.SubmitFeedback(false, "Not happy", 60, T0.AddMinutes(3));
        Assert.True(feedback.IsSuccess);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel); // sanity: attention really is set
        Assert.Equal(AttentionReason.UnresolvedFeedback, request.AttentionReason);

        var detail = await ExecuteAsync(request, T0.AddMinutes(10));

        Assert.Equal("none", detail.EffectiveAttention.Level);
        Assert.Null(detail.EffectiveAttention.Reason);
        Assert.Null(detail.EffectiveAttention.GuidanceKey);
    }

    [Fact]
    public async Task Terminal_status_suppresses_all_three_conditions()
    {
        var request = MakeRequest();
        request.SetFollowUpOn(DateOnly.FromDateTime(T0), FollowUpReason.Other, "note", UserId, "Actor", T0.AddMinutes(1));
        request.ChangeStatus(KeepRequestStatus.Cancelled, "no longer needed", Guid.NewGuid(), "Actor", T0.AddMinutes(2));

        var detail = await ExecuteAsync(request, T0.AddDays(3)); // follow-up and first-response both long overdue

        Assert.Equal("none", detail.EffectiveAttention.Level);
        Assert.Null(detail.EffectiveAttention.Reason);
    }

    // --- Fakes (mirrors KeepRequestDetailServiceTests's pattern) ---

    private sealed class FakeDetailPersistence : IKeepRequestDetailPersistence
    {
        public AccountUserSnapshot? UserSnapshot { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }
        public KeepRequest? Request { get; set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<KeepRequest?> GetRequestAsync(
            Guid requestId, Guid accountId, Guid userId, KeepRequestVisibilityScope scope, CancellationToken ct) =>
            Task.FromResult(Request);

        public Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(Guid requestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepRequestEvent>>([]);

        public Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid requestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepParticipantProjection>>([]);

        public Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult<string?>("Test Business");

        public Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(string token, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid requestId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Guid>> GetReadyToCloseNavigationIdsAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<KeepRequestRelatedWorkQueryResult> GetOtherCustomerRequestsAsync(
            Guid keepCustomerId, Guid excludeRequestId, Guid accountId, Guid currentAccountUserId,
            KeepRequestVisibilityScope scope, int take, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class FakeCurrentUser(Guid userId, Guid accountId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid AccountId => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified => true;
    }

    private sealed class FakeUserAccessPolicy : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key) => true;
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureAccessPolicy : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string key) => true;
        public int GetLimit(AccountPlan plan, string key) => 0;
        public int ResolveLimit(AccountEntitlements e, string key) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}

/// <summary>
/// Contract guard for the DueAtUtc/DueOnDate split: Follow Up On's promised calendar date must
/// serialize as a bare date, never as an instant that a non-UTC client could reinterpret and shift
/// by a day. Uses System.Text.Json's default options — the same built-in DateOnly handling the API
/// host relies on (no custom JsonSerializerOptions/converters are registered for it).
/// </summary>
public class EffectiveAttentionSerializationContractTests
{
    // Mirrors ASP.NET Core's minimal-API default JSON options (camelCase property names); the API
    // host registers no custom JsonSerializerOptions, so this is what actually goes over the wire.
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void FollowUp_due_date_serializes_as_bare_date_with_no_time_or_offset()
    {
        var effectiveAttention = new EffectiveAttentionResult(
            Level: "overdue",
            Reason: "follow_up_due",
            DueAtUtc: null,
            DueOnDate: new DateOnly(2026, 8, 22),
            GuidanceKey: "resolve_follow_up");

        var json = System.Text.Json.JsonSerializer.Serialize(effectiveAttention, Options);

        Assert.Contains("\"dueOnDate\":\"2026-08-22\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2026-08-22T", json); // no time component
        Assert.DoesNotContain("Z\"", json); // no UTC/offset marker anywhere a shift could hide
        Assert.Contains("\"dueAtUtc\":null", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void First_response_due_serializes_as_a_real_instant_not_a_bare_date()
    {
        var effectiveAttention = new EffectiveAttentionResult(
            Level: "overdue",
            Reason: "first_response_due",
            DueAtUtc: new DateTime(2026, 8, 22, 14, 30, 0, DateTimeKind.Utc),
            DueOnDate: null,
            GuidanceKey: "log_external_contact");

        var json = System.Text.Json.JsonSerializer.Serialize(effectiveAttention, Options);

        Assert.Contains("2026-08-22T14:30:00", json);
        Assert.Contains("\"dueOnDate\":null", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Round_trip_preserves_the_exact_calendar_date_regardless_of_process_time_zone()
    {
        var original = new EffectiveAttentionResult(
            Level: "needs_attention", Reason: "follow_up_due",
            DueAtUtc: null, DueOnDate: new DateOnly(2026, 12, 31), GuidanceKey: "resolve_follow_up");

        var json = System.Text.Json.JsonSerializer.Serialize(original, Options);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<EffectiveAttentionResult>(json, Options);

        Assert.Equal(original.DueOnDate, roundTripped!.DueOnDate);
    }
}
