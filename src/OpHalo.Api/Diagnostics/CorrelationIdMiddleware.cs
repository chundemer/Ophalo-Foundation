namespace OpHalo.Api.Diagnostics;

/// <summary>
/// Assigns a server-generated correlation ID to every request. Never trusts a client-supplied
/// header — this route range includes unauthenticated public routes, and honoring an inbound
/// value would let a caller inject arbitrary text into structured logs. The ID is echoed on the
/// response and, along with <see cref="ReleaseIdentity"/>, attached to the logging scope for the
/// rest of the request pipeline — so a triaged log line ties back to both the request and the
/// exact deployed commit.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Guid.NewGuid().ToString("n");

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["ReleaseId"] = ReleaseIdentity.Current,
        }))
        {
            await next(context);
        }
    }
}
