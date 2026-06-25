namespace OpHalo.Foundation.Application.Devices;

/// <summary>
/// Produces safe diagnostic representations of raw push tokens.
/// Raw tokens are never stored in logs or returned to callers — only the fingerprint and last four.
/// </summary>
public interface IPushTokenFingerprintService
{
    /// <summary>Returns a deterministic, non-reversible SHA-256 hex digest of the raw token.</summary>
    string Fingerprint(string rawToken);

    /// <summary>Returns the last four characters of the raw token for diagnostic display.</summary>
    string LastFour(string rawToken);
}
