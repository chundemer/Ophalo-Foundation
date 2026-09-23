using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Maintainability review item 5 preflight: a route-inventory baseline for
/// <see cref="OpHalo.Api.Keep.KeepEndpoints"/>, captured BEFORE the route-family split so the
/// split's "no behavior change" claim is provable rather than asserted. Locks every route this
/// file registers (path pattern + HTTP verb), whether it requires authentication (every route in
/// this file uses either the bare default <c>.RequireAuthorization()</c> policy or none — no named
/// policies exist here, so "auth-policy" collapses to that boolean), and its rate-limit policy name
/// where one is attached (<c>.RequireRateLimiting(...)</c>) — a silent drop of that during a
/// mechanical file split would be a real behavior change (rate-limit bypass), not just a code-shape
/// change, so it belongs in the same baseline even though the workboard's shorthand names only
/// "route+verb+auth-policy".
///
/// Scoped to endpoints whose handler delegate is declared on <c>KeepEndpoints</c> specifically
/// (including its compiler-generated closure classes) — <c>KeepEndpoints.cs</c> shares the
/// <c>/keep</c> path prefix with a dozen sibling files already split by family (PriceBookEndpoints,
/// OfferingAssemblyEndpoints, etc.); this test must not assert on their routes.
///
/// After the family split, this test's expected table moves unchanged (only DeclaringType strings
/// inside the filter may need updating if handlers move to differently-named classes) — if it still
/// passes, the split changed no route's path, verb, auth requirement, or rate-limit policy.
/// </summary>
public sealed class KeepEndpointsRouteInventoryTests : IClassFixture<KeepApiWebFactory>
{
    private readonly KeepApiWebFactory _factory;

    public KeepEndpointsRouteInventoryTests(KeepApiWebFactory factory) => _factory = factory;

    private static readonly (string Method, string Route, bool RequiresAuth, string? RateLimitPolicy)[] Expected =
    [
        ("DELETE", "/keep/pricebook/actual-work/{actualWorkId:guid}", true, null),
        ("DELETE", "/keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}", true, null),
        ("DELETE", "/keep/requests/{requestId:guid}/follow-up-on", true, null),
        ("DELETE", "/keep/requests/{requestId:guid}/mute", true, null),
        ("DELETE", "/keep/requests/{requestId:guid}/planned-for", true, null),
        ("DELETE", "/keep/requests/{requestId:guid}/responsible", true, null),
        ("DELETE", "/keep/requests/{requestId:guid}/watch", true, null),
        ("DELETE", "/keep/requests/{requestId:guid}/watchers/{accountUserId:guid}", true, null),
        ("GET", "/keep/intake-sms/{handoffToken}", false, "public-intake"),
        ("GET", "/keep/pricebook/actual-work/{actualWorkId:guid}/financial-detail", true, null),
        ("GET", "/keep/pricebook/actual-work/performer-candidates", true, null),
        ("GET", "/keep/pricebook/actual-work/recorder-candidates", true, null),
        ("GET", "/keep/pricebook/actual-work/request/{requestId:guid}/history", true, null),
        ("GET", "/keep/pricebook/actual-work/request/{requestId:guid}/pending-financial-reviews", true, null),
        ("GET", "/keep/pricebook/actual-work/review-queue", true, null),
        ("GET", "/keep/pricebook/actual-work/review-queue/count", true, null),
        ("GET", "/keep/public-intake/slug/{slug}/info", false, "public-intake"),
        ("GET", "/keep/public-intake/token/{publicIntakeToken}/info", false, "public-intake"),
        ("GET", "/keep/r/{pageToken}", false, "customer-write"),
        ("GET", "/keep/requests", true, null),
        ("GET", "/keep/requests/{requestId:guid}", true, null),
        ("GET", "/keep/requests/{requestId:guid}/related-work", true, null),
        ("GET", "/keep/requests/available", true, null),
        ("GET", "/keep/requests/lookup", true, null),
        ("GET", "/keep/requests/participant-candidates", true, null),
        ("GET", "/keep/setup", true, null),
        ("GET", "/keep/setup/guided", true, null),
        ("GET", "/keep/setup/intake", true, null),
        ("GET", "/keep/setup/onboarding", true, null),
        ("GET", "/keep/share-call/{handoffToken}", false, "public-intake"),
        ("GET", "/keep/share-sms/{handoffToken}", false, "public-intake"),
        ("PATCH", "/keep/requests/{requestId:guid}/status", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/expand-assembly", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/financial-disposition", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/lines", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}/financial-resolution", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/replace", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/review", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/submit", true, null),
        ("POST", "/keep/pricebook/actual-work/{actualWorkId:guid}/transfer-recorder", true, null),
        ("POST", "/keep/pricebook/actual-work/create", true, null),
        ("POST", "/keep/public-intake/slug/{slug}", false, "public-intake"),
        ("POST", "/keep/public-intake/token/{publicIntakeToken}", false, "public-intake"),
        ("POST", "/keep/r/{pageToken}/call_requested", false, "customer-write"),
        ("POST", "/keep/r/{pageToken}/cancellation_requested", false, "customer-write"),
        ("POST", "/keep/r/{pageToken}/feedback", false, "customer-write"),
        ("POST", "/keep/r/{pageToken}/information_added", false, "customer-write"),
        ("POST", "/keep/r/{pageToken}/question", false, "customer-write"),
        ("POST", "/keep/r/{pageToken}/timing_change_requested", false, "customer-write"),
        ("POST", "/keep/r/{pageToken}/update_request", false, "customer-write"),
        ("POST", "/keep/requests", true, null),
        ("POST", "/keep/requests/{requestId:guid}/attention/acknowledge", true, null),
        ("POST", "/keep/requests/{requestId:guid}/business-updates", true, null),
        ("POST", "/keep/requests/{requestId:guid}/call-handoff", true, null),
        ("POST", "/keep/requests/{requestId:guid}/classify", true, null),
        ("POST", "/keep/requests/{requestId:guid}/external-contact", true, null),
        ("POST", "/keep/requests/{requestId:guid}/feedback-review", true, null),
        ("POST", "/keep/requests/{requestId:guid}/follow-up-resolution", true, null),
        ("POST", "/keep/requests/{requestId:guid}/internal-notes", true, null),
        ("POST", "/keep/requests/{requestId:guid}/notification-confirmation", true, null),
        ("POST", "/keep/requests/{requestId:guid}/notification-preparation", true, null),
        ("POST", "/keep/requests/{requestId:guid}/share-intent", true, null),
        ("POST", "/keep/requests/{requestId:guid}/sms-handoff", true, null),
        ("POST", "/keep/setup/guided/defer/{step:int}", true, null),
        ("POST", "/keep/setup/intake/ensure", true, null),
        ("POST", "/keep/setup/intake/replace", true, null),
        ("POST", "/keep/setup/intake/sms-handoff", true, null),
        ("POST", "/keep/setup/onboarding/marks/quick-capture-exercise", true, null),
        ("POST", "/keep/setup/onboarding/marks/spam-classification", true, null),
        ("POST", "/keep/setup/onboarding/marks/tracker-review", true, null),
        ("PUT", "/keep/pricebook/actual-work/{actualWorkId:guid}/default-performer", true, null),
        ("PUT", "/keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}", true, null),
        ("PUT", "/keep/pricebook/actual-work/{actualWorkId:guid}/visit-note", true, null),
        ("PUT", "/keep/pricebook/actual-work/{actualWorkId:guid}/zero-line-disposition", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/follow-up-on", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/mute", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/planned-for", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/priority", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/responsible", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/service-location", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/watch", true, null),
        ("PUT", "/keep/requests/{requestId:guid}/watchers/{accountUserId:guid}", true, null),
        ("PUT", "/keep/setup/calendar", true, null),
        ("PUT", "/keep/setup/intake/link-name", true, null),
        ("PUT", "/keep/setup/policy", true, null),
        ("PUT", "/keep/setup/profile", true, null),
    ];

    [Fact]
    public void KeepEndpoints_route_inventory_matches_the_locked_baseline()
    {
        _ = _factory.Server; // force the real host (and its endpoint registrations) to build

        var dataSources = _factory.Services.GetServices<EndpointDataSource>();
        var actual = new List<(string Method, string Route, bool RequiresAuth, string? RateLimitPolicy)>();

        foreach (var dataSource in dataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is not RouteEndpoint routeEndpoint) continue;

                var declaringType = routeEndpoint.Metadata.GetMetadata<System.Reflection.MethodInfo>()?.DeclaringType;
                var isKeepEndpointsHandler = declaringType is not null
                    && declaringType.FullName is not null
                    && (declaringType.FullName == "OpHalo.Api.Keep.KeepEndpoints"
                        || declaringType.FullName.StartsWith("OpHalo.Api.Keep.KeepEndpoints+", StringComparison.Ordinal));
                if (!isKeepEndpointsHandler) continue;

                var httpMethods = routeEndpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                    ?? throw new InvalidOperationException($"Route {routeEndpoint.RoutePattern.RawText} has no HTTP method metadata.");
                var requiresAuth = routeEndpoint.Metadata.Any(m => m is IAuthorizeData);
                var rateLimitPolicy = routeEndpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

                foreach (var method in httpMethods)
                    actual.Add((method, routeEndpoint.RoutePattern.RawText!, requiresAuth, rateLimitPolicy));
            }
        }

        var actualSorted = actual.OrderBy(t => t.Method, StringComparer.Ordinal)
            .ThenBy(t => t.Route, StringComparer.Ordinal)
            .ToList();
        var expectedSorted = Expected.OrderBy(t => t.Method, StringComparer.Ordinal)
            .ThenBy(t => t.Route, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expectedSorted, actualSorted);
    }
}
