using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpHalo.Api.Auth;
using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.SharedKernel.Results;
using Testcontainers.PostgreSql;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Scripted IEmailSender: each call dequeues one behavior (block on a gate, or throw); once
/// exhausted, calls succeed immediately. Lets tests control exactly what the background worker
/// experiences per item without depending on wall-clock timing.
/// </summary>
public sealed class ScriptedEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<Func<Task<Result>>> _behaviors = new();

    /// <summary>
    /// Per-recipient call counts. Tests key waits/assertions on their own recipient addresses
    /// rather than an aggregate count, since a single WebApplicationFactory instance (and this
    /// sender) is shared across every [Fact] in the class via IClassFixture.
    /// </summary>
    public ConcurrentDictionary<string, int> RecipientCallCounts { get; } = new();

    public ConcurrentQueue<string> Recipients { get; } = new();

    public TaskCompletionSource<Result> EnqueueBlocking()
    {
        var gate = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        _behaviors.Enqueue(() => gate.Task);
        return gate;
    }

    public void EnqueueThrow(Exception ex) => _behaviors.Enqueue(() => throw ex);

    public async Task<Result> SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken cancellationToken)
    {
        RecipientCallCounts.AddOrUpdate(to, 1, (_, count) => count + 1);
        Recipients.Enqueue(to);

        if (_behaviors.TryDequeue(out var behavior))
            return await behavior();

        return Result.Success();
    }
}

/// <summary>
/// WebApplicationFactory that exercises the real GAP-095 095-1 dispatch path — bounded
/// <c>Channel&lt;T&gt;</c> + <c>MagicLinkDispatchBackgroundService</c> as registered in
/// Program.cs — rather than the synchronous test-double used by every other auth test
/// (see SynchronousMagicLinkDispatchQueue in KeepApiWebFactory.cs).
/// </summary>
public sealed class RealMagicLinkDispatchWebFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.5-alpine").Build();

    public readonly ScriptedEmailSender EmailSender = new();
    public readonly CapturingLoggerProvider LogCapture = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["App:PublicBaseUrl"] = "https://test.ophalo.com",
                ["Keep:RequestListCursorSigningKey"] = Convert.ToBase64String(new byte[32]),
                // Small, deterministic capacity so queue-full is reachable with a handful of calls.
                ["MagicLinkDispatch:QueueCapacity"] = "2",
            });
        });

        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));

        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<IEmailSender>(EmailSender);

            // Deliberately no IMagicLinkDispatchQueue override — this factory exercises the
            // real Channel<T> + MagicLinkDispatchBackgroundService from Program.cs.
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>
/// GAP-095 095-1 acceptance criteria against the real dispatch queue/worker: the request path
/// never awaits delivery, a full queue drops silently to the caller while logging the drop, and
/// one item's provider exception never stops the next item from being delivered.
/// </summary>
public sealed class MagicLinkDispatchBackgroundServiceTests(RealMagicLinkDispatchWebFactory factory)
    : IClassFixture<RealMagicLinkDispatchWebFactory>
{
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(20);
        }
    }

    private static object NewAccountBody(string email) => new
    {
        email,
        businessName = "GAP-095 Test Co",
        name = "Test Owner",
        timeZone = "America/Chicago",
    };

    [Fact]
    public async Task Start_ResponseCompletes_WhileWorkerRemainsBlockedInSendAsync()
    {
        var gate = factory.EmailSender.EnqueueBlocking();

        var responseTask = factory.CreateClient().PostAsJsonAsync(
            "/auth/start", NewAccountBody("gap095-blocking-1@example.com"));

        // The queued send never resolves during this test's setup; a request path that awaited
        // it inline would hang here. It must not.
        var completed = await Task.WhenAny(responseTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(responseTask, completed);

        var response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Proves the worker — not the request — is the one calling SendAsync: it has already
        // picked the item up and is parked inside the still-unreleased gate.
        await WaitUntilAsync(
            () => factory.EmailSender.RecipientCallCounts.ContainsKey("gap095-blocking-1@example.com"),
            TimeSpan.FromSeconds(5));

        gate.SetResult(Result.Success());
        await WaitUntilAsync(
            () => factory.EmailSender.Recipients.Contains("gap095-blocking-1@example.com"),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Start_QueueFull_DropsExcessAndLogsWithoutFailingTheCaller()
    {
        factory.LogCapture.Clear();
        var gate = factory.EmailSender.EnqueueBlocking();
        const string email1 = "gap095-qf-1@example.com";
        const string email2 = "gap095-qf-2@example.com";
        const string email3 = "gap095-qf-3@example.com";
        const string email4 = "gap095-qf-4@example.com";

        // Item 1 is dequeued immediately and blocks the worker inside SendAsync.
        var r1 = await factory.CreateClient().PostAsJsonAsync("/auth/start", NewAccountBody(email1));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        await WaitUntilAsync(() => factory.EmailSender.RecipientCallCounts.ContainsKey(email1), TimeSpan.FromSeconds(5));

        // Items 2 and 3 fill the capacity-2 queue behind it.
        var r2 = await factory.CreateClient().PostAsJsonAsync("/auth/start", NewAccountBody(email2));
        var r3 = await factory.CreateClient().PostAsJsonAsync("/auth/start", NewAccountBody(email3));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);

        // Item 4 arrives at a full queue and must be dropped — but the caller still sees a
        // neutral 200, identical to every other outcome.
        var r4 = await factory.CreateClient().PostAsJsonAsync("/auth/start", NewAccountBody(email4));
        Assert.Equal(HttpStatusCode.OK, r4.StatusCode);

        await WaitUntilAsync(
            () => factory.LogCapture.Messages.Any(m => m.Contains("queue full", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(5));

        gate.SetResult(Result.Success());
        await WaitUntilAsync(
            () => factory.EmailSender.RecipientCallCounts.ContainsKey(email2)
                && factory.EmailSender.RecipientCallCounts.ContainsKey(email3),
            TimeSpan.FromSeconds(5));

        // Let any incorrect extra delivery surface, then confirm the dropped 4th never arrives.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.DoesNotContain(email4, factory.EmailSender.Recipients);
    }

    [Fact]
    public async Task Start_OneItemThrows_DoesNotStopTheWorkerFromProcessingTheNext()
    {
        factory.LogCapture.Clear();
        factory.EmailSender.EnqueueThrow(new InvalidOperationException("simulated provider crash"));

        var r1 = await factory.CreateClient().PostAsJsonAsync("/auth/start", NewAccountBody("gap095-fail-1@example.com"));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await factory.CreateClient().PostAsJsonAsync("/auth/start", NewAccountBody("gap095-fail-2@example.com"));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        await WaitUntilAsync(
            () => factory.EmailSender.Recipients.Contains("gap095-fail-2@example.com"),
            TimeSpan.FromSeconds(5));

        Assert.Contains(
            factory.LogCapture.Messages,
            m => m.Contains("email delivery failed", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Adapts a <see cref="CapturingLoggerProvider"/>-backed <see cref="ILogger"/> to the generic
/// <see cref="ILogger{T}"/> shape the production constructors require, without needing a full
/// host — these tests construct <see cref="MagicLinkDispatchQueue"/> and
/// <see cref="MagicLinkDispatchBackgroundService"/> directly.
/// </summary>
public sealed class CapturingLogger<T>(CapturingLoggerProvider provider) : ILogger<T>
{
    private readonly ILogger _inner = provider.CreateLogger(typeof(T).FullName ?? typeof(T).Name);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _inner.Log(logLevel, eventId, state, exception, formatter);
}

/// <summary>
/// GAP-095 095-1 shutdown-drain acceptance criterion, tested directly against the production
/// queue/worker (no host, no database) for speed and determinism: <c>StopAsync</c> stops
/// admission and drains buffered work up to the shutdown deadline, logging what's left if the
/// deadline wins.
/// </summary>
public sealed class MagicLinkDispatchShutdownDrainTests
{
    private static MagicLinkDispatchItem Item(string email) =>
        new(Guid.NewGuid(), email, "Subject", "<html/>", "text", "Magic link");

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task StopAsync_DrainsBufferedItem_WhenItFinishesBeforeTheDeadline()
    {
        var sender = new ScriptedEmailSender();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailSender>(sender);
        await using var provider = services.BuildServiceProvider();

        var queue = new MagicLinkDispatchQueue(
            Options.Create(new MagicLinkDispatchSettings { QueueCapacity = 10 }),
            NullLogger<MagicLinkDispatchQueue>.Instance);
        var worker = new MagicLinkDispatchBackgroundService(
            queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MagicLinkDispatchBackgroundService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var gate = sender.EnqueueBlocking();
            const string blockedEmail = "gap095-drain-blocked@example.com";
            const string queuedEmail = "gap095-drain-queued@example.com";

            queue.Enqueue(Item(blockedEmail));
            await WaitUntilAsync(() => sender.RecipientCallCounts.ContainsKey(blockedEmail), TimeSpan.FromSeconds(5));

            queue.Enqueue(Item(queuedEmail));
            Assert.Equal(1, queue.Depth);

            gate.SetResult(Result.Success());

            using var shutdownDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(shutdownDeadline.Token);

            Assert.True(sender.RecipientCallCounts.ContainsKey(queuedEmail));
            Assert.Equal(0, queue.Depth);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopAsync_LogsRemainingDepth_WhenDrainExceedsTheShutdownDeadline()
    {
        var sender = new ScriptedEmailSender();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailSender>(sender);
        await using var provider = services.BuildServiceProvider();
        var logCapture = new CapturingLoggerProvider();

        var queue = new MagicLinkDispatchQueue(
            Options.Create(new MagicLinkDispatchSettings { QueueCapacity = 10 }),
            new CapturingLogger<MagicLinkDispatchQueue>(logCapture));
        var worker = new MagicLinkDispatchBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new CapturingLogger<MagicLinkDispatchBackgroundService>(logCapture));

        await worker.StartAsync(CancellationToken.None);
        var gate = sender.EnqueueBlocking();
        try
        {
            // This gate is deliberately not released during the test body — the worker stays
            // blocked on the first item, so the second item can never be drained in time.
            const string blockedEmail = "gap095-deadline-blocked@example.com";
            const string abandonedEmail = "gap095-deadline-abandoned@example.com";

            queue.Enqueue(Item(blockedEmail));
            await WaitUntilAsync(() => sender.RecipientCallCounts.ContainsKey(blockedEmail), TimeSpan.FromSeconds(5));

            queue.Enqueue(Item(abandonedEmail));
            Assert.Equal(1, queue.Depth);

            using var shutdownDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await worker.StopAsync(shutdownDeadline.Token);

            Assert.Equal(1, queue.Depth);
            Assert.Contains(
                logCapture.Messages,
                m => m.Contains("abandoned", StringComparison.OrdinalIgnoreCase)
                    && m.Contains("2 item(s)", StringComparison.Ordinal)
                    && m.Contains("1 in-flight", StringComparison.Ordinal)
                    && m.Contains("1 buffered", StringComparison.Ordinal));
        }
        finally
        {
            // Release the blocked send so the still-running worker task can finish and this
            // cleanup call doesn't itself hang forever.
            gate.TrySetResult(Result.Success());
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopAsync_LogsInFlightSend_WhenDeadlineExpiresWithNoBufferedFollowUp()
    {
        // GAP-095 095-1 review: queue.Depth excludes the item currently inside SendAsync — an
        // in-flight-only shutdown (nothing buffered behind it) must still be logged, not treated
        // as a clean drain just because Depth reads 0.
        var sender = new ScriptedEmailSender();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailSender>(sender);
        await using var provider = services.BuildServiceProvider();
        var logCapture = new CapturingLoggerProvider();

        var queue = new MagicLinkDispatchQueue(
            Options.Create(new MagicLinkDispatchSettings { QueueCapacity = 10 }),
            new CapturingLogger<MagicLinkDispatchQueue>(logCapture));
        var worker = new MagicLinkDispatchBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new CapturingLogger<MagicLinkDispatchBackgroundService>(logCapture));

        await worker.StartAsync(CancellationToken.None);
        var gate = sender.EnqueueBlocking();
        try
        {
            const string blockedEmail = "gap095-deadline-inflight-only@example.com";

            queue.Enqueue(Item(blockedEmail));
            await WaitUntilAsync(() => sender.RecipientCallCounts.ContainsKey(blockedEmail), TimeSpan.FromSeconds(5));

            // Nothing else enqueued — the only outstanding item is the one blocked in SendAsync.
            Assert.Equal(0, queue.Depth);

            using var shutdownDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await worker.StopAsync(shutdownDeadline.Token);

            Assert.Equal(0, queue.Depth);
            Assert.Contains(
                logCapture.Messages,
                m => m.Contains("abandoned", StringComparison.OrdinalIgnoreCase)
                    && m.Contains("1 item(s)", StringComparison.Ordinal)
                    && m.Contains("1 in-flight", StringComparison.Ordinal)
                    && m.Contains("0 buffered", StringComparison.Ordinal));
        }
        finally
        {
            gate.TrySetResult(Result.Success());
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
