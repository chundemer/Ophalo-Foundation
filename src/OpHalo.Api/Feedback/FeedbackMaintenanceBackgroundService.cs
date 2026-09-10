using System.Diagnostics;
using OpHalo.Foundation.Application.Feedback;

namespace OpHalo.Api.Feedback;

/// <summary>
/// Hosts the feedback delivery retry schedule (BL149 D5), the backlog/abandoned founder-channel
/// alert, and the 7-day-delivered / 30-day-abandoned retention sweep (GAP-038, 038-2c). One
/// <see cref="FeedbackDeliveryWorker"/> pass per minute, resolved in its own scope.
///
/// Safe for every API replica to run: retries are claimed in bounded batches and delivery is
/// at-least-once (the founder-channel receiver dedupes on the submission id); the retention sweep
/// deletes in <c>SKIP LOCKED</c> batches; operational alerts are rate-limited per instance.
/// </summary>
public sealed class FeedbackMaintenanceBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<FeedbackMaintenanceBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumStartupJitter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stay clear of host startup, readiness checks, and migrations; the randomized offset
        // becomes this replica's stable per-minute phase so replicas do not wake together.
        await Task.Delay(StartupDelay + TimeSpan.FromMilliseconds(
            Random.Shared.NextDouble() * MaximumStartupJitter.TotalMilliseconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<FeedbackDeliveryWorker>();
            await worker.RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown; BackgroundService exits without an error log.
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Feedback maintenance pass failed. {DurationMs}",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }
}
