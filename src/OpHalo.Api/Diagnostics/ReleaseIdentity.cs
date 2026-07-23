namespace OpHalo.Api.Diagnostics;

/// <summary>
/// Release/version identity for this running instance, read once at process start. Railway sets
/// <c>RAILWAY_GIT_COMMIT_SHA</c> on every deploy; falls back to "local" outside Railway. Attached
/// to structured logs (via <see cref="CorrelationIdMiddleware"/>) so a triaged log line or error
/// event can be tied to the exact deployed commit — no separate public endpoint is exposed for it.
/// </summary>
public static class ReleaseIdentity
{
    public static readonly string Current =
        Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA") is { Length: > 0 } sha
            ? sha
            : "local";
}
