using System.Collections.Concurrent;

namespace OpHalo.Foundation.Application.Notifications;

/// <summary>
/// Per-instance, in-memory rate limiter for <b>operational</b> founder-channel alerts
/// (GAP-038, BL149, 038-2c — feedback delivery backlog/abandoned; 038-2c-ii — content-source
/// failure). Best-effort and not shared across API replicas — deliberately the same posture as
/// the updates last-known-good slot: an operational alert may be posted once per replica per
/// window rather than exactly once globally. Never used for the per-submission feedback
/// delivery itself, only for the summary alerts.
/// </summary>
public sealed class FounderAlertThrottle
{
    private readonly ConcurrentDictionary<string, DateTime> _lastSentUtc = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns <c>true</c> and records <paramref name="nowUtc"/> when no alert for
    /// <paramref name="key"/> has been sent within <paramref name="minInterval"/>; otherwise
    /// returns <c>false</c> and changes nothing. Safe under concurrent callers.
    /// </summary>
    public bool TryAcquire(string key, TimeSpan minInterval, DateTime nowUtc)
    {
        while (true)
        {
            if (_lastSentUtc.TryGetValue(key, out var last))
            {
                if (nowUtc - last < minInterval)
                    return false;
                if (_lastSentUtc.TryUpdate(key, nowUtc, last))
                    return true;
            }
            else if (_lastSentUtc.TryAdd(key, nowUtc))
            {
                return true;
            }
        }
    }
}
