using System.Collections.Concurrent;
using Dalamud.Interface.ImGuiNotification;

namespace GoodGlam.Glam;

/// <summary>
/// Orchestrates the popularity check for a dropped item: bridge game ID -> EC ID,
/// fetch the most-loved glamour, compare against the configured threshold, and notify.
/// Results are cached per item ID to stay polite to Eorzea Collection.
/// </summary>
public sealed class GlamPopularityService(Configuration config, IGlamSource source)
{
    private readonly ConcurrentDictionary<uint, CacheEntry> cache = new();

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
            if (popularity.TopLoves >= config.LovesThreshold)
                this.Notify(drop, popularity);
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
        if (this.cache.TryGetValue(drop.ItemId, out var entry) && !entry.IsExpired(config.CacheTtlHours))
            return entry.Popularity;

        var ecItem = await source.ResolveEcItemAsync(drop.Slot, drop.Name, drop.ItemId, CancellationToken.None)
            .ConfigureAwait(false);

        var popularity = ecItem is null
            ? new GlamPopularity(0, null)
            : await source.GetTopPopularityAsync(drop.Slot, ecItem.EcId, CancellationToken.None).ConfigureAwait(false);

        this.cache[drop.ItemId] = new CacheEntry(popularity, DateTime.UtcNow);
        return popularity;
    }

    private void Notify(DropItem drop, GlamPopularity popularity)
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
