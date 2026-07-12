using System.Security.Cryptography;
using System.Text;
using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Short-lived SMS handoff record. Stores a hashed opaque token scoped to one request
/// and one prepared message context. Expires after 15 minutes; raw token is never stored.
/// Decision 15/21 (S25, build-log 082).
/// </summary>
public sealed class KeepSmsHandoff : BaseEntity
{
    public string HandoffTokenHash { get; private set; } = string.Empty;
    public Guid RequestId { get; private set; }
    public Guid AccountId { get; private set; }
    public string CustomerPhone { get; private set; } = string.Empty;
    public string MessageBody { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public static KeepSmsHandoff Create(
        string handoffTokenHash,
        Guid requestId,
        Guid accountId,
        string customerPhone,
        string messageBody,
        Guid createdBy,
        DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffTokenHash, nameof(handoffTokenHash));
        ArgumentException.ThrowIfNullOrWhiteSpace(customerPhone, nameof(customerPhone));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageBody, nameof(messageBody));
        if (requestId == Guid.Empty)  throw new ArgumentException("requestId is required.", nameof(requestId));
        if (accountId == Guid.Empty)  throw new ArgumentException("accountId is required.", nameof(accountId));
        if (createdBy == Guid.Empty)  throw new ArgumentException("createdBy is required.", nameof(createdBy));
        if (expiresAtUtc == default)  throw new ArgumentException("expiresAtUtc must be a real timestamp.", nameof(expiresAtUtc));

        return new KeepSmsHandoff
        {
            HandoffTokenHash = handoffTokenHash,
            RequestId        = requestId,
            AccountId        = accountId,
            CustomerPhone    = customerPhone,
            MessageBody      = messageBody,
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
