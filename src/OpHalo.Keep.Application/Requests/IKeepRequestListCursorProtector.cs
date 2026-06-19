namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Protects list cursor tokens against tampering.
/// Implementations must use a keyed signing scheme (e.g. HMAC-SHA256) so that
/// payload modifications are detectable.
/// </summary>
public interface IKeepRequestListCursorProtector
{
    /// <summary>Returns an opaque signed token from the given plaintext payload.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Validates the token signature and extracts the plaintext payload.
    /// Returns false if the token is malformed, has been tampered with, or has an invalid signature.
    /// </summary>
    bool TryUnprotect(string token, out string? plaintext);
}
