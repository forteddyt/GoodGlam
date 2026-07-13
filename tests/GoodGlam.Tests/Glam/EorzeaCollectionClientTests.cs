using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

public class EorzeaCollectionClientTests
{
    public EorzeaCollectionClientTests() => TestServices.EnsureLog();

    [Fact]
    public async Task ResolveEcItem_builds_slot_url_and_search_body()
    {
        var transport = new FakeTransport { PostResult = "[]" };
        var client = new EorzeaCollectionClient(transport);

        await client.ResolveEcItemAsync(GlamSlot.Hands, "Cavalry Gauntlets", 3610, CancellationToken.None);

        transport.PostUrls.Should().ContainSingle()
            .Which.Should().Be("https://ffxiv.eorzeacollection.com/gear/hands/search");
        JsonDocument.Parse(transport.PostBodies[0]).RootElement.GetProperty("search").GetString()
            .Should().Be("Cavalry Gauntlets");
    }

    [Fact]
    public async Task ResolveEcItem_matches_on_xivapi_id()
    {
        var transport = new FakeTransport
        {
            PostResult = """[{"ID":111,"Name":"Other","XIVApiId":999},{"ID":14930,"Name":"Cavalry Gauntlets","XIVApiId":3610}]""",
        };
        var client = new EorzeaCollectionClient(transport);

        var item = await client.ResolveEcItemAsync(GlamSlot.Hands, "Cavalry Gauntlets", 3610, CancellationToken.None);

        item.Should().NotBeNull();
        item!.EcId.Should().Be(14930);
        item.XivApiId.Should().Be(3610);
    }

    [Fact]
    public async Task ResolveEcItem_returns_null_when_no_id_matches()
    {
        var transport = new FakeTransport { PostResult = """[{"ID":111,"Name":"Other","XIVApiId":999}]""" };
        var item = await new EorzeaCollectionClient(transport)
            .ResolveEcItemAsync(GlamSlot.Hands, "x", 3610, CancellationToken.None);
        item.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>Cloudflare block</html>")]
    public async Task ResolveEcItem_returns_null_for_blank_or_non_json(string? body)
    {
        var item = await new EorzeaCollectionClient(new FakeTransport { PostResult = body })
            .ResolveEcItemAsync(GlamSlot.Hands, "x", 3610, CancellationToken.None);
        item.Should().BeNull();
    }

    [Fact]
    public async Task GetPopularity_builds_listing_url_with_slot_filter()
    {
        var transport = new FakeTransport { GetResult = string.Empty };
        await new EorzeaCollectionClient(transport).GetPopularityAsync(GlamSlot.Legs, 14930, new PopularityFilters(), CancellationToken.None);

        transport.GetUrls.Should().ContainSingle().Which.Should().Be(
            "https://ffxiv.eorzeacollection.com/glamours?filter%5BorderBy%5D=loves&filter%5BlegsPiece%5D=14930&page=1");
    }

    [Fact]
    public async Task GetPopularity_returns_ranked_results_sorted_descending_and_capped_at_ten()
    {
        var html = string.Concat(Enumerable.Range(1, 12).Select(id => Card(id, id * 100, $"Glam {id}")));

        var result = await new EorzeaCollectionClient(new FakeTransport { GetResult = html })
            .GetPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.RankedGlams.Should().HaveCount(10);
        result.RankedGlams.Select(glam => glam.Loves)
            .Should().Equal(1200, 1100, 1000, 900, 800, 700, 600, 500, 400, 300);
        result.TopLoves.Should().Be(1200);
        result.Top!.Name.Should().Be("Glam 12");
    }

    [Fact]
    public async Task GetPopularity_keeps_fewer_than_ten_results()
    {
        var html = Card(100, 100, "Bronze") + Card(200, 200, "Silver") + Card(300, 300, "Gold");

        var result = await new EorzeaCollectionClient(new FakeTransport { GetResult = html })
            .GetPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.RankedGlams.Should().HaveCount(3);
        result.RankedGlams.Select(glam => glam.Name).Should().Equal("Gold", "Silver", "Bronze");
    }

    [Fact]
    public async Task GetPopularity_preserves_source_order_for_tied_loves()
    {
        var html = Card(100, 555, "First") + Card(200, 555, "Second") + Card(300, 555, "Third");

        var result = await new EorzeaCollectionClient(new FakeTransport { GetResult = html })
            .GetPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.RankedGlams.Select(glam => glam.Url).Should().Equal(
            "https://ffxiv.eorzeacollection.com/glamour/100",
            "https://ffxiv.eorzeacollection.com/glamour/200",
            "https://ffxiv.eorzeacollection.com/glamour/300");
    }

    [Fact]
    public async Task GetPopularity_pairs_metadata_by_glamour_id()
    {
        const string html =
            "<span id=\"js-glamour-likes-100\">1,234</span>" +
            "<span id=\"js-glamour-likes-200\">5,678</span>" +
            "<span id=\"js-glamour-likes-300\">2,345</span>" +
            "<a href=\"/glamour/300/third\"><div><h3 class=\"content-title\">Third</h3></div></a>" +
            "<img class=\"c-glamour-grid-item-image u-inset\" src=\"https://glamours.eorzeacollection.com/300/cover-0-333.png\" loading=\"lazy\">" +
            "<a href=\"/glamour/200/winner\"><div><h3 class=\"content-title has-text-white\">Winner &amp; Co</h3></div></a>" +
            "<img class=\"c-glamour-grid-item-image u-inset\" src=\"https://glamours.eorzeacollection.com/200/cover-0-222.png\" loading=\"lazy\">" +
            "<a href=\"/glamour/100/first\"><div><h3 class=\"content-title\">First</h3></div></a>" +
            "<img class=\"c-glamour-grid-item-image u-inset\" src=\"https://glamours.eorzeacollection.com/100/cover-0-111.png\" loading=\"lazy\">";

        var transport = new FakeTransport { GetResult = html };
        var result = await new EorzeaCollectionClient(transport)
            .GetPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.RankedGlams.Select(glam => (glam.Loves, glam.Name, glam.ImageUrl)).Should().Equal(
            (5678, "Winner & Co", "https://glamours.eorzeacollection.com/200/cover-0-222.png"),
            (2345, "Third", "https://glamours.eorzeacollection.com/300/cover-0-333.png"),
            (1234, "First", "https://glamours.eorzeacollection.com/100/cover-0-111.png"));
        result.ListingUrl.Should().Be(transport.GetUrls.Single());
    }

    [Fact]
    public async Task GetPopularity_returns_empty_when_no_cards()
    {
        var transport = new FakeTransport { GetResult = "<html></html>" };
        var result = await new EorzeaCollectionClient(transport)
            .GetPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.TopLoves.Should().Be(0);
        result.Top.Should().BeNull();
        result.RankedGlams.Should().BeEmpty();
        result.ListingUrl.Should().Be(transport.GetUrls.Single());
    }

    [Fact]
    public async Task GetPopularity_appends_active_filters_to_listing_url()
    {
        var transport = new FakeTransport { GetResult = string.Empty };
        var filters = new PopularityFilters
        {
            Gender = "female",
            Job = "tanks",
            MinLevel = 50,
            ExcludeMogstation = true,
        };

        await new EorzeaCollectionClient(transport).GetPopularityAsync(GlamSlot.Legs, 14930, filters, CancellationToken.None);

        transport.GetUrls.Should().ContainSingle().Which.Should().Be(
            "https://ffxiv.eorzeacollection.com/glamours?filter%5BorderBy%5D=loves&filter%5BlegsPiece%5D=14930" +
            "&filter%5Bgender%5D=female&filter%5Bjob%5D=tanks&filter%5BminimumLvl%5D=50&filter%5BexcludeMogstation%5D=1&page=1");
    }

    [Fact]
    public async Task GetPopularity_encodes_each_race_as_array_param()
    {
        var transport = new FakeTransport { GetResult = string.Empty };
        var filters = new PopularityFilters { Races = ["miqote", "aura"] };

        await new EorzeaCollectionClient(transport).GetPopularityAsync(GlamSlot.Body, 1, filters, CancellationToken.None);

        transport.GetUrls.Single().Should()
            .Contain("filter%5Brace%5D%5B%5D=miqote").And.Contain("filter%5Brace%5D%5B%5D=aura");
    }

    [Fact]
    public void Parameterless_ctor_wires_the_default_transport_stack()
    {
        new EorzeaCollectionClient().Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveEcItem_returns_null_when_body_deserializes_to_null()
    {
        var item = await new EorzeaCollectionClient(new FakeTransport { PostResult = "null" })
            .ResolveEcItemAsync(GlamSlot.Hands, "x", 3610, CancellationToken.None);
        item.Should().BeNull();
    }

    private static string Card(int glamId, int loves, string? name = null, string? imageUrl = null)
    {
        var loveText = loves.ToString("N0", CultureInfo.InvariantCulture);
        var title = name is null
            ? string.Empty
            : $"<a href=\"/glamour/{glamId}/slug-{glamId}\"><div><h3 class=\"content-title\">{name}</h3></div></a>";
        var image = imageUrl is null
            ? string.Empty
            : $"<img class=\"c-glamour-grid-item-image u-inset\" src=\"{imageUrl}\" loading=\"lazy\">";
        return $"<span id=\"js-glamour-likes-{glamId}\">{loveText}</span>{title}{image}";
    }
}
