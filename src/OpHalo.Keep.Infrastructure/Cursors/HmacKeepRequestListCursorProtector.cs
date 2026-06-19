using System.Security.Cryptography;
using System.Text;
using OpHalo.Keep.Application.Requests;

namespace OpHalo.Keep.Infrastructure.Cursors;

/// <summary>
/// Signs and verifies list cursor tokens using HMAC-SHA256.
/// Token format: base64url(plaintextBytes) + "." + base64url(hmac).
/// The HMAC is computed over the raw plaintext bytes; fixed-time comparison prevents
/// timing attacks against the signature (ADR-257, Session 4A).
/// </summary>
public sealed class HmacKeepRequestListCursorProtector : IKeepRequestListCursorProtector
{
    private readonly byte[] _key;

    public HmacKeepRequestListCursorProtector(byte[] key)
    {
        if (key is null || key.Length == 0)
            throw new ArgumentException("Signing key must not be empty.", nameof(key));
        _key = key;
    }

    public string Protect(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        using var hmac = new HMACSHA256(_key);
        var signature = hmac.ComputeHash(plaintextBytes);
        return Base64UrlEncode(plaintextBytes) + "." + Base64UrlEncode(signature);
    }

    public bool TryUnprotect(string token, out string? plaintext)
    {
        plaintext = null;
        if (string.IsNullOrEmpty(token)) return false;

        var dotIndex = token.IndexOf('.');
        if (dotIndex < 0 || dotIndex == token.Length - 1) return false;

        try
        {
            var payloadPart = token[..dotIndex];
            var sigPart = token[(dotIndex + 1)..];

            var payloadBytes = Base64UrlDecode(payloadPart);
            var providedSig = Base64UrlDecode(sigPart);

            using var hmac = new HMACSHA256(_key);
            var expectedSig = hmac.ComputeHash(payloadBytes);

            if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
                return false;

            plaintext = Encoding.UTF8.GetString(payloadBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        var padding = (4 - padded.Length % 4) % 4;
        return Convert.FromBase64String(padded + new string('=', padding));
    }
}
