using GoodGlam.Diagnostics;
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
public sealed class HistoryNotifier : INotifier
{
    private readonly NotificationHistoryStore store;
    private readonly NotificationState notificationState;
    private readonly ITraceLogger<HistoryNotifier> log;

    public HistoryNotifier(
        NotificationHistoryStore store,
        NotificationState notificationState,
        ITraceLogger<HistoryNotifier>? log = null)
    {
        this.store = store;
        this.notificationState = notificationState;
        this.log = log ?? new TraceLogger<HistoryNotifier>();
    }

    public INotificationTarget CaptureTarget()
        => new CapturedTarget(this.store, this.store.CaptureBinding(), this.notificationState, this.log);

    private sealed class CapturedTarget(
        NotificationHistoryStore store,
        NotificationHistoryStore.Binding binding,
        NotificationState notificationState,
        ITraceLogger<HistoryNotifier> log) : INotificationTarget
    {
        public void NotifyPopular(DropOccurrence drop, GlamPopularity popularity)
        {
            var landedOnActiveCharacter = store.AddTo(binding, new PopularDropRecord(
                drop.ItemId,
                drop.Name,
                drop.Slot.Key,
                popularity.RankedGlams,
                drop.DroppedAt,
                drop.DutyName,
                popularity.ListingUrl));

            // Only light the active character's logo; a drop recorded for a since-switched character
            // must not glow on whoever is logged in now.
            if (landedOnActiveCharacter)
            {
                log.Information(
                    $"recorded popular {drop.Name} ({popularity.TopLoves} loves) for the active character; raising logo glow.");
                notificationState.Raise();
            }
            else
            {
                log.Debug(
                    $"recorded popular {drop.Name} against a since-switched (or detached) character; glow not raised.");
            }
        }
    }
}
