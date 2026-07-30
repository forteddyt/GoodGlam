using System.Diagnostics;
using System.Net;
using System.Text;
using GoodGlam.Diagnostics;

namespace GoodGlam.Glam;

/// <summary>
/// Minimal HTTP surface the Eorzea Collection client needs: a JSON POST (item search)
/// and a plain GET (glamour listing). Returns the response body, or <c>null</c> when the
/// request fails or is blocked.
/// </summary>
internal interface IEcTransport
{
    Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct);

    Task<string?> GetAsync(string url, CancellationToken ct);
}

internal static class EcTransportFactory
{
    /// <summary>
    /// A single in-process <see cref="HttpClient"/> transport, on every platform.
    ///
    /// GoodGlam used to pair this with a <c>curl.exe</c> subprocess because EC was thought to
    /// block .NET's TLS fingerprint on native Windows. It doesn't: the same curl binary is served
    /// 403 over HTTP/1.1 and 200 over HTTP/2, so what EC's edge refuses is the HTTP version, not
    /// the TLS stack. Pinning HTTP/2 (see <see cref="EcRequest"/>) fixes the managed client
    /// everywhere, which leaves the subprocess with nothing to do - and it could not have helped
    /// anyway, since Windows' bundled curl.exe is built without HTTP/2.
    /// </summary>
    public static IEcTransport Create() => new ManagedHttpTransport(EcRequest.UserAgent);
}

/// <summary>
/// Builds the requests GoodGlam sends to Eorzea Collection.
///
/// Centralised so the HTTP version and headers that decide whether EC answers at all live in one
/// place rather than being restated per verb, and so a test can pin them (see
/// <c>ManagedHttpTransportTests</c>).
/// </summary>
internal static class EcRequest
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>A JSON POST (the gear search endpoint).</summary>
    public static HttpRequestMessage Post(string url, string jsonBody, string userAgent = UserAgent)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("Origin", "https://ffxiv.eorzeacollection.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://ffxiv.eorzeacollection.com/glamours");
        return PinHttp2(req);
    }

    /// <summary>A plain GET (the glamour listing pages).</summary>
    public static HttpRequestMessage Get(string url, string userAgent = UserAgent)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        req.Headers.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        return PinHttp2(req);
    }

    /// <summary>
    /// Requests HTTP/2, which is the single thing that decides whether EC answers at all: its
    /// Cloudflare edge 403s HTTP/1.1 and serves HTTP/2 (verified against the live edge, and
    /// reproduced with curl to rule out any TLS-fingerprint explanation). Set per-request so it
    /// holds regardless of the shared client's defaults.
    ///
    /// OrLower (not OrHigher): negotiate HTTP/2 via ALPN on the real HTTPS edge, but degrade to
    /// HTTP/1.1 where HTTP/2 can't be negotiated - e.g. a plaintext loopback, which has no
    /// TLS/ALPN - instead of throwing. A downgrade against EC itself means a 403, which
    /// <see cref="ManagedHttpTransport"/> logs with the negotiated version so the cause is legible.
    /// </summary>
    private static HttpRequestMessage PinHttp2(HttpRequestMessage req)
    {
        req.Version = HttpVersion.Version20;
        req.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        return req;
    }
}

/// <summary>
/// The in-process transport, used on every platform GoodGlam ships on: native Windows, Wine and
/// Linux. See <see cref="EcTransportFactory"/> for why no subprocess fallback is needed.
/// </summary>
internal sealed class ManagedHttpTransport : IEcTransport
{
    private static readonly HttpClient Http = CreateClient();

    private readonly string userAgent;
    private readonly ITraceLogger<ManagedHttpTransport> log;

    public ManagedHttpTransport(string userAgent, ITraceLogger<ManagedHttpTransport>? log = null)
    {
        this.userAgent = userAgent;
        this.log = log ?? new TraceLogger<ManagedHttpTransport>();
    }

    public async Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        using var req = EcRequest.Post(url, jsonBody, this.userAgent);
        return await this.SendAsync(req, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        using var req = EcRequest.Get(url, this.userAgent);
        return await this.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private async Task<string?> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // Log the negotiated version and Cloudflare's mitigation marker, because with no
                // fallback transport this message is the whole story. The two failures worth
                // telling apart both arrive as a 403: a request that ended up on HTTP/1.1 (which
                // EC refuses outright) versus a bot challenge aimed at this IP.
                this.log.Debug(
                    $"in-process {req.Method} {req.RequestUri} returned HTTP {(int)resp.StatusCode} " +
                    $"over HTTP/{resp.Version} in {sw.ElapsedMilliseconds}ms{DescribeRefusal(resp)}.");
                return null;
            }

            var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            this.log.Verbose(
                $"in-process {req.Method} {req.RequestUri} -> HTTP {(int)resp.StatusCode}, {content.Length} chars in {sw.ElapsedMilliseconds}ms.");
            return content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            this.log.Warning($"in-process request to {req.RequestUri} failed after {sw.ElapsedMilliseconds}ms.", ex);
            return null;
        }
    }

    /// <summary>
    /// Names the cause of a refusal when the response identifies one, so a user's log says why
    /// rather than just reporting a status.
    /// </summary>
    private static string DescribeRefusal(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("cf-mitigated", out var mitigated))
            return $" - Cloudflare {string.Join(",", mitigated)} for this IP, not a GoodGlam fault";

        if (resp.StatusCode == HttpStatusCode.Forbidden && resp.Version < HttpVersion.Version20)
        {
            return " - the request fell back to HTTP/1.1, which Eorzea Collection refuses; " +
                   "HTTP/2 could not be negotiated";
        }

        return string.Empty;
    }
}
