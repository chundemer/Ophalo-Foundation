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

    public Result Revoke(DateTime nowUtc)
    {
        if (RevokedAtUtc.HasValue)
            return Result.Failure(KeepPublicIntakeLinkErrors.AlreadyRevoked);

        RevokedAtUtc = nowUtc;
        return Result.Success();
    }

    public static KeepPublicIntakeLink Create(
        Guid accountId,
        string publicSlug,
        string tokenHash)
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
            TokenHash = tokenHash
        };
    }
}
