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

    private static DropItem Drop() => new(3610, "Cavalry Gauntlets", GlamSlot.Hands);

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
        record.ListingUrl.Should().Be("https://ec/glamours?filter=1");
        record.RowId.Should().NotBe(Guid.Empty);
        record.Timestamp.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Persists_an_empty_ranked_result_safely()
    {
        this.Notifier().NotifyPopular(Drop(), new GlamPopularity());

        var record = this.store.Snapshot().Single();
        record.RankedGlams.Should().BeEmpty();
        record.SelectedGlam.Should().BeNull();
        record.ListingUrl.Should().BeNull();
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
