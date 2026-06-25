using System.Security.Cryptography;
using System.Text;
using OpHalo.Foundation.Application.Devices;

namespace OpHalo.Foundation.Infrastructure.Devices;

/// <summary>
/// SHA-256 implementation of IPushTokenFingerprintService.
/// Produces a 64-character lowercase hex digest — non-reversible, deterministic.
/// Raw push tokens are never logged or returned.
/// </summary>
public sealed class Sha256PushTokenFingerprintService : IPushTokenFingerprintService
{
    public string Fingerprint(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public string LastFour(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        return rawToken.Length >= 4 ? rawToken[^4..] : rawToken;
    }
}
