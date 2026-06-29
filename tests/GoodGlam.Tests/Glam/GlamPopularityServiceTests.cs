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
        var source = new FakeGlamSource { Popularity = new GlamPopularity(150, "u") };
        var service = new GlamPopularityService(new Configuration { LovesThreshold = 100 }, source, notifier);

        var result = await service.ProcessAsync(Drop());

        result.TopLoves.Should().Be(150);
        notifier.Count.Should().Be(1);
    }

    [Fact]
    public async Task Does_not_notify_below_threshold()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Popularity = new GlamPopularity(99, "u") };
        await new GlamPopularityService(new Configuration { LovesThreshold = 100 }, source, notifier)
            .ProcessAsync(Drop());
        notifier.Count.Should().Be(0);
    }

    [Fact]
    public async Task Returns_empty_and_silent_on_error()
    {
        var notifier = new FakeNotifier();
        var source = new FakeGlamSource { Throw = new InvalidOperationException("boom") };
        var result = await new GlamPopularityService(new Configuration(), source, notifier).ProcessAsync(Drop());

        result.TopLoves.Should().Be(0);
        notifier.Count.Should().Be(0);
    }

    [Fact]
    public async Task Caches_result_so_source_is_hit_once()
    {
        var source = new FakeGlamSource { Popularity = new GlamPopularity(150, "u") };
        var service = new GlamPopularityService(new Configuration { CacheTtlHours = 12 }, source, new FakeNotifier());

        await service.ProcessAsync(Drop());
        await service.ProcessAsync(Drop());

        source.ResolveCalls.Should().Be(1);
        source.PopularityCalls.Should().Be(1);
    }

    [Fact]
    public async Task Non_positive_ttl_clamps_so_entry_is_not_instantly_expired()
    {
        var source = new FakeGlamSource { Popularity = new GlamPopularity(150, "u") };
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
}
