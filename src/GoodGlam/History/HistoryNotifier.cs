using GoodGlam.Glam;

namespace GoodGlam.History;

/// <summary>
/// Replaces the old transient toast and the later bell notification. Every qualifying drop is
/// appended to the persistent history, then a one-way <see cref="NotificationState"/> signal is
/// raised so the floating logo lights up with its golden glow until the user opens the history
/// window. Failures to record never block the signal.
/// </summary>
public sealed class HistoryNotifier(NotificationHistoryStore store, NotificationState notificationState) : INotifier
{
    public void NotifyPopular(DropItem drop, GlamPopularity popularity)
    {
        store.Add(new PopularDropRecord(
            drop.ItemId,
            drop.Name,
            drop.Slot.Key,
            popularity.TopLoves,
            popularity.TopGlamName,
            popularity.TopGlamUrl,
            DateTimeOffset.Now,
            popularity.ListingUrl));

        // Light up the floating logo's glow; it stays lit until the history window is opened.
        notificationState.Raise();
    }
}
