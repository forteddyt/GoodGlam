using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.ImGuiNotification.EventArgs;
using Dalamud.Plugin.Services;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.History;
using Xunit;

namespace GoodGlam.Tests.History;

/// <summary>
/// Exercises <see cref="HistoryNotifier"/> with FakeItEasy fakes of the Dalamud services it
/// touches (injected into the static <c>Services</c> holder), so the record-write and bell-raise
/// behaviour can be asserted without a running framework.
/// </summary>
public class HistoryNotifierTests : IDisposable
{
    private readonly string path;
    private readonly NotificationHistoryStore store;
    private readonly IFramework framework = A.Fake<IFramework>();
    private readonly INotificationManager notifications = A.Fake<INotificationManager>();
    private readonly IActiveNotification active = A.Fake<IActiveNotification>();
    private int openHistoryCalls;

    public HistoryNotifierTests()
    {
        TestServices.EnsureLog();

        // Run the "framework thread" work inline so the bell-raising lambda actually executes.
        A.CallTo(() => this.framework.RunOnFrameworkThread(A<Action>._))
            .Invokes((Action a) => a())
            .Returns(Task.CompletedTask);
        A.CallTo(() => this.notifications.AddNotification(A<Notification>._)).Returns(this.active);

        TestServices.Install<IFramework>("Framework", this.framework);
        TestServices.Install<INotificationManager>("Notifications", this.notifications);

        this.path = Path.Combine(Path.GetTempPath(), $"goodglam-notifier-{Guid.NewGuid():N}.json");
        this.store = new NotificationHistoryStore(this.path);
    }

    private HistoryNotifier Notifier() => new(this.store, () => this.openHistoryCalls++);

    private static DropItem Drop() => new(3610, "Cavalry Gauntlets", GlamSlot.Hands);

    [Fact]
    public void Maps_every_field_onto_the_record()
    {
        var popularity = new GlamPopularity(250, "https://ec/glamour/200", "Nirvana", "https://ec/glamours?filter=1");

        this.Notifier().NotifyPopular(Drop(), popularity);

        var record = this.store.Snapshot().Should().ContainSingle().Subject;
        record.ItemId.Should().Be(3610);
        record.ItemName.Should().Be("Cavalry Gauntlets");
        record.Slot.Should().Be("hands");
        record.Loves.Should().Be(250);
        record.GlamName.Should().Be("Nirvana");
        record.GlamUrl.Should().Be("https://ec/glamour/200");
        record.ListingUrl.Should().Be("https://ec/glamours?filter=1");
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
    }

    [Fact]
    public void Raises_exactly_one_notification_dispatched_on_framework_thread()
    {
        this.Notifier().NotifyPopular(Drop(), new GlamPopularity(250, "u", "Nirvana", "list"));

        A.CallTo(() => this.framework.RunOnFrameworkThread(A<Action>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => this.notifications.AddNotification(A<Notification>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Notification_uses_glam_name_in_content_and_persists_until_dismissed()
    {
        Notification? captured = null;
        A.CallTo(() => this.notifications.AddNotification(A<Notification>._))
            .Invokes((Notification n) => captured = n)
            .Returns(this.active);

        this.Notifier().NotifyPopular(Drop(), new GlamPopularity(250, "u", "Nirvana", "list"));

        captured.Should().NotBeNull();
        captured!.Title.Should().Be("GoodGlam");
        captured.Content.Should().Contain("Cavalry Gauntlets").And.Contain("\"Nirvana\"").And.Contain("250");
        captured.MinimizedText.Should().Contain("Cavalry Gauntlets").And.Contain("250");
        captured.InitialDuration.Should().Be(TimeSpan.MaxValue);
    }

    [Fact]
    public void Notification_content_falls_back_when_glam_name_is_null()
    {
        Notification? captured = null;
        A.CallTo(() => this.notifications.AddNotification(A<Notification>._))
            .Invokes((Notification n) => captured = n)
            .Returns(this.active);

        this.Notifier().NotifyPopular(Drop(), new GlamPopularity(120, null));

        captured!.Content.Should().Contain("a popular glamour").And.NotContain("\"");
    }

    public void Dispose()
    {
        if (File.Exists(this.path))
            File.Delete(this.path);
    }
}
