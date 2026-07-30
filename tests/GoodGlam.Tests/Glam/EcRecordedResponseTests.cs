using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

/// <summary>
/// Replays real, unedited Eorzea Collection responses (see <c>Fixtures/Ec/README.md</c>) through the
/// actual <see cref="EorzeaCollectionClient"/> and <see cref="GlamPopularityService"/> - the same
/// orchestration <c>/goodglam check</c> runs below the Lumina resolve step.
///
/// These replaced a live suite that drove EC over the network. The live tests were deleted because
/// EC's Cloudflare edge challenges egress IPs unpredictably, which made every pull request hostage
/// to a third party's bot-management mood rather than to the correctness of this code.
///
/// The trade is deliberate and worth stating plainly: recorded responses cannot notice **EC changing
/// its own JSON shape or HTML structure**, which is the one thing the live suite was uniquely good
/// at. What they do give - and what the live suite could not, because it only ran when EC felt like
/// answering - is coverage of our parsing that runs deterministically on every change. Realism is
/// preserved by replaying markup nobody here wrote: the fixtures are captured verbatim, so an
/// assumption baked into a regex can actually be contradicted by them.
/// </summary>
public class EcRecordedResponseTests
{
    /// <summary>The EC item ID that <c>Scion Adventurer's Jacket</c> (game item 17492) bridges to.</summary>
    private const int ScionJacketEcId = 8912;

    private const uint ScionJacketGameItemId = 17492;

    private const string ScionJacketName = "Scion Adventurer's Jacket";

    public EcRecordedResponseTests() => TestServices.EnsureLog();

    [Fact]
    public async Task Bridges_a_game_item_to_its_ec_id_from_a_recorded_search()
    {
        var transport = EcFixtures.ReplayTransport();

        var item = await new EorzeaCollectionClient(transport)
            .ResolveEcItemAsync(GlamSlot.Body, ScionJacketName, ScionJacketGameItemId, CancellationToken.None);

        item.Should().NotBeNull();
        item!.EcId.Should().Be(ScionJacketEcId);
        item.XivApiId.Should().Be(ScionJacketGameItemId);
        item.Name.Should().Be(ScionJacketName);
    }

    /// <summary>
    /// The match is on <c>XIVApiId</c>, not on position or name, so a search whose results don't
    /// include the game item is an ordinary "not on EC" answer rather than a wrong bridge. The
    /// recorded body is a real single-result response, so this asks for an ID it genuinely lacks.
    /// </summary>
    [Fact]
    public async Task Returns_null_when_the_recorded_search_holds_no_matching_game_item()
    {
        var transport = EcFixtures.ReplayTransport();

        var item = await new EorzeaCollectionClient(transport)
            .ResolveEcItemAsync(GlamSlot.Body, ScionJacketName, gameItemId: 99999, CancellationToken.None);

        item.Should().BeNull();
    }

    /// <summary>
    /// The scrape against real markup: loves, glamour URL, title and cover image for the winning
    /// card. This is the case hand-written HTML cannot honestly cover - the regexes were written
    /// from this markup's shape, so only markup we didn't author can contradict them.
    /// </summary>
    [Fact]
    public async Task Scrapes_ranked_glamours_from_a_recorded_listing()
    {
        var transport = EcFixtures.ReplayTransport();

        var popularity = await new EorzeaCollectionClient(transport)
            .GetPopularityAsync(GlamSlot.Body, ScionJacketEcId, new PopularityFilters(), CancellationToken.None);

        popularity.RankedGlams.Should().HaveCount(10, "the listing holds 36 cards and the client caps at 10");
        popularity.RankedGlams.Select(glam => glam.Loves).Should().BeInDescendingOrder();

        var top = popularity.Top!;
        top.Loves.Should().Be(822);
        top.Name.Should().Be("Classy Flight Attendant");
        top.Url.Should().Be("https://ffxiv.eorzeacollection.com/glamour/15250");
        top.ImageUrl.Should().Be(
            "https://glamours.eorzeacollection.com/15250/classy-flight-attendant-0-1560718351.png");

        popularity.ListingUrl.Should().Contain($"{GlamSlot.Body.FilterParam}%5D={ScionJacketEcId}");
    }

    /// <summary>
    /// Every card in the recorded listing yields all three fields. A regex that silently stopped
    /// matching most cards would still produce a plausible-looking top result, so assert the whole
    /// page parses rather than just the winner.
    /// </summary>
    [Fact]
    public async Task Parses_name_and_image_for_every_ranked_card()
    {
        var transport = EcFixtures.ReplayTransport();

        var popularity = await new EorzeaCollectionClient(transport)
            .GetPopularityAsync(GlamSlot.Body, ScionJacketEcId, new PopularityFilters(), CancellationToken.None);

        popularity.RankedGlams.Should().OnlyContain(
            glam => glam.Loves > 0
                && !string.IsNullOrWhiteSpace(glam.Name)
                && glam.Url.StartsWith("https://ffxiv.eorzeacollection.com/glamour/")
                && glam.ImageUrl!.StartsWith("https://glamours.eorzeacollection.com/"));
    }

    /// <summary>
    /// The end-to-end check flow on recorded data: bridge, scrape, then the popularity notification,
    /// driving the same <see cref="GlamPopularityService.ProcessAsync"/> the slash command uses.
    /// </summary>
    [Fact]
    public async Task Check_flow_reports_loves_and_notifies_for_a_popular_item()
    {
        var drop = new DropOccurrence(
            new DropItem(ScionJacketGameItemId, ScionJacketName, GlamSlot.Body),
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            "The Praetorium");

        var notifier = new FakeNotifier();
        var service = new GlamPopularityService(
            new Configuration { LovesThreshold = 100 },
            new EorzeaCollectionClient(EcFixtures.ReplayTransport()),
            notifier);

        var popularity = await service.ProcessAsync(drop);

        popularity.TopLoves.Should().Be(822);
        notifier.Count.Should().Be(1, "822 loves clears the configured threshold of 100");
        notifier.LastOccurrence.Should().Be(drop);
        notifier.LastPopularity!.TopLoves.Should().Be(822);
    }

    /// <summary>The same recorded data stays below a threshold set above it, so no notification fires.</summary>
    [Fact]
    public async Task Check_flow_stays_quiet_when_the_threshold_is_above_the_recorded_loves()
    {
        var drop = new DropOccurrence(
            new DropItem(ScionJacketGameItemId, ScionJacketName, GlamSlot.Body),
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            "The Praetorium");

        var notifier = new FakeNotifier();
        var service = new GlamPopularityService(
            new Configuration { LovesThreshold = 5000 },
            new EorzeaCollectionClient(EcFixtures.ReplayTransport()),
            notifier);

        var popularity = await service.ProcessAsync(drop);

        popularity.TopLoves.Should().Be(822);
        notifier.Count.Should().Be(0);
    }
}
