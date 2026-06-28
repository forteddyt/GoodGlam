using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoodGlam.Glam;

/// <summary>An Eorzea Collection item record, bridging the game item ID to EC's own filter ID.</summary>
public sealed record EcItem(int EcId, string Name, long XivApiId);

/// <summary>The most-loved glamour found for a given item, used to judge popularity.</summary>
public sealed record GlamPopularity(int TopLoves, string? TopGlamUrl);

/// <summary>
/// Abstraction over the glamour data source so the live scraper can be swapped for a
/// hosted proxy or a prebuilt index later without touching the rest of the plugin.
/// </summary>
public interface IGlamSource
{
    Task<EcItem?> ResolveEcItemAsync(GlamSlot slot, string itemName, uint gameItemId, CancellationToken ct);

    Task<GlamPopularity> GetTopPopularityAsync(GlamSlot slot, int ecId, CancellationToken ct);
}

/// <summary>
/// Live Eorzea Collection client. EorzeaCollection has no public API and its Cloudflare
/// WAF blocks .NET's managed HTTP stacks via TLS fingerprinting (both SocketsHttpHandler
/// and WinHttpHandler get a 403, even with browser-like headers). The system
/// <c>curl.exe</c> (shipped with Windows 10 1803+) produces a TLS ClientHello that
/// Cloudflare accepts, so we shell out to it. This keeps everything on the user's machine
/// (no backend) while remaining reliable.
///
/// Two endpoints are used:
///   * POST /gear/{slot}/search  -> JSON, maps game item ID (XIVApiId) -> EC ID.
///   * GET  /glamours?filter[..]  -> HTML listing we scrape for the top "loves" count.
/// </summary>
public sealed partial class EorzeaCollectionClient : IGlamSource
{
    private const string BaseUrl = "https://ffxiv.eorzeacollection.com";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly string CurlPath = ResolveCurlPath();

    public async Task<EcItem?> ResolveEcItemAsync(GlamSlot slot, string itemName, uint gameItemId, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new SearchRequest(itemName));

        var args = new List<string>
        {
            "-s", "--compressed", "--max-time", "20",
            "-X", "POST", $"{BaseUrl}/gear/{slot.Key}/search",
            "-H", $"User-Agent: {UserAgent}",
            "-H", "Accept: application/json, text/plain, */*",
            "-H", "Accept-Language: en-US,en;q=0.9",
            "-H", "Content-Type: application/json",
            "-H", "X-Requested-With: XMLHttpRequest",
            "-H", $"Origin: {BaseUrl}",
            "-H", $"Referer: {BaseUrl}/glamours",
            "--data", body,
        };

        var output = await RunCurlAsync(args, ct).ConfigureAwait(false);
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

    public async Task<GlamPopularity> GetTopPopularityAsync(GlamSlot slot, int ecId, CancellationToken ct)
    {
        var url = $"{BaseUrl}/glamours?filter%5BorderBy%5D=loves&filter%5B{slot.FilterParam}%5D={ecId}&page=1";

        var args = new List<string>
        {
            "-s", "--compressed", "--max-time", "20",
            url,
            "-H", $"User-Agent: {UserAgent}",
            "-H", "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "-H", "Accept-Language: en-US,en;q=0.9",
        };

        var html = await RunCurlAsync(args, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html))
            return new GlamPopularity(0, null);

        // Each glamour card exposes its loves count as:
        //   <span id="js-glamour-likes-<glamId>" ...>1,234</span>
        // We take the maximum across the page rather than trusting result ordering.
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

        var glamUrl = bestId is null ? null : $"{BaseUrl}/glamour/{bestId}";
        return new GlamPopularity(bestLoves, glamUrl);
    }

    private static string ResolveCurlPath()
    {
        var system = Path.Combine(Environment.SystemDirectory, "curl.exe");
        return File.Exists(system) ? system : "curl.exe";
    }

    private static async Task<string?> RunCurlAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(CurlPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };

        try
        {
            if (!proc.Start())
                return null;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "GoodGlam: unable to launch curl.exe; Eorzea Collection lookups are disabled.");
            return null;
        }

        // Drain both pipes concurrently: if curl writes enough to stderr (e.g. TLS/HTTP
        // errors) and we never read it, the process can block once the pipe buffer fills.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" {stderr.Trim()}";
            Services.Log.Debug($"GoodGlam: curl.exe exited with code {proc.ExitCode}.{detail}");
            return null;
        }

        return stdout;
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
    }

    [GeneratedRegex("id=\"js-glamour-likes-(\\d+)\"[^>]*>([\\d,]+)<", RegexOptions.IgnoreCase)]
    private static partial Regex LovesRegex();

    private sealed record SearchRequest([property: JsonPropertyName("search")] string Search);

    private sealed class EcGearDto
    {
        [JsonPropertyName("ID")] public int Id { get; set; }

        [JsonPropertyName("Name")] public string? Name { get; set; }

        [JsonPropertyName("XIVApiId")] public long XivApiId { get; set; }
    }
}
