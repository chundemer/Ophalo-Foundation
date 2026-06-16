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
        string pageToken = "tok_abc123") =>
        KeepRequest.Create(
            AccountId, CustomerId,
            "Jane Smith", "0412345678", null,
            description, referenceCode, pageToken, Now);

    // --- Create ---

    [Fact]
    public void Create_initializes_received_status_and_last_business_activity()
    {
        var request = NewRequest();

        Assert.Equal(KeepRequestStatus.Received, request.Status);
        Assert.Equal(Now, request.LastBusinessActivityAt);
        Assert.Null(request.TerminatedAtUtc);
        Assert.Null(request.ExpiresAtUtc);
        Assert.Null(request.LastCustomerActivityAt);
        Assert.Null(request.CurrentStatusText);
    }

    [Fact]
    public void Create_stores_trimmed_fields()
    {
        var request = KeepRequest.Create(
            AccountId, CustomerId,
            "  Jane Smith  ", "  0412345678  ", " jane@example.com ",
            "  A burst pipe  ", "PQRS7842", "tok_abc", Now);

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
            KeepRequest.Create(Guid.Empty, CustomerId, "Jane", "04123", null, "desc", "REF1", "tok", Now));

    [Fact]
    public void Create_requires_non_empty_customer_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.Create(AccountId, Guid.Empty, "Jane", "04123", null, "desc", "REF1", "tok", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_customer_name(string name) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.Create(AccountId, CustomerId, name, "04123", null, "desc", "REF1", "tok", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_customer_phone(string phone) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.Create(AccountId, CustomerId, "Jane", phone, null, "desc", "REF1", "tok", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_description(string desc) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.Create(AccountId, CustomerId, "Jane", "04123", null, desc, "REF1", "tok", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_reference_code(string code) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.Create(AccountId, CustomerId, "Jane", "04123", null, "desc", code, "tok", Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_page_token(string token) =>
        Assert.Throws<ArgumentException>(() =>
            KeepRequest.Create(AccountId, CustomerId, "Jane", "04123", null, "desc", "REF1", token, Now));

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
}
