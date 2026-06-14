using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Foundation.Core.Helpers;

namespace OpHalo.Foundation.Core.Entities.Users;

/// <summary>
/// Represents verified control of an email inbox.
/// A User row is only ever created during /auth/exchange — code exchange is proof of inbox control.
/// A persisted User is always verified. There is no unverified User state.
/// </summary>
public sealed class User : BaseEntity
{
    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public DateTime? EmailVerifiedAtUtc { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }

    private User() { }

    /// <summary>
    /// Creates a new verified User. Must only be called from the /auth/exchange handler.
    /// Code exchange is proof of inbox control — the resulting User is verified at creation.
    ///
    /// Name is optional — it may be null if the operator did not provide it at /auth/start.
    /// An empty Name is a valid transient state; post-session onboarding collects it.
    /// </summary>
    public static User CreateVerified(string email, string? name, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));

        var normalizedEmail = EmailNormalizer.Normalize(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Email is required.", nameof(email));

        return new User
        {
            Email = normalizedEmail,
            Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim(),
            IsEmailVerified = true,
            EmailVerifiedAtUtc = nowUtc
        };
    }

    public void RecordLogin(DateTime loginAtUtc)
    {
        if (loginAtUtc == default)
            throw new ArgumentException("loginAtUtc must not be default.", nameof(loginAtUtc));
        if (loginAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("loginAtUtc must be UTC.", nameof(loginAtUtc));

        LastLoginAtUtc = loginAtUtc;
    }
}
