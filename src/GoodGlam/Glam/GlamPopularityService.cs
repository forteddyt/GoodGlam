using System.Collections.Concurrent;
using GoodGlam.Diagnostics;

namespace GoodGlam.Glam;

/// <summary>
/// Raises the "popular glamour" notification for a qualifying drop. Abstracted so the
/// orchestration/threshold logic can be unit-tested without the Dalamud framework thread.
///
/// Because the popularity check is dispatched fire-and-forget and completes on a thread-pool
/// thread (potentially after the player has switched characters), the target is <em>captured</em>
/// up front via <see cref="CaptureTarget"/> on the dispatching (framework) thread. The resulting
/// <see cref="INotificationTarget"/> records against the character that actually saw the drop, so a
/// mid-lookup character switch can never misattribute or lose the entry.
/// </summary>
public interface INotifier
{
    INotificationTarget CaptureTarget();
}

/// <summary>The character-bound sink a captured popularity result is delivered to on completion.</summary>
public interface INotificationTarget
{
    void NotifyPopular(DropOccurrence drop, GlamPopularity popularity);
}

/// <summary>
/// Orchestrates the popularity check for a dropped item: bridge game ID -> EC ID,
/// fetch the most-loved glamour, compare against the configured threshold, and notify.
/// Results are cached per item ID to stay polite to Eorzea Collection.
/// </summary>
public sealed class GlamPopularityService
{
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new();
    private readonly Configuration config;
    private readonly IGlamSource source;
    private readonly INotifier notifier;
    private readonly ITraceLogger<GlamPopularityService> log;

    public GlamPopularityService(
        Configuration config,
        IGlamSource source,
        INotifier notifier,
        ITraceLogger<GlamPopularityService>? log = null)
    {
        this.config = config;
        this.source = source;
        this.notifier = notifier;
        this.log = log ?? new TraceLogger<GlamPopularityService>();
    }

    /// <summary>
    /// Checks a dropped item and raises a notification if it qualifies as popular.
    /// Returns the popularity it found (or an empty result on error) so callers/diagnostics
    /// can inspect the outcome.
    /// </summary>
    public async Task<GlamPopularity> ProcessAsync(DropOccurrence drop)
    {
        // Capture the history target now, synchronously on the caller's (framework) thread, before
        // any await hands control to a thread-pool continuation. This pins the drop to the character
        // that's logged in right now, even if they switch before the lookup finishes.
        var target = this.notifier.CaptureTarget();
        var threshold = this.config.EffectiveThreshold(drop.Slot);
        this.log.Debug(
            $"checking {drop.Name} ({drop.ItemId}) [slot={drop.Slot.Key}] against threshold {threshold}.");
        try
        {
            var popularity = await this.GetPopularityAsync(drop).ConfigureAwait(false);
            var qualifies = popularity.TopLoves >= threshold;
            this.log.Debug(
                $"{drop.Name} -> topLoves={popularity.TopLoves} vs threshold={threshold} => " +
                $"{(qualifies ? "POPULAR (notifying)" : "below threshold")}.");
            if (qualifies)
                target.NotifyPopular(drop, popularity);
            return popularity;
        }
        catch (Exception ex)
        {
            this.log.Warning($"failed to check popularity for {drop.Name} ({drop.ItemId}).", ex);
            return new GlamPopularity(0, null);
        }
    }

    private async Task<GlamPopularity> GetPopularityAsync(DropOccurrence drop)
    {
        // Filters are global, but fold their signature into the cache key so toggling a filter
        // is treated as a fresh lookup instead of returning a stale, differently-filtered result.
        var filters = this.config.Filters;
        var cacheKey = $"{drop.ItemId}|{filters.Signature()}";
        if (this.cache.TryGetValue(cacheKey, out var entry) && !entry.IsExpired(this.config.CacheTtlHours))
        {
            this.log.Debug($"cache hit for key '{cacheKey}' (topLoves={entry.Popularity.TopLoves}).");
            return entry.Popularity;
        }

        this.log.Debug($"cache miss for key '{cacheKey}'; querying Eorzea Collection.");

        var ecItem = await this.source.ResolveEcItemAsync(drop.Slot, drop.Name, drop.ItemId, CancellationToken.None)
            .ConfigureAwait(false);

        if (ecItem is null)
        {
            this.log.Debug($"{drop.Name} ({drop.ItemId}) did not resolve to an EC item; treating as unpopular.");
        }

        var popularity = ecItem is null
            ? new GlamPopularity(0, null)
            : await this.source.GetTopPopularityAsync(drop.Slot, ecItem.EcId, filters, CancellationToken.None).ConfigureAwait(false);

        this.cache[cacheKey] = new CacheEntry(popularity, DateTime.UtcNow);
        return popularity;
    }

    private readonly record struct CacheEntry(GlamPopularity Popularity, DateTime FetchedUtc)
    {
        public bool IsExpired(int ttlHours)
        {
            // Guard against a non-positive TTL (manual config edit / future migration),
            // which would otherwise expire every entry instantly and hammer the network.
            var hours = Math.Max(1, ttlHours);
            return DateTime.UtcNow - this.FetchedUtc > TimeSpan.FromHours(hours);
        }
    }
}
