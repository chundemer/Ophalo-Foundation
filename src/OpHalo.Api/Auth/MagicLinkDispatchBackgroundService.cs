using OpHalo.Foundation.Application.Abstractions.Messaging;
using OpHalo.Foundation.Application.Auth;

namespace OpHalo.Api.Auth;

/// <summary>
/// Sole consumer of <see cref="MagicLinkDispatchQueue"/> (GAP-095 095-1): sends each queued
/// magic-link email out of band so provider latency never reaches the public
/// /auth/start or /auth/signin response.
///
/// Each item is sent in its own DI scope and its own try/catch — one provider exception or
/// scope failure must not stop delivery of the rest of the queue.
///
/// Shutdown (D4/D8, best-effort): <see cref="StopAsync"/> stops the queue from admitting new
/// work, then lets the read loop below drain whatever is already buffered — bounded by the
/// host's own shutdown deadline (<see cref="BackgroundService"/> cancels its internal stopping
/// token immediately, which is why the read loop uses <see cref="CancellationToken.None"/>
/// instead of it). If the deadline arrives before the drain finishes, both the still-buffered
/// depth and any item actively mid-<c>SendAsync</c> are logged — <see cref="MagicLinkDispatchQueue.Depth"/>
/// excludes the latter, since it's already been dequeued. A process kill can still lose
/// in-memory work either way, same as today's synchronous best-effort send.
/// </summary>
public sealed class MagicLinkDispatchBackgroundService(
    MagicLinkDispatchQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<MagicLinkDispatchBackgroundService> logger) : BackgroundService
{
    private volatile MagicLinkDispatchItem? _inFlightItem;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not stoppingToken: that token is cancelled the instant StopAsync begins, which would
        // abandon whatever is still queued without attempting delivery. The bounded drain below
        // (StopAsync) is what actually ends this loop during shutdown.
        await foreach (var item in queue.ReadAllAsync(CancellationToken.None))
        {
            _inFlightItem = item;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                var sendResult = await emailSender.SendAsync(
                    item.RecipientEmail,
                    item.Subject,
                    item.HtmlBody,
                    item.TextBody,
                    CancellationToken.None);

                if (sendResult.IsFailure)
                {
                    logger.LogWarning(
                        "{LogContext} email delivery failed for code {CodeId}: {ErrorCode}.",
                        item.LogContext,
                        item.CodeId,
                        sendResult.Error.Code);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{LogContext} email delivery failed for code {CodeId}.", item.LogContext, item.CodeId);
            }
            finally
            {
                _inFlightItem = null;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new work immediately, then let ExecuteAsync's read loop drain whatever
        // is already buffered, bounded by the host's shutdown deadline (cancellationToken).
        queue.CompleteAdding();

        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            var bufferedCount = queue.Depth;
            var inFlightCount = _inFlightItem is null ? 0 : 1;
            var abandonedCount = bufferedCount + inFlightCount;

            if (abandonedCount > 0)
            {
                logger.LogWarning(
                    "Magic link dispatch shutdown drain did not finish before the shutdown deadline; " +
                    "{AbandonedCount} item(s) abandoned ({InFlightCount} in-flight, {BufferedCount} buffered).",
                    abandonedCount,
                    inFlightCount,
                    bufferedCount);
            }
        }
    }
}
