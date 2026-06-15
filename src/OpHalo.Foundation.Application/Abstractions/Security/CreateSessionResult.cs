namespace OpHalo.Foundation.Application.Abstractions.Security;

/// <summary>
/// Returned by IAccountSessionService.CreateSession. Contains the raw opaque token for
/// transport (cookie or bearer header) and the absolute expiry for cookie creation.
/// The raw token must not be persisted or logged by callers.
/// </summary>
public sealed record CreateSessionResult(string RawToken, DateTime ExpiresAtUtc);
