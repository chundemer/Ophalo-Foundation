using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Messaging;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Bounded in-memory dispatch queue for magic-link email delivery (GAP-095 095-1). Registered
/// as a singleton; <see cref="MagicLinkDispatchBackgroundService"/> is the sole reader via
/// <see cref="ReadAllAsync"/>.
///
/// Deliberately not a durable/persisted outbox — delivery here is already best-effort (D4/D8),
/// so an item lost on process crash carries the same practical risk as today's synchronous
/// best-effort send failing. See BL155.
///
/// Admission is gated by an explicit counter rather than the channel's own bounded-capacity
/// drop modes: every non-<c>Wait</c> <see cref="BoundedChannelFullMode"/> still reports
/// <c>TryWrite</c> as successful when it silently drops an item, which would make the drop
/// unobservable here. The underlying channel is unbounded; this type enforces the capacity.
///
/// Drops are both logged and counted (<c>magic_link_dispatch.dropped</c>, tagged by
/// <c>reason</c>) so <see cref="MagicLinkDispatchSettings.QueueCapacity"/> can be tuned from
/// observed pilot behavior even without a metrics backend wired up to scrape it yet.
/// </summary>
public sealed class MagicLinkDispatchQueue : IMagicLinkDispatchQueue
{
    private static readonly Meter Meter = new("OpHalo.Foundation.Application.Auth.MagicLinkDispatchQueue");
    private static readonly Counter<long> DroppedCounter = Meter.CreateCounter<long>(
        "magic_link_dispatch.dropped",
        description: "Magic-link dispatch items dropped (queue full or shutting down).");

    private readonly Channel<MagicLinkDispatchItem> _channel = Channel.CreateUnbounded<MagicLinkDispatchItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly int _capacity;
    private readonly ILogger<MagicLinkDispatchQueue> _logger;
    private int _depth;

    public MagicLinkDispatchQueue(
        IOptions<MagicLinkDispatchSettings> settings,
        ILogger<MagicLinkDispatchQueue> logger)
    {
        _capacity = settings.Value.QueueCapacity;
        _logger = logger;
    }

    /// <summary>Approximate count of items currently buffered (dequeued items excluded).</summary>
    public int Depth => Volatile.Read(ref _depth);

    public void Enqueue(MagicLinkDispatchItem item)
    {
        if (Interlocked.Increment(ref _depth) > _capacity)
        {
            Interlocked.Decrement(ref _depth);
            DroppedCounter.Add(1, new KeyValuePair<string, object?>("reason", "queue_full"));

            // Tune MagicLinkDispatchSettings.QueueCapacity from the observed drop rate
            // (magic_link_dispatch.dropped counter, or this log if no metrics backend is wired).
            _logger.LogWarning(
                "Magic link dispatch queue full (capacity {Capacity}); dropped item for code {CodeId}.",
                _capacity,
                item.CodeId);
            return;
        }

        if (!_channel.Writer.TryWrite(item))
        {
            // Writer already completed (shutdown drain in progress) — stop admitting.
            Interlocked.Decrement(ref _depth);
            DroppedCounter.Add(1, new KeyValuePair<string, object?>("reason", "shutting_down"));
            _logger.LogWarning(
                "Magic link dispatch queue is shutting down; dropped item for code {CodeId}.",
                item.CodeId);
        }
    }

    /// <summary>
    /// Stops admitting new items. Called by <see cref="MagicLinkDispatchBackgroundService"/> at
    /// the start of shutdown so the drain below has a bounded, known set of remaining work —
    /// items enqueued after this point are dropped and logged by <see cref="Enqueue"/> above.
    /// </summary>
    public void CompleteAdding() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<MagicLinkDispatchItem> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _depth);
            yield return item;
        }
    }
}
