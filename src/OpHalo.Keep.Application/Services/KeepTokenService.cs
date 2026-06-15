using System.Security.Cryptography;
using System.Text;

namespace OpHalo.Keep.Application.Services;

/// <summary>
/// Generates and hashes tokens and reference codes used by Keep.
/// </summary>
/// <remarks>
/// All tokens use 32 bytes (256 bits) of cryptographic randomness.
/// Public intake tokens are hashed before storage (ADR-046, ADR-011).
/// Reference codes use a 32-character safe alphabet to avoid visual ambiguity
/// (0/O/1/I/L excluded) and are scoped unique per account.
/// </remarks>
public sealed class KeepTokenService
{
    // 31 uppercase alphanumeric chars; excludes 0, 1, I, L, O to avoid visual ambiguity.
    // (36 alphanumeric − 5 excluded = 31)
    private static readonly char[] ReferenceAlphabet =
        "ABCDEFGHJKMNPQRSTUVWXYZ23456789".ToCharArray();

    /// <summary>
    /// Generates a high-entropy opaque page token for public request page access.
    /// Returns a URL-safe base64 string (no padding, no +/).
    /// </summary>
    public string GeneratePageToken() => GenerateUrlSafeToken();

    /// <summary>
    /// Generates a high-entropy opaque public intake token (the raw value that is
    /// delivered to the account owner and never stored). Hash it via
    /// <see cref="HashPublicIntakeToken"/> before persisting.
    /// </summary>
    public string GeneratePublicIntakeToken() => GenerateUrlSafeToken();

    /// <summary>
    /// Returns the SHA-256 lowercase-hex hash of a raw public intake token.
    /// This is what gets stored in <c>KeepPublicIntakeLink.TokenHash</c>.
    /// </summary>
    public string HashPublicIntakeToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Generates an 8-character account-scoped reference code from a safe alphabet.
    /// Uses rejection sampling so every position is uniformly distributed (no modulo bias).
    /// Suitable for dictation to customers (e.g. "PQRS7842").
    /// </summary>
    public string GenerateReferenceCode()
    {
        const int length = 8;
        // Accept only bytes strictly below this threshold so that the accepted range
        // divides evenly by alphabet length (31). 256 % 31 = 8 → threshold = 248.
        var threshold = 256 - (256 % ReferenceAlphabet.Length);

        var chars = new char[length];
        var buffer = new byte[length * 2]; // 2× capacity keeps the loop rare
        var written = 0;

        while (written < length)
        {
            RandomNumberGenerator.Fill(buffer);
            foreach (var b in buffer)
            {
                if (b >= threshold) continue;
                chars[written++] = ReferenceAlphabet[b % ReferenceAlphabet.Length];
                if (written == length) break;
            }
        }

        return new string(chars);
    }

    private static string GenerateUrlSafeToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
