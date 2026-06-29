using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoodGlam.Glam;

/// <summary>An Eorzea Collection item record, bridging the game item ID to EC's own filter ID.</summary>
public sealed record EcItem(int EcId, string Name, long XivApiId);

/// <summary>The most-loved glamour found for a given item, used to judge popularity.</summary>
public sealed record GlamPopularity(int TopLoves, string? TopGlamUrl, string? TopGlamName = null, string? ListingUrl = null);

/// <summary>
/// Abstraction over the glamour data source so the live scraper can be swapped for a
/// hosted proxy or a prebuilt index later without touching the rest of the plugin.
/// </summary>
public interface IGlamSource
{
    Task<EcItem?> ResolveEcItemAsync(GlamSlot slot, string itemName, uint gameItemId, CancellationToken ct);

    Task<GlamPopularity> GetTopPopularityAsync(GlamSlot slot, int ecId, PopularityFilters filters, CancellationToken ct);
}

/// <summary>
/// Live Eorzea Collection client. EorzeaCollection has no public API, so two endpoints are
/// scraped:
///   * POST /gear/{slot}/search  -> JSON, maps game item ID (XIVApiId) -> EC ID.
///   * GET  /glamours?filter[..]  -> HTML listing we scrape for the top "loves" count.
///
/// The actual HTTP is delegated to an <see cref="IEcTransport"/> chosen for the current
/// platform (in-process HttpClient under Wine, the curl.exe subprocess on native Windows),
/// because Cloudflare's WAF blocks managed HTTP only on native Windows. See
/// <see cref="EcTransportFactory"/>.
/// </summary>
public sealed partial class EorzeaCollectionClient : IGlamSource
{
    private const string BaseUrl = "https://ffxiv.eorzeacollection.com";

    private readonly IEcTransport transport;

    public EorzeaCollectionClient()
        : this(EcTransportFactory.Create())
    {
    }

    internal EorzeaCollectionClient(IEcTransport transport) => this.transport = transport;

    public async Task<EcItem?> ResolveEcItemAsync(GlamSlot slot, string itemName, uint gameItemId, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new SearchRequest(itemName));

        var output = await this.transport.PostJsonAsync($"{BaseUrl}/gear/{slot.Key}/search", body, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
            return null;

        List<EcGearDto>? records;
        try
        {
            records = JsonSerializer.Deserialize<List<EcGearDto>>(output);
        }
        catch (JsonException)
        {
            // A non-JSON body usually means Cloudflare served a block/challenge page.
            Services.Log.Warning("GoodGlam: Eorzea Collection search returned an unexpected (non-JSON) response.");
            return null;
        }

        if (records is null)
            return null;

        foreach (var r in records)
        {
            if (r.XivApiId == gameItemId)
                return new EcItem(r.Id, r.Name ?? itemName, r.XivApiId);
        }

        return null;
    }

    public async Task<GlamPopularity> GetTopPopularityAsync(GlamSlot slot, int ecId, PopularityFilters filters, CancellationToken ct)
    {
        var url = BuildListingUrl(slot, ecId, filters);

        var html = await this.transport.GetAsync(url, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
            return new GlamPopularity(0, null, null, url);

        // Each glamour card exposes its loves count as:
        //   <span id="js-glamour-likes-<glamId>" ...>1,234</span>
        // and its title in a sibling link:
        //   <a ... href="/glamour/<glamId>/<slug>"> ... <h3 class="...content-title...">Name</h3>
        // We take the maximum loves across the page rather than trusting result ordering,
        // then pair the winner with its name.
        var bestLoves = 0;
        string? bestId = null;
        foreach (Match m in LovesRegex().Matches(html))
        {
            var raw = m.Groups[2].Value.Replace(",", string.Empty);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var loves) && loves > bestLoves)
            {
                bestLoves = loves;
                bestId = m.Groups[1].Value;
            }
        }

        if (bestId is null)
            return new GlamPopularity(0, null, null, url);

        var name = ExtractGlamName(html, bestId);
        return new GlamPopularity(bestLoves, $"{BaseUrl}/glamour/{bestId}", name, url);
    }

    /// <summary>
    /// Pulls the glamour title for a specific id out of the listing HTML, decoding HTML entities.
    /// Returns <c>null</c> if the card's title can't be located (the URL alone is still useful).
    /// </summary>
    private static string? ExtractGlamName(string html, string glamId)
    {
        foreach (Match m in NameRegex().Matches(html))
        {
            if (m.Groups[1].Value == glamId)
                return WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
        }

        return null;
    }

    /// <summary>
    /// Builds the glamour-listing URL: always ordered by loves and scoped to the slot piece,
    /// then any active global filters appended in EC's <c>filter[name]=value</c> form. With no
    /// filters configured this matches the original unfiltered query exactly.
    /// </summary>
    private static string BuildListingUrl(GlamSlot slot, int ecId, PopularityFilters filters)
    {
        var query = new StringBuilder($"{BaseUrl}/glamours?");
        query.Append(Filter("orderBy", "loves"));
        query.Append('&').Append(Filter(slot.FilterParam, ecId.ToString()));

        foreach (var (name, value) in filters.ActiveParams())
            query.Append('&').Append(Filter(name, value));

        query.Append("&page=1");
        return query.ToString();

        static string Filter(string name, string value)
        {
            var key = name.EndsWith("[]", StringComparison.Ordinal)
                ? $"filter%5B{Uri.EscapeDataString(name[..^2])}%5D%5B%5D"
                : $"filter%5B{Uri.EscapeDataString(name)}%5D";
            return $"{key}={Uri.EscapeDataString(value)}";
        }
    }

    [GeneratedRegex("id=\"js-glamour-likes-(\\d+)\"[^>]*>([\\d,]+)<", RegexOptions.IgnoreCase)]
    private static partial Regex LovesRegex();

    [GeneratedRegex("href=\"/glamour/(\\d+)/[^\"]*\"[\\s\\S]{0,200}?content-title[^>]*>([^<]+)<", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    private sealed record SearchRequest([property: JsonPropertyName("search")] string Search);

    private sealed class EcGearDto
    {
        [JsonPropertyName("ID")] public int Id { get; set; }

        [JsonPropertyName("Name")] public string? Name { get; set; }

        [JsonPropertyName("XIVApiId")] public long XivApiId { get; set; }
    }
}
