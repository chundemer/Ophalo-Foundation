using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Keep-owned record that authorizes public intake submissions for an account (ADR-046).
/// The raw intake token is never stored — only the SHA-256 lowercase-hex hash.
/// Active unique indexes on slug and token_hash enforce one active link per account.
/// </summary>
public sealed class KeepPublicIntakeLink : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string PublicSlug { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActive => !RevokedAtUtc.HasValue && !IsDeleted;

    public Result Revoke(DateTime nowUtc, Guid? modifiedByUserId = null)
    {
        if (RevokedAtUtc.HasValue)
            return Result.Failure(KeepPublicIntakeLinkErrors.AlreadyRevoked);

        RevokedAtUtc = nowUtc;
        ModifiedByUserId = modifiedByUserId;
        return Result.Success();
    }

    /// <summary>
    /// Updates the public slug in-place (ADR-429 ordinary rename, preserves alias).
    /// Returns true if the slug changed; false if the new slug equals the current one
    /// (no-op — caller must not create an alias row in this case).
    /// </summary>
    public bool RenameSlug(string newSlug)
    {
        if (string.IsNullOrWhiteSpace(newSlug))
            throw new ArgumentException("New slug is required.", nameof(newSlug));
        var normalized = newSlug.Trim().ToLowerInvariant();
        if (string.Equals(PublicSlug, normalized, StringComparison.OrdinalIgnoreCase))
            return false;
        PublicSlug = normalized;
        return true;
    }

    public static KeepPublicIntakeLink Create(
        Guid accountId,
        string publicSlug,
        string tokenHash,
        Guid? createdByUserId = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(publicSlug))
            throw new ArgumentException("Public slug is required.", nameof(publicSlug));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));

        return new KeepPublicIntakeLink
        {
            AccountId = accountId,
            PublicSlug = publicSlug.Trim(),
            TokenHash = tokenHash,
            CreatedByUserId = createdByUserId
        };
    }
}
