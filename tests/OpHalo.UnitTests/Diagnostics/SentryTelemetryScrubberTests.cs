using System.Text;
using System.Text.Json;
using OpHalo.Api.Diagnostics;
using Sentry;
using Sentry.Protocol;

namespace OpHalo.UnitTests.Diagnostics;

/// <summary>
/// GAP-039 / ADR-495 D2. Every assertion is made against the final serialized event that would
/// leave the process — the representation Sentry's transport sends — not against intermediate
/// object state.
/// </summary>
public class SentryTelemetryScrubberTests
{
    private const string CustomerEmail = "jane.customer@example.com";
    private const string CustomerPhone = "+15551234567";
    private const string CustomerAddress = "42 Elm Street, Springfield";
    private const string SessionCookie = "ophalo.sid=supersecretsessionvalue";
    private const string AuthHeader = "Bearer eyJsupersecrettokenpayload";
    private const string ClientIp = "203.0.113.9";
    private const string QuerySecret = "ssn=900112222";

    private static string Serialize(SentryEvent evt)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            evt.WriteTo(writer, null!);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static SentryEvent FullyPopulatedEvent(string requestUrl = "https://api.ophalo.com/keep/requests/123?token=abc123&ssn=900112222#section")
    {
        var exception = new SentryException
        {
            Type = "System.InvalidOperationException",
            Value = $"Could not reach {CustomerEmail} / {CustomerPhone} at {CustomerAddress}",
            Module = "OpHalo.Keep.Application",
            Stacktrace = new SentryStackTrace(),
        };
        exception.Stacktrace.Frames.Add(new SentryStackFrame
        {
            Function = "HandleRequest",
            Module = "OpHalo.Keep.Application.Requests.RequestService",
            FileName = "RequestService.cs",
            AbsolutePath = "/build/src/OpHalo.Keep.Application/Requests/RequestService.cs",
            LineNumber = 88,
            ContextLine = $"var addr = \"{CustomerAddress}\";",
            PreContext = { $"// caller passed {CustomerEmail}" },
            PostContext = { "return Result.Failure();" },
        });

        var evt = new SentryEvent(new InvalidOperationException())
        {
            Release = "test-release-sha",
            Environment = "production",
            Level = SentryLevel.Error,
            Message = $"Unhandled failure while notifying {CustomerEmail}",
            ServerName = "prod-worker-01",
            SentryExceptions = new[] { exception },
        };

        evt.Request = new SentryRequest
        {
            Url = requestUrl,
            Method = "POST",
            Data = $"{{\"customerEmail\":\"{CustomerEmail}\",\"phone\":\"{CustomerPhone}\"}}",
            QueryString = QuerySecret,
            Cookies = SessionCookie,
        };
        evt.Request.Headers.Add("Authorization", AuthHeader);
        evt.Request.Headers.Add("Cookie", SessionCookie);
        evt.Request.Env.Add("REMOTE_ADDR", ClientIp);

        evt.User.Id = Guid.NewGuid().ToString();
        evt.User.Email = CustomerEmail;
        evt.User.Username = "jane.customer";
        evt.User.IpAddress = ClientIp;

        evt.AddBreadcrumb(new Breadcrumb(message: $"contacted {CustomerPhone}", type: "user"));
        evt.SetExtra("customerAddress", CustomerAddress);
        evt.Contexts.Device.Name = "Jane's iPhone";
        evt.Modules.Add("Sensitive.Internal.Assembly", "1.2.3");

        evt.SetTag("correlation_id", "0123456789abcdef0123456789abcdef");
        evt.SetTag("account_id", "11111111-1111-1111-1111-111111111111");
        evt.SetTag("http.status_code", "500");
        evt.SetTag("url", "https://api.ophalo.com/keep/r/leakedpagetoken");
        evt.SetTag("server_name", "prod-worker-01");
        evt.SetTag("RequestPath", $"/keep/r/leakedpagetoken?email={CustomerEmail}");

        return evt;
    }

    [Fact]
    public void Scrub_RemovesAllProtectedRequestAndCustomerData()
    {
        var json = Serialize(SentryTelemetryScrubber.Scrub(FullyPopulatedEvent())!);

        foreach (var forbidden in new[]
        {
            CustomerEmail, CustomerPhone, CustomerAddress, SessionCookie, AuthHeader, ClientIp,
            QuerySecret, "token=abc123", "#section", "section", "Jane's iPhone",
            "Sensitive.Internal.Assembly", "prod-worker-01", "leakedpagetoken",
            "Unhandled failure", "Could not reach",
        })
        {
            Assert.DoesNotContain(forbidden, json);
        }
    }

    [Fact]
    public void Scrub_RetainsAllowlistedDiagnosticData()
    {
        var scrubbed = SentryTelemetryScrubber.Scrub(FullyPopulatedEvent())!;
        var json = Serialize(scrubbed);

        Assert.Contains("test-release-sha", json);
        Assert.Contains("production", json);
        Assert.Contains("POST", json);
        Assert.Contains("/keep/requests/123", json);
        Assert.Contains("0123456789abcdef0123456789abcdef", json);
        Assert.Contains("11111111-1111-1111-1111-111111111111", json);
        Assert.Contains("InvalidOperationException", json);
        Assert.Contains("HandleRequest", json);

        Assert.Equal("POST", scrubbed.Request!.Method);
        Assert.Null(scrubbed.Request!.QueryString);
        Assert.Null(scrubbed.Request!.Cookies);
        Assert.Empty(scrubbed.Request!.Headers);
        Assert.Empty(scrubbed.Request!.Env);
        Assert.Empty(scrubbed.SentryExceptions!.Single().Stacktrace!.Frames
            .Where(f => f.ContextLine is not null || f.PreContext.Count > 0 || f.PostContext.Count > 0));
        Assert.Null(scrubbed.SentryExceptions!.Single().Value);
        Assert.Empty(scrubbed.Breadcrumbs);
        Assert.All(scrubbed.Tags.Keys, key =>
            Assert.Contains(key, new[] { "correlation_id", "account_id", "http.status_code" }));
    }

    [Theory]
    [InlineData("https://api.ophalo.com/keep/public-intake/token/RAWSECRET", "/keep/public-intake/token/[redacted]")]
    [InlineData("https://api.ophalo.com/continuity/public-intake/token/RAWSECRET", "/continuity/public-intake/token/[redacted]")]
    [InlineData("https://api.ophalo.com/keep/r/RAWSECRET", "/keep/r/[redacted]")]
    [InlineData("https://api.ophalo.com/keep/r/RAWSECRET/message", "/keep/r/[redacted]/message")]
    [InlineData("https://api.ophalo.com/keep/intake-sms/RAWSECRET", "/keep/intake-sms/[redacted]")]
    [InlineData("https://api.ophalo.com/keep/share-sms/RAWSECRET", "/keep/share-sms/[redacted]")]
    [InlineData("https://api.ophalo.com/keep/share-call/RAWSECRET", "/keep/share-call/[redacted]")]
    public void Scrub_RedactsEveryPublicTokenRouteFamily(string requestUrl, string expectedRoute)
    {
        var scrubbed = SentryTelemetryScrubber.Scrub(FullyPopulatedEvent(requestUrl))!;

        Assert.Equal(expectedRoute, scrubbed.Request!.Url);
        Assert.DoesNotContain("RAWSECRET", Serialize(scrubbed));
    }

    [Theory]
    [InlineData("https://api.ophalo.com/health/live")]
    [InlineData("https://api.ophalo.com/health/ready")]
    [InlineData("/health/ready")]
    public void Scrub_DiscardsHealthEndpointEvents(string requestUrl)
    {
        Assert.Null(SentryTelemetryScrubber.Scrub(FullyPopulatedEvent(requestUrl)));
    }

    [Fact]
    public void Scrub_DiscardsEvent_WhenUnredactedTokenSurvivesInStackFramePath()
    {
        var evt = FullyPopulatedEvent("https://api.ophalo.com/keep/requests/1");
        // A capability token that leaked into a retained non-path string (here a frame function
        // name) — path sanitization does not touch it, so the residual guard must discard.
        evt.SentryExceptions!.Single().Stacktrace!.Frames.Add(new SentryStackFrame
        {
            Function = "Invoke /keep/r/leakedtoken123",
            AbsolutePath = "/build/src/Log.cs",
        });

        Assert.Null(SentryTelemetryScrubber.Scrub(evt));
    }

    [Fact]
    public void Scrub_WithNoRequest_StillReturnsGroupableEvent()
    {
        var evt = new SentryEvent(new InvalidOperationException())
        {
            Release = "test-release-sha",
            Environment = "production",
            SentryExceptions = new[]
            {
                new SentryException { Type = "System.TimeoutException", Value = "boom", Module = "X" },
            },
        };

        var scrubbed = SentryTelemetryScrubber.Scrub(evt)!;

        Assert.Null(scrubbed.Request?.Url);
        Assert.Null(scrubbed.Request?.Method);
        Assert.Equal("System.TimeoutException", scrubbed.SentryExceptions!.Single().Type);
        Assert.Null(scrubbed.SentryExceptions!.Single().Value);
    }

    [Fact]
    public void Scrub_SanitizesStackFramePaths_StrippingQueryFragmentAndEmail()
    {
        var evt = FullyPopulatedEvent("https://api.ophalo.com/keep/requests/1");
        evt.SentryExceptions!.Single().Stacktrace!.Frames.Add(new SentryStackFrame
        {
            Function = "Log",
            FileName = $"https://host/path?email={CustomerEmail}#fragment",
            AbsolutePath = "https://host/some/dir?token=abc123#section",
        });

        var scrubbed = SentryTelemetryScrubber.Scrub(evt)!;
        var frame = scrubbed.SentryExceptions!.Single().Stacktrace!.Frames[^1];
        var json = Serialize(scrubbed);

        Assert.Equal("/path", frame.FileName);
        Assert.Equal("/some/dir", frame.AbsolutePath);
        Assert.DoesNotContain(CustomerEmail, json);
        Assert.DoesNotContain("fragment", json);
        Assert.DoesNotContain("token=abc123", json);
    }

    [Theory]
    [InlineData("correlation_id", "not-a-guid-n-value")]
    [InlineData("correlation_id", "0123456789abcdef0123456789abcde")]        // 31 hex chars
    [InlineData("correlation_id", "0123456789abcdef0123456789abcdefff")]     // 33 hex chars
    [InlineData("account_id", "00000000-0000-0000-0000-000000000000")]      // empty GUID
    [InlineData("account_id", "not-a-guid")]
    [InlineData("http.status_code", "600")]
    [InlineData("http.status_code", "99")]
    [InlineData("http.status_code", "abc")]
    public void Scrub_DropsAllowlistedTag_WhenValueIsMalformed(string key, string value)
    {
        var evt = FullyPopulatedEvent("https://api.ophalo.com/keep/requests/1");
        evt.SetTag(key, value);

        var scrubbed = SentryTelemetryScrubber.Scrub(evt)!;

        Assert.DoesNotContain(key, scrubbed.Tags.Keys);
    }

    [Fact]
    public void Scrub_NormalizesValidCorrelationIdTagToLowerHex()
    {
        var evt = FullyPopulatedEvent("https://api.ophalo.com/keep/requests/1");
        evt.SetTag("correlation_id", "0123456789ABCDEF0123456789ABCDEF");

        var scrubbed = SentryTelemetryScrubber.Scrub(evt)!;

        Assert.Equal("0123456789abcdef0123456789abcdef", scrubbed.Tags["correlation_id"]);
    }

    [Fact]
    public void Scrub_DropsDisallowedTagsIncludingUrlAndServerName()
    {
        var scrubbed = SentryTelemetryScrubber.Scrub(FullyPopulatedEvent())!;

        Assert.DoesNotContain("url", scrubbed.Tags.Keys);
        Assert.DoesNotContain("server_name", scrubbed.Tags.Keys);
        Assert.DoesNotContain("RequestPath", scrubbed.Tags.Keys);
    }
}
