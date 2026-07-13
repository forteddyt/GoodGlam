using FluentAssertions;
using GoodGlam.Glam;
using GoodGlam.Loot;
using Xunit;

namespace GoodGlam.Tests.Loot;

public class DropContextProviderTests
{
    private static readonly DateTimeOffset DroppedAt = new(2026, 7, 12, 21, 19, 32, TimeSpan.Zero);

    [Fact]
    public void Capture_combines_the_item_with_detection_time_and_duty()
    {
        var item = new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands);
        var provider = new DropContextProvider(
            new FixedTimeProvider(DroppedAt),
            new StubDutyNameProvider("The Aurum Vale"));

        var drop = provider.Capture(item);

        drop.Item.Should().BeSameAs(item);
        drop.DroppedAt.Should().Be(DroppedAt);
        drop.DutyName.Should().Be("The Aurum Vale");
    }

    [Fact]
    public void Capture_preserves_a_missing_duty_for_the_ui_fallback()
    {
        var provider = new DropContextProvider(
            new FixedTimeProvider(DroppedAt),
            new StubDutyNameProvider(null));

        var drop = provider.Capture(new DropItem(3610, "Cavalry Gauntlets", GlamSlot.Hands));

        drop.DutyName.Should().BeNull();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubDutyNameProvider(string? dutyName) : IDutyNameProvider
    {
        public string? GetCurrentDutyName() => dutyName;
    }
}
