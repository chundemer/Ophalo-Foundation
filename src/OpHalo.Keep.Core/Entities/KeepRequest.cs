using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A service request submitted through Keep. Customer contact info is denormalized
/// at creation so operator views remain stable if the customer record is updated later.
/// </summary>
public sealed class KeepRequest : BaseEntity
{
    public Guid AccountId { get; private set; }
    public Guid KeepCustomerId { get; private set; }

    // Denormalized at creation time — intentionally independent of KeepCustomer updates.
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string? CustomerEmail { get; private set; }

    public string Description { get; private set; } = string.Empty;
    public string? CurrentStatusText { get; private set; }
    public KeepRequestStatus Status { get; private set; } = KeepRequestStatus.Received;

    // ReferenceCode: short human-readable identifier (e.g. PQRS7842), account-scoped unique.
    // PageToken: high-entropy opaque token for public request page access.
    public string ReferenceCode { get; private set; } = string.Empty;
    public string PageToken { get; private set; } = string.Empty;

    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime LastBusinessActivityAt { get; private set; }
    public DateTime? LastCustomerActivityAt { get; private set; }

    public bool IsTerminal =>
        Status is KeepRequestStatus.Closed or KeepRequestStatus.Cancelled;

    public static KeepRequest Create(
        Guid accountId,
        Guid customerId,
        string customerName,
        string customerPhone,
        string? customerEmail,
        string description,
        string referenceCode,
        string pageToken,
        DateTime nowUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name is required.", nameof(customerName));
        if (string.IsNullOrWhiteSpace(customerPhone))
            throw new ArgumentException("Customer phone is required.", nameof(customerPhone));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));
        if (string.IsNullOrWhiteSpace(referenceCode))
            throw new ArgumentException("Reference code is required.", nameof(referenceCode));
        if (string.IsNullOrWhiteSpace(pageToken))
            throw new ArgumentException("Page token is required.", nameof(pageToken));

        return new KeepRequest
        {
            AccountId = accountId,
            KeepCustomerId = customerId,
            CustomerName = customerName.Trim(),
            CustomerPhone = customerPhone.Trim(),
            CustomerEmail = customerEmail?.Trim(),
            Description = description.Trim(),
            Status = KeepRequestStatus.Received,
            ReferenceCode = referenceCode.Trim(),
            PageToken = pageToken.Trim(),
            LastBusinessActivityAt = nowUtc
        };
    }
}
