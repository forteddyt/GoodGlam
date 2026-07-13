using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoodGlam.Diagnostics;

namespace GoodGlam.Glam;

/// <summary>An Eorzea Collection item record, bridging the game item ID to EC's own filter ID.</summary>
public sealed record EcItem(int EcId, string Name, long XivApiId);

/// <summary>A ranked glamour result scraped from an Eorzea Collection listing.</summary>
public sealed record GlamResult(int Loves, string Url, string? Name = null, string? ImageUrl = null);

/// <summary>
/// The ranked glamour results found for a given item, used to judge popularity while retaining the
/// rest of the listing for later browsing.
/// </summary>
public sealed record GlamPopularity
{
    public GlamPopularity(IEnumerable<GlamResult>? rankedGlams = null, string? listingUrl = null)
    {
        this.RankedGlams = rankedGlams?.Take(10).ToArray() ?? [];
        this.ListingUrl = listingUrl;
    }

    public IReadOnlyList<GlamResult> RankedGlams { get; init; }

    public string? ListingUrl { get; init; }

    [JsonIgnore]
    public GlamResult? Top => this.RankedGlams.Count == 0 ? null : this.RankedGlams[0];

    [JsonIgnore]
    public int TopLoves => this.Top?.Loves ?? 0;
}

/// <summary>
/// Abstraction over the glamour data source so the live scraper can be swapped for a
/// hosted proxy or a prebuilt index later without touching the rest of the plugin.
/// </summary>
public interface IGlamSource
{
    Task<EcItem?> ResolveEcItemAsync(GlamSlot slot, string itemName, uint gameItemId, CancellationToken ct);

    Task<GlamPopularity> GetPopularityAsync(GlamSlot slot, int ecId, PopularityFilters filters, CancellationToken ct);
}

/// <summary>
/// Live Eorzea Collection client. EorzeaCollection has no public API, so two endpoints are
/// scraped:
///   * POST /gear/{slot}/search  -> JSON, maps game item ID (XIVApiId) -> EC ID.
///   * GET  /glamours?filter[..]  -> HTML listing we scrape for the ranked "loves" results.
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
    private readonly ITraceLogger<EorzeaCollectionClient> log;

    public EorzeaCollectionClient(ITraceLogger<EorzeaCollectionClient>? log = null)
        : this(EcTransportFactory.Create(), log)
    {
    }

    internal EorzeaCollectionClient(IEcTransport transport, ITraceLogger<EorzeaCollectionClient>? log = null)
    {
        this.transport = transport;
        this.log = log ?? new TraceLogger<EorzeaCollectionClient>();
    }

    public async Task<EcItem?> ResolveEcItemAsync(GlamSlot slot, string itemName, uint gameItemId, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new SearchRequest(itemName));

        this.log.Debug($"searching /gear/{slot.Key}/search for '{itemName}' (gameItemId={gameItemId}).");

        var output = await this.transport.PostJsonAsync($"{BaseUrl}/gear/{slot.Key}/search", body, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            this.log.Debug($"search for '{itemName}' returned an empty/blocked response.");
            return null;
        }

        List<EcGearDto>? records;
        try
        {
            records = JsonSerializer.Deserialize<List<EcGearDto>>(output);
        }
        catch (JsonException)
        {
            // A non-JSON body usually means Cloudflare served a block/challenge page.
            this.log.Warning("Eorzea Collection search returned an unexpected (non-JSON) response.");
            return null;
        }

        if (records is null)
        {
            this.log.Debug($"search for '{itemName}' deserialized to no records.");
            return null;
        }

        this.log.Verbose($"search for '{itemName}' returned {records.Count} record(s); matching on XIVApiId={gameItemId}.");
        foreach (var r in records)
        {
            if (r.XivApiId == gameItemId)
            {
                this.log.Debug($"resolved '{itemName}' (gameItemId={gameItemId}) -> EC id {r.Id}.");
                return new EcItem(r.Id, r.Name ?? itemName, r.XivApiId);
            }
        }

        this.log.Debug($"no EC record matched gameItemId={gameItemId} among {records.Count} result(s) for '{itemName}'.");
        return null;
    }

    public async Task<GlamPopularity> GetPopularityAsync(GlamSlot slot, int ecId, PopularityFilters filters, CancellationToken ct)
    {
        var url = BuildListingUrl(slot, ecId, filters);

        this.log.Verbose($"fetching glamour listing {url}.");

        var html = await this.transport.GetAsync(url, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
        {
            this.log.Debug($"glamour listing for EC id {ecId} returned an empty/blocked response.");
            return new GlamPopularity(listingUrl: url);
        }

        this.log.Verbose($"listing for EC id {ecId} returned {html.Length} chars of HTML.");

        var names = ExtractGlamNames(html);
        var images = ExtractGlamImages(html);
        var rankedGlams = LovesRegex().Matches(html)
            .Cast<Match>()
            .Select(match => ParseGlam(match, names, images))
            .Where(glam => glam is not null)
            .Select(glam => glam!)
            .OrderByDescending(glam => glam.Loves)
            .Take(10)
            .ToArray();

        if (rankedGlams.Length == 0)
        {
            this.log.Debug($"no glamour cards found in the listing for EC id {ecId}.");
            return new GlamPopularity(listingUrl: url);
        }

        var top = rankedGlams[0];
        this.log.Debug(
            $"top glamour for EC id {ecId} is '{top.Name ?? top.Url}' with {top.Loves} loves " +
            $"({rankedGlams.Length} ranked result(s) captured).");
        return new GlamPopularity(rankedGlams, url);
    }

    private static GlamResult? ParseGlam(
        Match match,
        IReadOnlyDictionary<string, string?> names,
        IReadOnlyDictionary<string, string?> images)
    {
        var rawLoves = match.Groups[2].Value.Replace(",", string.Empty);
        if (!int.TryParse(rawLoves, NumberStyles.Integer, CultureInfo.InvariantCulture, out var loves))
            return null;

        var glamId = match.Groups[1].Value;
        names.TryGetValue(glamId, out var name);
        images.TryGetValue(glamId, out var imageUrl);
        return new GlamResult(loves, $"{BaseUrl}/glamour/{glamId}", name, imageUrl);
    }

    private static IReadOnlyDictionary<string, string?> ExtractGlamNames(string html)
    {
        Dictionary<string, string?> names = new();
        foreach (Match match in NameRegex().Matches(html))
        {
            var glamId = match.Groups[1].Value;
            names.TryAdd(glamId, WebUtility.HtmlDecode(match.Groups[2].Value).Trim());
        }

        return names;
    }

    private static IReadOnlyDictionary<string, string?> ExtractGlamImages(string html)
    {
        Dictionary<string, string?> images = new();
        foreach (Match match in ImageRegex().Matches(html))
        {
            var glamId = match.Groups[2].Value;
            images.TryAdd(glamId, match.Groups[1].Value);
        }

        return images;
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

    [GeneratedRegex("class=\"c-glamour-grid-item-image[^\"]*\"[^>]*?src=\"(https://glamours\\.eorzeacollection\\.com/(\\d+)/[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();

    private sealed record SearchRequest([property: JsonPropertyName("search")] string Search);

    private sealed class EcGearDto
    {
        [JsonPropertyName("ID")] public int Id { get; set; }

        [JsonPropertyName("Name")] public string? Name { get; set; }

        [JsonPropertyName("XIVApiId")] public long XivApiId { get; set; }
    }
}
