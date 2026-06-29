using Dalamud.Interface.ImGuiNotification;
using GoodGlam.Glam;

namespace GoodGlam.History;

/// <summary>
/// Replaces the old transient toast. Every qualifying drop is appended to the persistent
/// history, and a single bell-style notification is raised that stays put until dismissed and,
/// when clicked, opens the browsable history window. Failures to record never block the alert.
/// </summary>
public sealed class HistoryNotifier(NotificationHistoryStore store, Action openHistory) : INotifier
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

        var label = popularity.TopGlamName is null ? "a popular glamour" : $"\"{popularity.TopGlamName}\"";
        var content = $"{drop.Name} is used in {label} ({popularity.TopLoves} loves). Click to view history.";

        // Notifications must be raised on the framework thread.
        _ = Services.Framework.RunOnFrameworkThread(() =>
        {
            var active = Services.Notifications.AddNotification(new Notification
            {
                Title = "GoodGlam",
                Content = content,
                Type = NotificationType.Info,
                // Persist until the user dismisses it (bell-style) rather than auto-vanishing.
                InitialDuration = TimeSpan.MaxValue,
                MinimizedText = $"{drop.Name} — {popularity.TopLoves} loves",
            });

            active.Click += _ =>
            {
                openHistory();
                active.DismissNow();
            };
        });
    }
}
