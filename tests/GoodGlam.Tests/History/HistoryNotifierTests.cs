using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using Xunit;

namespace GoodGlam.Tests.History;

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

    private static DropOccurrence Drop() => new(
        new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands),
        new DateTimeOffset(2026, 7, 12, 21, 19, 32, TimeSpan.Zero),
        "The Aurum Vale");

    [Fact]
    public void Maps_the_ranked_result_onto_the_record()
    {
        var popularity = new GlamPopularity(
        [
            new GlamResult(250, "https://ec/glamour/200", "Nirvana", "https://glamours.ec/200/cover-0-9.png"),
            new GlamResult(180, "https://ec/glamour/100", "Runner Up"),
        ],
        "https://ec/glamours?filter=1");

        this.Notifier().NotifyPopular(Drop(), popularity);

        var record = this.store.Snapshot().Should().ContainSingle().Subject;
        record.ItemId.Should().Be(3610);
        record.ItemName.Should().Be("Cavalry Gauntlets");
        record.Slot.Should().Be("hands");
        record.RankedGlams.Should().Equal(popularity.RankedGlams);
        record.SelectedIndex.Should().Be(0);
        record.ClampedSelectedIndex.Should().Be(0);
        record.SelectedGlam.Should().Be(popularity.Top);
        record.Loves.Should().Be(250);
        record.GlamName.Should().Be("Nirvana");
        record.GlamUrl.Should().Be("https://ec/glamour/200");
        record.GlamImageUrl.Should().Be("https://glamours.ec/200/cover-0-9.png");
        record.ListingUrl.Should().Be("https://ec/glamours?filter=1");
        record.RowId.Should().NotBe(Guid.Empty);
        record.DroppedAt.Should().Be(new DateTimeOffset(2026, 7, 12, 21, 19, 32, TimeSpan.Zero));
        record.DutyName.Should().Be("The Aurum Vale");
    }

    [Fact]
    public void Persists_an_empty_ranked_result_safely()
    {
        this.Notifier().NotifyPopular(Drop(), new GlamPopularity());

        var record = this.store.Snapshot().Single();
        record.RankedGlams.Should().BeEmpty();
        record.SelectedGlam.Should().BeNull();
        record.Loves.Should().Be(0);
        record.GlamName.Should().BeNull();
        record.GlamUrl.Should().BeNull();
        record.GlamImageUrl.Should().BeNull();
        record.ListingUrl.Should().BeNull();
        record.DroppedAt.Should().Be(new DateTimeOffset(2026, 7, 12, 21, 19, 32, TimeSpan.Zero));
        record.DutyName.Should().Be("The Aurum Vale");
    }

    [Fact]
    public void Raises_the_notification_state_so_the_logo_glows()
    {
        this.notificationState.HasUnseen.Should().BeFalse();

        this.Notifier().NotifyPopular(Drop(), new GlamPopularity([new GlamResult(250, "u", "Nirvana")], "list"));

        this.notificationState.HasUnseen.Should().BeTrue();
    }

    public void Dispose()
    {
        if (File.Exists(this.path))
            File.Delete(this.path);
    }
}
