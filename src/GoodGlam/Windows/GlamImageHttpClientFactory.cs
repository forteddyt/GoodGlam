using System.Net;

namespace GoodGlam.Windows;

/// <summary>
/// Builds the cover-image <see cref="HttpClient"/> the History tab's hover preview uses to download
/// Eorzea Collection covers. Extracted from <see cref="HistoryTab"/> — which is excluded from coverage
/// as pure wiring — so the one piece of behaviour that matters, the HTTP/2 protocol configuration that
/// keeps Cloudflare from rejecting the download, stays under test. <see cref="GlamImageLoader"/> is
/// handed the byte fetch off this client, so its own tests inject a fake and never observe this config;
/// this factory is the seam that does.
/// </summary>
internal static class GlamImageHttpClientFactory
{
    /// <summary>A browser-shaped User-Agent so the CDN treats the download like a real client.</summary>
    internal const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>Wall-clock cap on a single cover-image download before it is abandoned.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    internal static HttpClient Create()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var http = new HttpClient(handler)
        {
            Timeout = Timeout,

            // Eorzea Collection's Cloudflare edge 403s the .NET client's default HTTP/1.1 requests but
            // serves HTTP/2 — the same reason browsers and curl (both HTTP/2) succeed while a bare
            // HttpClient is blocked. Prefer HTTP/2 so the cover-image download from
            // glamours.eorzeacollection.com isn't rejected. Verified against the live CDN: HTTP/1.1 -> 403,
            // HTTP/2 -> 200, independent of request headers. OrLower degrades gracefully where HTTP/2
            // can't be negotiated rather than throwing.
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return http;
    }
}
