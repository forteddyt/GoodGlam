using GoodGlam.Glam;

namespace GoodGlam.History;

/// <summary>
/// Replaces the old transient toast and the later bell notification. Every qualifying drop is
/// appended to the persistent history, then a one-way <see cref="NotificationState"/> signal is
/// raised so the floating logo lights up with its golden glow until the user opens the history
/// window. Failures to record never block the signal.
///
/// The history target is captured at dispatch time (see <see cref="CaptureTarget"/>) so a lookup
/// that completes after a character switch is recorded against the character that actually saw the
/// drop — and the glow is only raised when that character is still the active one.
/// </summary>
public sealed class HistoryNotifier(NotificationHistoryStore store, NotificationState notificationState) : INotifier
{
    public INotificationTarget CaptureTarget()
        => new CapturedTarget(store, store.CaptureBinding(), notificationState);

    private sealed class CapturedTarget(
        NotificationHistoryStore store,
        NotificationHistoryStore.Binding binding,
        NotificationState notificationState) : INotificationTarget
    {
        public void NotifyPopular(DropItem drop, GlamPopularity popularity)
        {
            var landedOnActiveCharacter = store.AddTo(binding, new PopularDropRecord(
                drop.ItemId,
                drop.Name,
                drop.Slot.Key,
                popularity.TopLoves,
                popularity.TopGlamName,
                popularity.TopGlamUrl,
                DateTimeOffset.Now,
                popularity.ListingUrl));

            // Only light the active character's logo; a drop recorded for a since-switched character
            // must not glow on whoever is logged in now.
            if (landedOnActiveCharacter)
                notificationState.Raise();
        }
    }
}
