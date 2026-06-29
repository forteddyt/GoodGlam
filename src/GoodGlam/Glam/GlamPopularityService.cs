using System.Collections.Concurrent;
using Dalamud.Interface.ImGuiNotification;

namespace GoodGlam.Glam;

/// <summary>
/// Raises the "popular glamour" notification for a qualifying drop. Abstracted so the
/// orchestration/threshold logic can be unit-tested without the Dalamud framework thread.
/// </summary>
public interface INotifier
{
    void NotifyPopular(DropItem drop, GlamPopularity popularity);
}

/// <summary>Default notifier: raises a native Dalamud toast on the framework thread.</summary>
public sealed class DalamudNotifier : INotifier
{
    public void NotifyPopular(DropItem drop, GlamPopularity popularity)
    {
        var content = popularity.TopGlamUrl is null
            ? $"{drop.Name} is used in a popular glamour ({popularity.TopLoves} loves)."
            : $"{drop.Name} is used in a popular glamour ({popularity.TopLoves} loves).\n{popularity.TopGlamUrl}";

        // Notifications must be raised on the framework thread.
        _ = Services.Framework.RunOnFrameworkThread(() =>
            Services.Notifications.AddNotification(new Notification
            {
                Title = "GoodGlam",
                Content = content,
                Type = NotificationType.Info,
                InitialDuration = TimeSpan.FromSeconds(10),
            }));
    }
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

    public GlamPopularityService(Configuration config, IGlamSource source)
        : this(config, source, new DalamudNotifier())
    {
    }

    internal GlamPopularityService(Configuration config, IGlamSource source, INotifier notifier)
    {
        this.config = config;
        this.source = source;
        this.notifier = notifier;
    }

    /// <summary>
    /// Checks a dropped item and raises a notification if it qualifies as popular.
    /// Returns the popularity it found (or an empty result on error) so callers/diagnostics
    /// can inspect the outcome.
    /// </summary>
    public async Task<GlamPopularity> ProcessAsync(DropItem drop)
    {
        try
        {
            var popularity = await this.GetPopularityAsync(drop).ConfigureAwait(false);
            if (popularity.TopLoves >= this.config.LovesThreshold)
                this.notifier.NotifyPopular(drop, popularity);
            return popularity;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, $"GoodGlam: failed to check popularity for {drop.Name} ({drop.ItemId}).");
            return new GlamPopularity(0, null);
        }
    }

    private async Task<GlamPopularity> GetPopularityAsync(DropItem drop)
    {
        // Filters are global, but fold their signature into the cache key so toggling a filter
        // is treated as a fresh lookup instead of returning a stale, differently-filtered result.
        var filters = this.config.Filters;
        var cacheKey = $"{drop.ItemId}|{filters.Signature()}";
        if (this.cache.TryGetValue(cacheKey, out var entry) && !entry.IsExpired(this.config.CacheTtlHours))
            return entry.Popularity;

        var ecItem = await this.source.ResolveEcItemAsync(drop.Slot, drop.Name, drop.ItemId, CancellationToken.None)
            .ConfigureAwait(false);

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
