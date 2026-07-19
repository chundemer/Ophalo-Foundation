using System.Security.Cryptography;
using System.Text;
using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Short-lived call handoff record. Stores a hashed opaque token scoped to one request
/// and one customer phone number. Expires after 15 minutes; raw token is never stored.
/// Distinct from <see cref="KeepSmsHandoff"/> so the SMS message-body invariant is never
/// weakened to accommodate a purpose it does not serve (ADR-448, GAP-020).
/// </summary>
public sealed class KeepCallHandoff : BaseEntity
{
    public string HandoffTokenHash { get; private set; } = string.Empty;
    public Guid RequestId { get; private set; }
    public Guid AccountId { get; private set; }
    public string CustomerPhone { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public static KeepCallHandoff Create(
        string handoffTokenHash,
        Guid requestId,
        Guid accountId,
        string customerPhone,
        Guid createdBy,
        DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffTokenHash, nameof(handoffTokenHash));
        ArgumentException.ThrowIfNullOrWhiteSpace(customerPhone, nameof(customerPhone));
        if (requestId == Guid.Empty)  throw new ArgumentException("requestId is required.", nameof(requestId));
        if (accountId == Guid.Empty)  throw new ArgumentException("accountId is required.", nameof(accountId));
        if (createdBy == Guid.Empty)  throw new ArgumentException("createdBy is required.", nameof(createdBy));
        if (expiresAtUtc == default)  throw new ArgumentException("expiresAtUtc must be a real timestamp.", nameof(expiresAtUtc));

        return new KeepCallHandoff
        {
            HandoffTokenHash = handoffTokenHash,
            RequestId        = requestId,
            AccountId        = accountId,
            CustomerPhone    = customerPhone,
            CreatedBy        = createdBy,
            ExpiresAtUtc     = expiresAtUtc,
        };
    }

    /// <summary>
    /// Canonical SHA-256 hex hash used when storing or resolving a handoff token.
    /// Both create and resolve paths must use this method to ensure consistent hashing.
    /// </summary>
    public static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
