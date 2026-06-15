using System.Security.Cryptography;
using System.Text;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Generates and hashes single-use magic link codes.
///
/// Generation: 32 bytes from RandomNumberGenerator → URL-safe Base64 (no padding).
/// ~43 characters, 256 bits of entropy — safe for use in URLs and email links.
///
/// Hashing: SHA-256 hex digest of the UTF-8 encoded raw code.
/// Only the hash is persisted — the raw code is never stored.
/// </summary>
internal static class MagicLinkCodeGenerator
{
    public static string GenerateRawCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashCode(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
            throw new ArgumentException("Raw code is required.", nameof(rawCode));

        var bytes = Encoding.UTF8.GetBytes(rawCode);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
