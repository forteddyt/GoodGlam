using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using Xunit;

namespace GoodGlam.Tests.History;

/// <summary>
/// Exercises <see cref="HistoryNotifier"/>: every qualifying drop is appended to the persistent
/// store and the shared <see cref="NotificationState"/> is raised so the floating logo lights up.
/// The notifier no longer touches any Dalamud service (the old bell is gone), so these run without
/// a framework.
/// </summary>
public class HistoryNotifierTests : IDisposable
{
    private readonly string path;
    private readonly NotificationHistoryStore store;
    private readonly NotificationState notificationState = new();

    public HistoryNotifierTests()
    {
        TestServices.EnsureLog();
        this.path = Path.Combine(Path.GetTempPath(), $"goodglam-notifier-{Guid.NewGuid():N}.json");
        this.store = new NotificationHistoryStore(this.path);
    }

    private INotificationTarget Notifier() => new HistoryNotifier(this.store, this.notificationState).CaptureTarget();

    private static DropItem Drop() => new(3610, "Cavalry Gauntlets", GlamSlot.Hands);

    [Fact]
    public void Maps_every_field_onto_the_record()
    {
        var popularity = new GlamPopularity(
            250, "https://ec/glamour/200", "Nirvana", "https://ec/glamours?filter=1",
            "https://glamours.ec/200/cover-0-9.png");

        this.Notifier().NotifyPopular(Drop(), popularity);

        var record = this.store.Snapshot().Should().ContainSingle().Subject;
        record.ItemId.Should().Be(3610);
        record.ItemName.Should().Be("Cavalry Gauntlets");
        record.Slot.Should().Be("hands");
        record.Loves.Should().Be(250);
        record.GlamName.Should().Be("Nirvana");
        record.GlamUrl.Should().Be("https://ec/glamour/200");
        record.ListingUrl.Should().Be("https://ec/glamours?filter=1");
        record.GlamImageUrl.Should().Be("https://glamours.ec/200/cover-0-9.png");
        record.Timestamp.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Persists_null_glam_and_listing_fields()
    {
        this.Notifier().NotifyPopular(Drop(), new GlamPopularity(120, null));

        var record = this.store.Snapshot().Single();
        record.GlamName.Should().BeNull();
        record.GlamUrl.Should().BeNull();
        record.ListingUrl.Should().BeNull();
        record.GlamImageUrl.Should().BeNull();
    }

    [Fact]
    public void Raises_the_notification_state_so_the_logo_glows()
    {
        this.notificationState.HasUnseen.Should().BeFalse();

        this.Notifier().NotifyPopular(Drop(), new GlamPopularity(250, "u", "Nirvana", "list"));

        this.notificationState.HasUnseen.Should().BeTrue();
    }

    public void Dispose()
    {
        if (File.Exists(this.path))
            File.Delete(this.path);
    }
}
