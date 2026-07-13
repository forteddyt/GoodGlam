using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.IntegrationTests.Glam;

/// <summary>
/// End-to-end coverage of the live half of the GoodGlam pipeline against real Eorzea Collection,
/// reusing the same orchestration the <c>/goodglam check</c> command runs after item resolution:
/// <c>EC bridge (game ID -&gt; EC ID) -&gt; popularity (loves) -&gt; notify (threshold)</c>.
///
/// The Lumina item-resolution step (<see cref="ItemResolver"/>) needs FFXIV game data and so is
/// verified manually in-game (see the wiki: Development); these tests start one seam below it,
/// from a known game item ID, which is the realistically CI-automatable portion.
/// </summary>
[Collection(LiveEc.Collection)]
public sealed class EcPipelineTests
{
    /// <summary>
    /// The EC bridge: a known game item ID resolves to the expected EC item ID and name via the
    /// real search endpoint, across several slots (guards slot-specific URL/bridge regressions).
    /// </summary>
    [Theory]
    [MemberData(nameof(EcFixtures.KnownItems), MemberType = typeof(EcFixtures))]
    public async Task Resolves_known_game_item_to_expected_ec_id(string name, string slotKey, uint gameItemId, int expectedEcId)
    {
        var client = new EorzeaCollectionClient();
        var slot = new GlamSlot(slotKey);

        var item = await LiveEc.RetryAsync(
            ct => client.ResolveEcItemAsync(slot, name, gameItemId, ct),
            result => result is not null);

        item.Should().NotBeNull(
            "the EC search for '{0}' (game id {1}) should bridge to an EC item", name, gameItemId);
        item!.EcId.Should().Be(expectedEcId);
        item.XivApiId.Should().Be(gameItemId);
        item.Name.Should().Be(name);
    }

    /// <summary>
    /// The full live half via the check-flow seam: drive a known, reliably-popular drop through the
    /// real <see cref="GlamPopularityService.ProcessAsync"/> (the same call <c>/goodglam check</c>
    /// makes) and assert it bridges to EC, finds loves, and fires the popularity notification.
    /// </summary>
    [Fact]
    public async Task Check_flow_reports_loves_and_notifies_for_popular_item()
    {
        var item = EcFixtures.ScionJacket;
        var drop = new DropItem(item.GameItemId, item.Name, item.Slot);

        CapturingNotifier notifier = null!;
        var popularity = await LiveEc.RetryAsync(
            async ct =>
            {
                // Build a fresh service per attempt so a transient empty result isn't cached and
                // poisoning the retry (GlamPopularityService caches whatever it computes per item).
                notifier = new CapturingNotifier();
                var config = new Configuration { LovesThreshold = 1 };
                var service = new GlamPopularityService(config, new EorzeaCollectionClient(), notifier);
                return await service.ProcessAsync(drop);
            },
            result => result.TopLoves > 0);

        popularity.TopLoves.Should().BeGreaterThan(0,
            "'{0}' is a well-loved glamour piece on EC", item.Name);
        popularity.Top!.Url.Should().StartWith("https://ffxiv.eorzeacollection.com/glamour/");

        notifier.Count.Should().Be(1, "the loves count clears the threshold, so the drop is popular");
        notifier.LastDrop.Should().Be(drop);
        notifier.LastPopularity!.TopLoves.Should().Be(popularity.TopLoves);
    }

    /// <summary>
    /// The popularity scrape end-to-end: the listing for a known EC piece yields a top glamour with
    /// loves, a glamour URL, a parsed title, and its cover-image URL — exercising the live HTML
    /// scraping (loves + name + image regexes) the unit tests can only cover against canned markup.
    /// </summary>
    [Fact]
    public async Task Ranked_popularity_scrapes_ordered_loves_url_name_and_image_for_known_piece()
    {
        var item = EcFixtures.ScionJacket;
        var client = new EorzeaCollectionClient();

        var popularity = await LiveEc.RetryAsync(
            ct => client.GetPopularityAsync(item.Slot, item.EcId, new PopularityFilters(), ct),
            result => result.TopLoves > 0);

        popularity.TopLoves.Should().BeGreaterThan(0);
        popularity.RankedGlams.Should().NotBeEmpty();
        popularity.RankedGlams.Should().HaveCountLessThanOrEqualTo(10);
        popularity.RankedGlams.Select(glam => glam.Loves)
            .Should().BeInDescendingOrder();
        popularity.Top!.Url.Should().StartWith("https://ffxiv.eorzeacollection.com/glamour/");
        popularity.Top.Name.Should().NotBeNullOrWhiteSpace();
        popularity.Top.ImageUrl.Should().StartWith("https://glamours.eorzeacollection.com/",
            "the winning card's cover image is scraped from the listing so the History tab can preview it");
        popularity.ListingUrl.Should().Contain($"{item.Slot.FilterParam}%5D={item.EcId}");
    }
}
