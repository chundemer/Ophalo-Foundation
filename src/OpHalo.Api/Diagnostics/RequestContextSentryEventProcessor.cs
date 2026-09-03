using System.Security.Claims;
using OpHalo.Foundation.Core.Constants;
using Sentry;
using Sentry.Extensibility;

namespace OpHalo.Api.Diagnostics;

/// <summary>
/// Attaches the two request-scoped identifiers permitted by ADR-495 D2 as Sentry tags:
/// the server-generated correlation ID, and — only for a framework-authenticated request that
/// carries a valid OpHalo <c>account_id</c> claim — that account ID.
///
/// It never reads a scoped service. The authenticated account is taken directly from
/// <see cref="HttpContext.User"/> at capture time, so the processor is safe to register as a
/// singleton. No context or no valid claim means no <c>account_id</c> tag is emitted; the tag is
/// never set for public or failed-authentication requests.
///
/// <see cref="SentryTelemetryScrubber"/> runs afterwards as the final boundary and keeps only the
/// allowlisted tags.
/// </summary>
public sealed class RequestContextSentryEventProcessor(IHttpContextAccessor httpContextAccessor)
    : ISentryEventProcessor
{
    public SentryEvent Process(SentryEvent @event)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
            return @event;

        if (context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var correlationId)
            && correlationId is string { Length: > 0 } id)
        {
            @event.SetTag("correlation_id", id);
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var accountId = context.User.FindFirst(AuthConstants.AccountIdClaimType)?.Value;
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(accountId, out var account) && account != Guid.Empty
                && Guid.TryParse(userId, out var user) && user != Guid.Empty)
            {
                @event.SetTag("account_id", account.ToString());
            }
        }

        return @event;
    }
}
