using System.Collections.Concurrent;

namespace ChatServer;

/// <summary>Thread-safe, in-memory sliding-window limiter for authentication endpoints.</summary>
internal sealed class SlidingWindowRateLimiter
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public bool TryAcquire(string key, int limit, TimeSpan window, out TimeSpan retryAfter)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        lock (entry.Timestamps)
        {
            while (entry.Timestamps.Count > 0 && now - entry.Timestamps.Peek() >= window)
                entry.Timestamps.Dequeue();

            if (entry.Timestamps.Count >= limit)
            {
                retryAfter = window - (now - entry.Timestamps.Peek());
                return false;
            }

            entry.Timestamps.Enqueue(now);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private sealed class Entry
    {
        public Queue<DateTimeOffset> Timestamps { get; } = new();
    }
}
