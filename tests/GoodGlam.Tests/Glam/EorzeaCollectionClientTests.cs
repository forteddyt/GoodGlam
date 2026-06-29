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
    public async Task GetTopPopularity_returns_empty_when_no_cards()
    {
        var result = await new EorzeaCollectionClient(new FakeTransport { GetResult = "<html></html>" })
            .GetTopPopularityAsync(GlamSlot.Body, 1, new PopularityFilters(), CancellationToken.None);

        result.TopLoves.Should().Be(0);
        result.TopGlamUrl.Should().BeNull();
    }
}
