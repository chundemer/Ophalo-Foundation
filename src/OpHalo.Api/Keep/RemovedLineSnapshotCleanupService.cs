using System.Diagnostics;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Api.Keep;

/// <summary>
/// Removes expired undo-delete snapshots outside request handling. It is safe for every API
/// replica to run: cleanup is global, idempotent, batched, and uses row-level SKIP LOCKED claims.
/// </summary>
public sealed class RemovedLineSnapshotCleanupService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<RemovedLineSnapshotCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumStartupJitter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Avoid competing with host startup, readiness checks, and migrations. The randomized
        // offset becomes this replica's stable hourly phase, so replicas do not wake together.
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
        var cutoffTime = clock.UtcNow.Subtract(Retention);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IProposedScopePersistence>();
            var result = await persistence.DeleteExpiredRemovedLineSnapshotsAsync(cutoffTime, stoppingToken);

            logger.LogInformation(
                "Expired removed-line snapshot cleanup completed. {CutoffTime} {DeletedRowsCount} {BatchCount} {DurationMs}",
                cutoffTime, result.DeletedRowCount, result.BatchCount,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown; BackgroundService will exit without an error log.
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Expired removed-line snapshot cleanup failed. {CutoffTime} {DurationMs}",
                cutoffTime, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }
}
