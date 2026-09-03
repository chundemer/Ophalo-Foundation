using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpHalo.Api.Diagnostics;
using OpHalo.Foundation.Core.Constants;
using Sentry;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Direct tests for the Sentry request-context tag processor (GAP-039, ADR-495 D2). Hosted in the
/// integration assembly only because it needs the ASP.NET <see cref="HttpContext"/> types; it uses
/// no web host.
/// </summary>
public sealed class RequestContextSentryEventProcessorTests
{
    private const string CorrelationId = "0123456789abcdef0123456789abcdef";

    private static RequestContextSentryEventProcessor ProcessorFor(HttpContext? context) =>
        new(new HttpContextAccessor { HttpContext = context });

    private static HttpContext AuthenticatedContext(string? accountId, string? userId)
    {
        var claims = new List<Claim>();
        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (accountId is not null)
            claims.Add(new Claim(AuthConstants.AccountIdClaimType, accountId));

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth")),
        };
    }

    [Fact]
    public void Process_AttachesCorrelationIdTag_FromHttpContextItems()
    {
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdMiddleware.HeaderName] = CorrelationId;

        var evt = ProcessorFor(context).Process(new SentryEvent());

        Assert.Equal(CorrelationId, evt.Tags["correlation_id"]);
    }

    [Fact]
    public void Process_AttachesAccountIdTag_ForAuthenticatedRequestWithValidClaims()
    {
        var accountId = Guid.NewGuid();
        var context = AuthenticatedContext(accountId.ToString(), Guid.NewGuid().ToString());

        var evt = ProcessorFor(context).Process(new SentryEvent());

        Assert.Equal(accountId.ToString(), evt.Tags["account_id"]);
    }

    [Fact]
    public void Process_WithNoHttpContext_AttachesNothingAndReturnsEvent()
    {
        var input = new SentryEvent();

        var evt = ProcessorFor(null).Process(input);

        Assert.Same(input, evt);
        Assert.DoesNotContain("correlation_id", evt.Tags.Keys);
        Assert.DoesNotContain("account_id", evt.Tags.Keys);
    }

    [Fact]
    public void Process_AnonymousRequest_DoesNotAttachAccountId()
    {
        var context = new DefaultHttpContext
        {
            // Unauthenticated identity: has the claims but no authentication type.
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(AuthConstants.AccountIdClaimType, Guid.NewGuid().ToString()),
            })),
        };

        var evt = ProcessorFor(context).Process(new SentryEvent());

        Assert.DoesNotContain("account_id", evt.Tags.Keys);
    }

    [Theory]
    [InlineData(null, "11111111-1111-1111-1111-111111111111")]                       // no account_id claim
    [InlineData("11111111-1111-1111-1111-111111111111", null)]                       // no user id claim
    [InlineData("not-a-guid", "11111111-1111-1111-1111-111111111111")]               // malformed account_id
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111")] // empty account_id
    [InlineData("11111111-1111-1111-1111-111111111111", "not-a-guid")]               // malformed user id
    public void Process_AuthenticatedWithIncompleteOrMalformedClaims_DoesNotAttachAccountId(
        string? accountId, string? userId)
    {
        var context = AuthenticatedContext(accountId, userId);

        var evt = ProcessorFor(context).Process(new SentryEvent());

        Assert.DoesNotContain("account_id", evt.Tags.Keys);
    }
}
