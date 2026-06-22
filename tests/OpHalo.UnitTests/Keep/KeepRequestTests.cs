using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepRequestTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid CustomerId = Guid.NewGuid();
    static readonly DateTime Now = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    static KeepRequest NewRequest(
        string description = "Burst pipe in bathroom",
        string referenceCode = "PQRS7842",
        string pageToken = "tok_abc123",
        int firstResponseTargetMinutes = 60,
        KeepRequestOrigin origin = KeepRequestOrigin.Customer) =>
        origin == KeepRequestOrigin.Business
            ? KeepRequest.CreateByBusiness(
                AccountId, CustomerId,
                "Jane Smith", "0412345678", null,
                description, referenceCode, pageToken, Now)
            : KeepRequest.CreateFromCustomerIntake(
                AccountId, CustomerId,
                "Jane Smith", "0412345678", null,
                description, referenceCode, pageToken, Now,
                firstResponseTargetMinutes);

    // --- Create ---

    [Fact]
    public void CreateFromCustomerIntake_sets_customer_activity_and_leaves_business_null()
    {
        var request = NewRequest(); // Customer origin by default

        Assert.Equal(KeepRequestStatus.Received, request.Status);
        Assert.Null(request.LastBusinessActivityAt);       // business has not acted yet
        Assert.Equal(Now, request.LastCustomerActivityAt); // customer submitted → activity
        Assert.Null(request.TerminatedAtUtc);
        Assert.Null(request.ExpiresAtUtc);
        Assert.Null(request.CurrentStatusText);
    }

    [Fact]
    public void CreateByBusiness_sets_business_activity_and_leaves_customer_null()
    {
        var request = NewRequest(origin: KeepRequestOrigin.Business);

        Assert.Equal(KeepRequestStatus.Received, request.Status);
        Assert.Equal(Now, request.LastBusinessActivityAt); // business created → activity
        Assert.Null(request.LastCustomerActivityAt);       // customer has not acted yet
        Assert.Null(request.FirstResponseDueAtUtc);        // no timer on business-origin
        Assert.Null(request.TerminatedAtUtc);
        Assert.Null(request.ExpiresAtUtc);
        Assert.Null(request.CurrentStatusText);
    }

    [Fact]
    public void Create_stores_trimmed_fields()
    {
        var request = KeepRequest.CreateFromCustomerIntake(
            AccountId, CustomerId,
            "  Jane Smith  ", "  0412345678  ", " jane@example.com ",
            "  A burst pipe  ", "PQRS7842", "tok_abc", Now, 60);

        Assert.Equal("Jane Smith", request.CustomerName);
        Assert.Equal("0412345678", request.CustomerPhone);
        Assert.Equal("jane@example.com", request.CustomerEmail);
        Assert.Equal("A burst pipe", request.Description);
    }

    [Fact]
    public void Create_allows_null_customer_email()
    {
        var request = NewRequest();
        Assert.Null(request.CustomerEmail);
    }

    [Fact]
    public void Create_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(Guid.Empty, CustomerId, "Jane", "04123", null, "desc", "REF1", "tok", Now, 60));

    [Fact]
    public void Create_requires_non_empty_customer_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(AccountId, Guid.Empty, "Jane", "04123", null, "desc", "REF1", "tok", Now, 60));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_customer_name(string name) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, name, "04123", null, "desc", "REF1", "tok", Now, 60));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_customer_phone(string phone) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Jane", phone, null, "desc", "REF1", "tok", Now, 60));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_description(string desc) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Jane", "04123", null, desc, "REF1", "tok", Now, 60));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_reference_code(string code) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Jane", "04123", null, "desc", code, "tok", Now, 60));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_page_token(string token) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Jane", "04123", null, "desc", "REF1", token, Now, 60));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Create_throws_when_first_response_target_minutes_is_not_positive(int minutes) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.CreateFromCustomerIntake(AccountId, CustomerId, "Jane", "04123", null, "desc", "REF1", "tok", Now, minutes));

    [Fact]
    public void Create_with_customer_origin_sets_first_response_due_at_utc()
    {
        var request = NewRequest(firstResponseTargetMinutes: 90);

        Assert.Equal(Now.AddMinutes(90), request.FirstResponseDueAtUtc);
    }

    [Fact]
    public void Create_with_business_origin_sets_first_response_due_at_utc_to_null()
    {
        var request = NewRequest(origin: KeepRequestOrigin.Business);

        Assert.Null(request.FirstResponseDueAtUtc);
    }

    [Fact]
    public void Create_customer_origin_first_response_due_uses_exact_target_minutes()
    {
        var request = NewRequest(firstResponseTargetMinutes: 60);

        Assert.Equal(Now.AddMinutes(60), request.FirstResponseDueAtUtc);
    }

    // --- ChangeStatus: Cancelled expiry ---

    [Fact]
    public void ChangeStatus_Cancelled_sets_Status_TerminatedAtUtc_and_ExpiresAtUtc()
    {
        var request = NewRequest();
        var result = request.ChangeStatus(
            KeepRequestStatus.Cancelled, "Cancelled by customer", ActorId, "Jane", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(KeepRequestStatus.Cancelled, request.Status);
        Assert.Equal(Now, request.TerminatedAtUtc);
        Assert.Equal(Now.AddDays(30), request.ExpiresAtUtc);
        Assert.Equal(KeepRequestStatus.Cancelled, result.Value.StatusChangedEvent!.StatusAfter);
    }

    [Fact]
    public void ChangeStatus_Cancelled_missing_message_leaves_status_termination_and_expiry_unchanged()
    {
        var request = NewRequest();
        var result = request.ChangeStatus(
            KeepRequestStatus.Cancelled, null, ActorId, "Jane", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestStatus.Received, request.Status);
        Assert.Null(request.TerminatedAtUtc);
        Assert.Null(request.ExpiresAtUtc);
    }

    // --- IsTerminal ---

    [Theory]
    [InlineData(KeepRequestStatus.Received, false)]
    [InlineData(KeepRequestStatus.InProgress, false)]
    [InlineData(KeepRequestStatus.PendingCustomer, false)]
    [InlineData(KeepRequestStatus.Resolved, false)]
    [InlineData(KeepRequestStatus.Closed, true)]
    [InlineData(KeepRequestStatus.Cancelled, true)]
    public void IsTerminal_reflects_terminal_statuses(KeepRequestStatus status, bool expected)
    {
        // Create a plain instance via reflection to test IsTerminal across all statuses.
        var request = NewRequest();
        var statusProp = typeof(KeepRequest).GetProperty("Status")!;
        statusProp.SetValue(request, status, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, null, null);
        Assert.Equal(expected, request.IsTerminal);
    }

    // --- KeepRequestEvent factory ---

    [Fact]
    public void CreateRequestCreated_event_has_system_visibility()
    {
        var request = NewRequest();
        var ev = KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now);

        Assert.Equal(request.Id, ev.RequestId);
        Assert.Equal(AccountId, ev.AccountId);
        Assert.Equal(KeepRequestEventType.RequestCreated, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.System, ev.Visibility);
        Assert.Equal(Now, ev.OccurredAtUtc);
        Assert.Null(ev.Content);
        Assert.Null(ev.ActorAccountUserId);
    }

    [Fact]
    public void CreateRequestCreated_requires_non_empty_request_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequestEvent.CreateRequestCreated(Guid.Empty, AccountId, Now));

    [Fact]
    public void CreateRequestCreated_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequestEvent.CreateRequestCreated(Guid.NewGuid(), Guid.Empty, Now));

    // --- CreateRequestCreated (authenticated actor overload) ---

    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void CreateRequestCreated_actor_overload_requires_non_empty_request_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequestEvent.CreateRequestCreated(Guid.Empty, AccountId, ActorId, "Jane", Now));

    [Fact]
    public void CreateRequestCreated_actor_overload_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequestEvent.CreateRequestCreated(Guid.NewGuid(), Guid.Empty, ActorId, "Jane", Now));

    [Fact]
    public void CreateRequestCreated_actor_overload_requires_non_empty_actor_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequestEvent.CreateRequestCreated(Guid.NewGuid(), AccountId, Guid.Empty, "Jane", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRequestCreated_actor_overload_requires_non_blank_display_name(string name) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequestEvent.CreateRequestCreated(Guid.NewGuid(), AccountId, ActorId, name, Now));

    [Fact]
    public void CreateRequestCreated_actor_overload_requires_non_default_timestamp() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequestEvent.CreateRequestCreated(Guid.NewGuid(), AccountId, ActorId, "Jane", default));

    [Fact]
    public void CreateRequestCreated_actor_overload_sets_fields_and_trims_display_name()
    {
        var requestId = Guid.NewGuid();
        var ev = KeepRequestEvent.CreateRequestCreated(requestId, AccountId, ActorId, "  Jane Doe  ", Now);

        Assert.Equal(requestId, ev.RequestId);
        Assert.Equal(AccountId, ev.AccountId);
        Assert.Equal(KeepRequestEventType.RequestCreated, ev.EventType);
        Assert.Equal(KeepRequestEventVisibility.System, ev.Visibility);
        Assert.Equal(ActorType.AccountUser, ev.ActorType);
        Assert.Equal(ActorId, ev.ActorAccountUserId);
        Assert.Equal("Jane Doe", ev.ActorDisplayName);
        Assert.Equal(Now, ev.OccurredAtUtc);
    }
}
