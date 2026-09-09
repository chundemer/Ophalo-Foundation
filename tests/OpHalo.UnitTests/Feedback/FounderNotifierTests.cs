using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Infrastructure.Notifications;
using Xunit;

namespace OpHalo.UnitTests.Feedback;

/// <summary>
/// Locks the generic founder-channel webhook notifier (BL149): a compact structured POST, and
/// fail-soft on every unhappy path — unconfigured, non-success, transport error, timeout — all
/// return <c>false</c> without throwing.
/// </summary>
public class FounderNotifierTests
{
    private static readonly FounderEvent Event = new(
        Type: "feedback_submitted",
        Summary: "[Bug] something broke",
        Correlation: "abc-123",
        Context: "{\"route\":\"/x\"}");

    private static FounderNotifier Build(StubHandler handler, string? webhookUrl)
    {
        var client = new HttpClient(handler);
        var settings = new FounderChannelSettings { WebhookUrl = webhookUrl };
        var env = new FakeHostEnvironment { EnvironmentName = "staging" };
        return new FounderNotifier(client, settings, env, NullLogger<FounderNotifier>.Instance);
    }

    [Fact]
    public async Task Unconfigured_webhook_returns_false_without_a_request()
    {
        var handler = new StubHandler();
        var notifier = Build(handler, webhookUrl: null);

        Assert.False(await notifier.NotifyAsync(Event, CancellationToken.None));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Success_response_returns_true_and_posts_a_google_chat_text_message()
    {
        var handler = new StubHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.NoContent) };
        var notifier = Build(handler, "https://chat.googleapis.com/v1/spaces/AAA/messages?key=k&token=t");

        Assert.True(await notifier.NotifyAsync(Event, CancellationToken.None));

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;

        // Google Chat incoming webhooks require a Chat message body — { "text": ... } at minimum;
        // no bare structured fields that Chat would reject.
        Assert.False(root.TryGetProperty("type", out _));
        Assert.False(root.TryGetProperty("environment", out _));
        var text = root.GetProperty("text").GetString()!;
        Assert.Contains("[staging] feedback_submitted", text);
        Assert.Contains("[Bug] something broke", text);
        Assert.Contains("correlation: abc-123", text);
        Assert.Contains("context: {\"route\":\"/x\"}", text);
    }

    [Fact]
    public void RenderText_includes_count_and_oldest_age_for_operational_alerts()
    {
        var alert = new FounderEvent(
            Type: "delivery_backlog",
            Summary: "feedback delivery backlog",
            Correlation: "id-1, id-2",
            Count: 3,
            OldestAge: "22m");

        var text = FounderNotifier.RenderText(alert, "Production");

        Assert.Contains("[production] delivery_backlog", text);
        Assert.Contains("count: 3", text);
        Assert.Contains("oldest: 22m", text);
        Assert.Contains("correlation: id-1, id-2", text);
        Assert.DoesNotContain("context:", text);
    }

    [Fact]
    public async Task Non_success_status_returns_false()
    {
        var handler = new StubHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) };
        var notifier = Build(handler, "https://hooks.example.com/abc");

        Assert.False(await notifier.NotifyAsync(Event, CancellationToken.None));
    }

    [Fact]
    public async Task Transport_exception_is_swallowed_and_returns_false()
    {
        var handler = new StubHandler { Responder = _ => throw new HttpRequestException("no route to host") };
        var notifier = Build(handler, "https://hooks.example.com/abc");

        Assert.False(await notifier.NotifyAsync(Event, CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_is_swallowed_and_returns_false()
    {
        var handler = new StubHandler { Responder = _ => throw new TaskCanceledException("timed out") };
        var notifier = Build(handler, "https://hooks.example.com/abc");

        Assert.False(await notifier.NotifyAsync(Event, CancellationToken.None));
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "staging";
        public string ApplicationName { get; set; } = "OpHalo.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        public int CallCount { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return Responder(request);
        }
    }
}
