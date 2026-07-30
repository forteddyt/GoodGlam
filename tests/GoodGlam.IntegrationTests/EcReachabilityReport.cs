using System.Net;
using System.Text;
using System.Text.Json;
using GoodGlam.Glam;

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
    string? NegotiatedVersion = null,
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
            report.AppendLine($"  negotiated   : HTTP/{this.NegotiatedVersion ?? "(none)"}");
            report.AppendLine($"  server       : {this.Server ?? "(none)"}");
            report.AppendLine($"  cf-ray       : {this.CfRay ?? "(none)"}");
            report.AppendLine($"  cf-mitigated : {this.CfMitigated ?? "(none)"}");

            // A body that is present but empty is itself a finding, so label it rather than
            // rendering a blank line that reads like missing output.
            report.AppendLine(
                $"  body         : {(string.IsNullOrEmpty(this.BodySnippet) ? "(empty)" : this.BodySnippet)}");
        }

        if (this.IsCloudflareChallenge)
        {
            report.AppendLine();
            report.AppendLine(
                "  Diagnosis: Cloudflare bot challenge. Eorzea Collection is serving its managed " +
                "challenge to this client's egress IP instead of proxying to the origin. This is IP " +
                "reputation rather than anything about the request, so it is environmental and not " +
                "a GoodGlam regression - no transport or TLS change would get past it. Datacenter " +
                "ranges (CI runners) are challenged most often, but residential addresses are not " +
                "exempt. Re-running usually clears it; from CI a re-run also lands on a different " +
                "egress IP.");
        }

        return report.ToString();
    }
}

/// <summary>
/// Issues the diagnostic probe that produces an <see cref="EcReachabilityReport"/>.
///
/// The request is built by the plugin's own <see cref="EcRequest"/>, the same builder
/// <see cref="ManagedHttpTransport"/> uses, so the probe cannot drift into reporting on a request
/// GoodGlam never sends. What differs is only the handling: the transport maps every failure to
/// <c>null</c>, whereas this keeps the status line and Cloudflare headers.
/// </summary>
internal static class EcReachabilityProbe
{
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
            using var request = EcRequest.Post(
                $"https://ffxiv.eorzeacollection.com/gear/{probe.Slot.Key}/search",
                $$"""{"search":{{JsonSerializer.Serialize(probe.Name)}}}""");

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return new EcReachabilityReport(
                StatusCode: (int)response.StatusCode,
                NegotiatedVersion: response.Version.ToString(),
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
