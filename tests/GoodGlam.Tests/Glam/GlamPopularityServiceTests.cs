using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

public class GlamPopularityServiceTests
{
    public GlamPopularityServiceTests() => TestServices.EnsureLog();

    private static DropItem Drop() => new(3610, "Cavalry Gauntlets", GlamSlot.Hands);

    [Fact]
    public async Task Notifies_when_loves_meet_threshold()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };
        var service = new GlamPopularityService(new Configuration { LovesThreshold = 100 }, source, notifier);

        var result = await service.ProcessAsync(Drop());

        result.TopLoves.Should().Be(150);
        notifier.Count.Should().Be(1);
    }

    [Fact]
    public async Task Forwards_full_ranked_popularity_to_notifier()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource
        {
            Popularity = new GlamPopularity(
            [
                new GlamResult(150, "u1", "Nirvana", "https://glamours/u1.png"),
                new GlamResult(120, "u2", "Runner Up"),
            ],
            "list"),
        };

        await new GlamPopularityService(new Configuration { LovesThreshold = 100 }, source, notifier)
            .ProcessAsync(Drop());

        notifier.LastPopularity.Should().NotBeNull();
        notifier.LastPopularity!.RankedGlams.Select(glam => glam.Name).Should().Equal("Nirvana", "Runner Up");
        notifier.LastPopularity.ListingUrl.Should().Be("list");
    }

    [Fact]
    public async Task Does_not_notify_below_threshold()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(99, "u")]) };
        await new GlamPopularityService(new Configuration { LovesThreshold = 100 }, source, notifier)
            .ProcessAsync(Drop());
        notifier.Count.Should().Be(0);
    }

    [Fact]
    public async Task Uses_the_per_slot_threshold_when_advanced_mode_is_on()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };
        var config = new Configuration { LovesThreshold = 1000, PerSlotThresholds = true };
        config.Slots[GlamSlot.Hands.Key] = new SlotSetting { LovesThreshold = 50 };

        await new GlamPopularityService(config, source, notifier).ProcessAsync(Drop());

        notifier.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_high_per_slot_threshold_suppresses_a_drop_the_master_would_pass()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };
        var config = new Configuration { LovesThreshold = 10, PerSlotThresholds = true };
        config.Slots[GlamSlot.Hands.Key] = new SlotSetting { LovesThreshold = 200 };

        await new GlamPopularityService(config, source, notifier).ProcessAsync(Drop());

        notifier.Count.Should().Be(0);
    }

    [Fact]
    public async Task Ignores_the_per_slot_override_while_advanced_mode_is_off()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };
        var config = new Configuration { LovesThreshold = 200, PerSlotThresholds = false };
        config.Slots[GlamSlot.Hands.Key] = new SlotSetting { LovesThreshold = 5 };

        await new GlamPopularityService(config, source, notifier).ProcessAsync(Drop());

        notifier.Count.Should().Be(0);
    }

    [Fact]
    public async Task Returns_empty_and_silent_on_error()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Throw = new InvalidOperationException("boom") };
        var result = await new GlamPopularityService(new Configuration(), source, notifier).ProcessAsync(Drop());

        result.TopLoves.Should().Be(0);
        result.Top.Should().BeNull();
        notifier.Count.Should().Be(0);
    }

    [Fact]
    public async Task Caches_result_so_source_is_hit_once()
    {
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };
        var service = new GlamPopularityService(new Configuration { CacheTtlHours = 12 }, source, new FakeNotifier());

        await service.ProcessAsync(Drop());
        await service.ProcessAsync(Drop());

        source.ResolveCalls.Should().Be(1);
        source.PopularityCalls.Should().Be(1);
    }

    [Fact]
    public async Task Non_positive_ttl_clamps_so_entry_is_not_instantly_expired()
    {
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };
        var service = new GlamPopularityService(new Configuration { CacheTtlHours = 0 }, source, new FakeNotifier());

        await service.ProcessAsync(Drop());
        await service.ProcessAsync(Drop());

        source.ResolveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Skips_popularity_when_item_not_on_ec()
    {
        var source = new FakeGlamSource { EcItem = null };
        var result = await new GlamPopularityService(new Configuration(), source, new FakeNotifier()).ProcessAsync(Drop());

        result.TopLoves.Should().Be(0);
        source.PopularityCalls.Should().Be(0);
    }

    [Fact]
    public async Task Passes_configured_filters_to_source()
    {
        var config = new Configuration { Filters = new PopularityFilters { Gender = "female" } };
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };

        await new GlamPopularityService(config, source, new FakeNotifier()).ProcessAsync(Drop());

        source.LastFilters.Should().BeSameAs(config.Filters);
    }

    [Fact]
    public async Task Distinct_filters_are_cached_separately()
    {
        var config = new Configuration();
        var source = new FakeGlamSource { Popularity = new GlamPopularity([new GlamResult(150, "u")]) };
        var service = new GlamPopularityService(config, source, new FakeNotifier());

        await service.ProcessAsync(Drop());
        config.Filters = new PopularityFilters { Gender = "female" };
        await service.ProcessAsync(Drop());

        source.PopularityCalls.Should().Be(2);
    }
}
