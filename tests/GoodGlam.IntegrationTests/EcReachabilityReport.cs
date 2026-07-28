using System.Net;
using System.Text;

namespace GoodGlam.IntegrationTests;

/// <summary>
/// A post-mortem of a single reachability probe against Eorzea Collection: the response metadata
/// that actually explains *why* the live tests could not reach EC.
///
/// The plugin's transport deliberately collapses every failure to <c>null</c> (see
/// <c>IEcTransport</c>), and the fixture installs a no-op Dalamud log, so a failing CI run used to
/// report nothing but "unreachable after retries". That is not enough to tell a Cloudflare bot
/// challenge apart from an EC outage, a DNS failure, or a genuine bridge regression — which is
/// exactly the distinction the next person needs. This record captures the missing detail once,
/// at the moment of failure, and renders it into the assertion message.
/// </summary>
internal sealed record EcReachabilityReport(
    int? StatusCode = null,
    string? CfRay = null,
    string? CfMitigated = null,
    string? Server = null,
    string? BodySnippet = null,
    string? TransportError = null)
{
    /// <summary>
    /// Whether Cloudflare answered with a managed bot challenge instead of proxying to the origin.
    /// Signalled either by the <c>cf-mitigated</c> header or by the "Just a moment..." interstitial
    /// body; a bare 403 without those markers is a different problem and must not be conflated.
    /// </summary>
    public bool IsCloudflareChallenge =>
        this.CfMitigated?.Contains("challenge", StringComparison.OrdinalIgnoreCase) == true
        || (this.StatusCode is 403 or 503 && this.LooksLikeInterstitial);

    private bool LooksLikeInterstitial =>
        this.BodySnippet is { } body
        && (body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || body.Contains("cf-chl", StringComparison.OrdinalIgnoreCase)
            || body.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase));

    public string Describe()
    {
        var report = new StringBuilder();
        report.AppendLine("Eorzea Collection reachability probe:");

        if (this.TransportError is { } error)
        {
            report.AppendLine($"  result       : no HTTP response ({error})");
        }
        else
        {
            report.AppendLine($"  status       : HTTP {this.StatusCode?.ToString() ?? "(none)"}");
            report.AppendLine($"  server       : {this.Server ?? "(none)"}");
            report.AppendLine($"  cf-ray       : {this.CfRay ?? "(none)"}");
            report.AppendLine($"  cf-mitigated : {this.CfMitigated ?? "(none)"}");
            report.AppendLine($"  body         : {this.BodySnippet ?? "(empty)"}");
        }

        if (this.IsCloudflareChallenge)
        {
            report.AppendLine();
            report.AppendLine(
                "  Diagnosis: Cloudflare bot challenge. Eorzea Collection is serving its managed " +
                "challenge to this runner's egress IP instead of proxying to the origin, so every " +
                "transport is refused alike - in-process HTTP and curl.exe are challenged " +
                "identically, because this is IP reputation, not a TLS fingerprint. That makes it " +
                "an environmental block on the CI runner, not a GoodGlam regression: the same " +
                "request succeeds from a residential IP, and re-running the job usually lands on a " +
                "different runner IP and passes.");
        }

        return report.ToString();
    }
}

/// <summary>
/// Issues the diagnostic probe that produces an <see cref="EcReachabilityReport"/>. Mirrors the
/// request the plugin's in-process transport makes (HTTP/2, same headers) so what it observes is
/// what the real client would have observed — but, unlike the plugin, it keeps the status line and
/// Cloudflare headers instead of collapsing them to <c>null</c>.
/// </summary>
internal static class EcReachabilityProbe
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private const int BodySnippetLength = 200;

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static async Task<EcReachabilityReport> RunAsync(EcFixture probe, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"https://ffxiv.eorzeacollection.com/gear/{probe.Slot.Key}/search")
            {
                Content = new StringContent(
                    $"{{\"search\":{System.Text.Json.JsonSerializer.Serialize(probe.Name)}}}",
                    Encoding.UTF8,
                    "application/json"),
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };

            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Headers.TryAddWithoutValidation("Origin", "https://ffxiv.eorzeacollection.com");
            request.Headers.TryAddWithoutValidation("Referer", "https://ffxiv.eorzeacollection.com/glamours");

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return new EcReachabilityReport(
                StatusCode: (int)response.StatusCode,
                CfRay: FirstHeader(response, "cf-ray"),
                CfMitigated: FirstHeader(response, "cf-mitigated"),
                Server: FirstHeader(response, "server"),
                BodySnippet: Snippet(body));
        }
        catch (Exception ex)
        {
            return new EcReachabilityReport(TransportError: $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? FirstHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string Snippet(string body)
    {
        var collapsed = body.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= BodySnippetLength
            ? collapsed
            : collapsed[..BodySnippetLength] + "...";
    }
}
