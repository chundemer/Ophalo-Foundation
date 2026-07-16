using System.Security.Cryptography;
using System.Text;
using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Short-lived SMS handoff record scoped to an account, not a specific request.
/// Carries the pre-built intake link message; the raw token is never stored.
/// Expires after 15 minutes (R88f-a, GAP-018).
/// </summary>
public sealed class KeepIntakeSmsHandoff : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string HandoffTokenHash { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string MessageBody { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }

    public static KeepIntakeSmsHandoff Create(
        Guid accountId,
        string handoffTokenHash,
        string customerPhone,
        string messageBody,
        Guid? createdByUserId,
        DateTime expiresAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffTokenHash, nameof(handoffTokenHash));
        ArgumentException.ThrowIfNullOrWhiteSpace(customerPhone, nameof(customerPhone));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageBody, nameof(messageBody));
        if (expiresAtUtc == default)
            throw new ArgumentException("ExpiresAtUtc must be a real timestamp.", nameof(expiresAtUtc));

        return new KeepIntakeSmsHandoff
        {
            AccountId        = accountId,
            HandoffTokenHash = handoffTokenHash,
            CustomerPhone    = customerPhone,
            MessageBody      = messageBody,
            CreatedByUserId  = createdByUserId,
            ExpiresAtUtc     = expiresAtUtc,
        };
    }

    public static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
