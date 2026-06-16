using System.Security.Cryptography;
using System.Text;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Generates and hashes single-use invite tokens.
///
/// Generation: 32 bytes from RandomNumberGenerator → URL-safe Base64 (no padding).
/// Hashing: uppercase SHA-256 hex digest (Convert.ToHexString) — matches the reference
/// InviteTokenGenerator and is invite-subsystem-local (ADR-076, ADR-013).
/// The raw token is never persisted; only the hash is stored.
/// </summary>
public static class InviteTokenGenerator
{
    public static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new ArgumentException("Raw token is required.", nameof(rawToken));

        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
