using System.Security.Cryptography;
using System.Text;

namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// Produces the SHA-256 lowercase hex digest used for session token storage and lookup.
/// Raw session tokens are never persisted — only their hash.
/// All callers must use this utility — no inline hashing anywhere in the solution.
/// </summary>
public static class SessionHasher
{
    /// <summary>Hashes a raw session token. Returns a 64-character lowercase hex string.</summary>
    public static string HashToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
