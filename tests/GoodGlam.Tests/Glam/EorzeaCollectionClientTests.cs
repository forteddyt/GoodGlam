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
    public async Task GetTopPopularity_builds_listing_url_with_slot_filter()
    {
        var transport = new FakeTransport { GetResult = "" };
        await new EorzeaCollectionClient(transport).GetTopPopularityAsync(GlamSlot.Legs, 14930, new PopularityFilters(), CancellationToken.None);

        transport.GetUrls.Should().ContainSingle().Which.Should().Be(
            "https://ffxiv.eorzeacollection.com/glamours?filter%5BorderBy%5D=loves&filter%5BlegsPiece%5D=14930&page=1");
    }

    [Fact]
    public async Task GetTopPopularity_picks_max_loves_and_builds_glam_url()
    {
        const string html =
            "<span id=\"js-glamour-likes-100\">1,234</span><span id=\"js-glamour-likes-200\">5,678</span>";
        var result = await new EorzeaCollectionClient(new FakeTransport { GetResult = html })
            .GetTopPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.TopLoves.Should().Be(5678);
        result.TopGlamUrl.Should().Be("https://ffxiv.eorzeacollection.com/glamour/200");
    }

    [Fact]
    public async Task GetTopPopularity_captures_name_of_winning_glamour()
    {
        const string html =
            "<span id=\"js-glamour-likes-100\">1,234</span>" +
            "<a href=\"/glamour/100/loser\"><div><h3 class=\"content-title\">Loser</h3></div></a>" +
            "<span id=\"js-glamour-likes-200\">5,678</span>" +
            "<a href=\"/glamour/200/nirvana\"><div><h3 class=\"content-title has-text-white\">Nirvana &amp; Co</h3></div></a>";
        var transport = new FakeTransport { GetResult = html };
        var result = await new EorzeaCollectionClient(transport)
            .GetTopPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.TopLoves.Should().Be(5678);
        result.TopGlamUrl.Should().Be("https://ffxiv.eorzeacollection.com/glamour/200");
        result.TopGlamName.Should().Be("Nirvana & Co");
        result.ListingUrl.Should().Be(transport.GetUrls.Single());
    }

    [Fact]
    public async Task GetTopPopularity_returns_empty_when_no_cards()
    {
        var transport = new FakeTransport { GetResult = "<html></html>" };
        var result = await new EorzeaCollectionClient(transport)
            .GetTopPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.TopLoves.Should().Be(0);
        result.TopGlamUrl.Should().BeNull();
        result.TopGlamName.Should().BeNull();
        result.ListingUrl.Should().Be(transport.GetUrls.Single());
    }

    [Fact]
    public async Task GetTopPopularity_name_null_when_winner_has_no_card_title()
    {
        // The winning glamour (id 200) has a loves span but no matching title link on the page;
        // a different card does. Name should be null while url + listing are still populated.
        const string html =
            "<span id=\"js-glamour-likes-200\">5,678</span>" +
            "<a href=\"/glamour/100/other\"><div><h3 class=\"content-title\">Other</h3></div></a>";
        var transport = new FakeTransport { GetResult = html };
        var result = await new EorzeaCollectionClient(transport)
            .GetTopPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.TopLoves.Should().Be(5678);
        result.TopGlamUrl.Should().Be("https://ffxiv.eorzeacollection.com/glamour/200");
        result.TopGlamName.Should().BeNull();
        result.ListingUrl.Should().Be(transport.GetUrls.Single());
    }

    [Fact]
    public async Task GetTopPopularity_appends_active_filters_to_listing_url()
    {
        var transport = new FakeTransport { GetResult = "" };
        var filters = new PopularityFilters
        {
            Gender = "female",
            Job = "tanks",
            MinLevel = 50,
            ExcludeMogstation = true,
        };

        await new EorzeaCollectionClient(transport).GetTopPopularityAsync(GlamSlot.Legs, 14930, filters, CancellationToken.None);

        transport.GetUrls.Should().ContainSingle().Which.Should().Be(
            "https://ffxiv.eorzeacollection.com/glamours?filter%5BorderBy%5D=loves&filter%5BlegsPiece%5D=14930" +
            "&filter%5Bgender%5D=female&filter%5Bjob%5D=tanks&filter%5BminimumLvl%5D=50&filter%5BexcludeMogstation%5D=1&page=1");
    }

    [Fact]
    public void Parameterless_ctor_wires_the_default_transport_stack()
    {
        // Exercises the production constructor (EcTransportFactory.Create()); no network is touched.
        new EorzeaCollectionClient().Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveEcItem_returns_null_when_body_deserializes_to_null()
    {
        // A literal "null" JSON body deserializes to a null list — treated as no match.
        var item = await new EorzeaCollectionClient(new FakeTransport { PostResult = "null" })
            .ResolveEcItemAsync(GlamSlot.Hands, "x", 3610, CancellationToken.None);
        item.Should().BeNull();
    }
}
