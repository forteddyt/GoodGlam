using System.Diagnostics;
using System.Globalization;
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
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>
    /// In-process <see cref="HttpClient"/> first, with the <c>curl.exe</c> subprocess as a
    /// fallback. Managed HTTP works everywhere except native Windows, where Cloudflare blocks
    /// .NET's TLS fingerprint; trying it first and only shelling out on a block keeps the
    /// design self-correcting (no OS sniffing, and it drops the subprocess automatically if
    /// Cloudflare ever stops blocking).
    /// </summary>
    public static IEcTransport Create()
        => new FallbackEcTransport(new ManagedHttpTransport(UserAgent), new CurlTransport(UserAgent));
}

/// <summary>
/// Tries the primary transport, falling back to the secondary when the primary returns no
/// usable body (a Cloudflare block on native Windows yields a 403 → null). The transport that
/// last succeeded becomes primary so steady-state traffic uses a single working path.
/// </summary>
internal sealed class FallbackEcTransport : IEcTransport
{
    private readonly IEcTransport managed;
    private readonly IEcTransport curl;
    private readonly ITraceLogger<FallbackEcTransport> log;

    private volatile bool preferCurl;

    public FallbackEcTransport(IEcTransport managed, IEcTransport curl, ITraceLogger<FallbackEcTransport>? log = null)
    {
        this.managed = managed;
        this.curl = curl;
        this.log = log ?? new TraceLogger<FallbackEcTransport>();
    }

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
        => this.SendAsync((t, c) => t.PostJsonAsync(url, jsonBody, c), ct);

    public Task<string?> GetAsync(string url, CancellationToken ct)
        => this.SendAsync((t, c) => t.GetAsync(url, c), ct);

    private async Task<string?> SendAsync(Func<IEcTransport, CancellationToken, Task<string?>> call, CancellationToken ct)
    {
        var preferCurlNow = this.preferCurl;
        var primary = preferCurlNow ? this.curl : this.managed;
        var secondary = preferCurlNow ? this.managed : this.curl;
        var primaryName = preferCurlNow ? "curl.exe" : "in-process HTTP";
        var secondaryName = preferCurlNow ? "in-process HTTP" : "curl.exe";

        this.log.Verbose($"trying primary transport ({primaryName}).");
        var result = await call(primary, ct).ConfigureAwait(false);
        if (result is not null)
            return result;

        this.log.Debug($"primary transport ({primaryName}) returned nothing; falling back to {secondaryName}.");
        result = await call(secondary, ct).ConfigureAwait(false);
        if (result is not null)
        {
            this.preferCurl = !this.preferCurl;
            this.log.Information(
                $"switched Eorzea Collection transport to {(this.preferCurl ? "curl.exe" : "in-process HTTP")}.");
        }
        else
        {
            this.log.Debug("both transports returned nothing for this request.");
        }

        return result;
    }
}

/// <summary>In-process transport. Works under Wine and on most platforms; native Windows is blocked.</summary>
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
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("User-Agent", this.userAgent);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("Origin", "https://ffxiv.eorzeacollection.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://ffxiv.eorzeacollection.com/glamours");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return await this.SendAsync(req, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", this.userAgent);
        req.Headers.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        return await this.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private async Task<string?> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        // Eorzea Collection's Cloudflare edge 403s the .NET client's HTTP/1.1 requests but serves
        // HTTP/2 — the same reason browsers and curl (both HTTP/2) succeed while a default HttpClient is
        // rejected. Request HTTP/2 for every in-process call. Set on the request itself so it applies
        // regardless of the shared client's defaults. Verified against the live edge: HTTP/1.1 -> 403,
        // HTTP/2 -> 200.
        req.Version = HttpVersion.Version20;
        // OrLower (not OrHigher): negotiate HTTP/2 via ALPN on the real HTTPS edge (verified: EC serves
        // HTTP/2 -> 200), but degrade to HTTP/1.1 where HTTP/2 can't be negotiated — e.g. a plaintext
        // loopback, which has no TLS/ALPN — instead of throwing.
        req.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                this.log.Debug(
                    $"in-process {req.Method} {req.RequestUri} returned HTTP {(int)resp.StatusCode} in {sw.ElapsedMilliseconds}ms.");
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
}

/// <summary>
/// Splits the HTTP status curl is asked to append to its own output back off the response body.
///
/// curl exits 0 for any transfer it *completed*, including one the server refused: a Cloudflare
/// challenge is, as far as curl is concerned, a perfectly successful download of a block page. The
/// exit code alone therefore cannot distinguish a usable body from a refusal, so the status is
/// requested via <c>--write-out</c> and recovered here.
/// </summary>
internal static class CurlStatusLine
{
    /// <summary>
    /// The <c>--write-out</c> format: a newline followed by the response status. curl expands the
    /// <c>\n</c> escape itself, so this is passed through as the literal two characters.
    ///
    /// Appending (rather than prefixing) leaves the body's leading bytes untouched for the JSON and
    /// HTML parsers, and the separating newline makes the split unambiguous even when the body
    /// itself ends in digits or spans multiple lines.
    /// </summary>
    public const string WriteOutFormat = @"\n%{http_code}";

    /// <summary>
    /// Recovers the status and the original body from curl's stdout. Returns <c>false</c> when the
    /// marker is missing or unparseable, which means the output can't be trusted as a response.
    /// </summary>
    public static bool TrySplit(string stdout, out int statusCode, out string body)
    {
        statusCode = 0;
        body = string.Empty;

        var marker = stdout.LastIndexOf('\n');
        if (marker < 0)
            return false;

        if (!int.TryParse(
                stdout.AsSpan(marker + 1).Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out statusCode))
        {
            return false;
        }

        body = stdout[..marker];
        return true;
    }

    /// <summary>Whether the status is one whose body is worth handing back to a parser.</summary>
    public static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;
}

/// <summary>
/// Fallback transport for native Windows: shells out to the system <c>curl.exe</c>
/// (libcurl/Schannel). Returns <c>null</c> when curl is unavailable (e.g. under Wine) or when the
/// response isn't usable, letting the in-process transport take over.
/// </summary>
/// <remarks>
/// <para>
/// <b>This route cannot currently reach EC on a stock Windows install.</b>
/// <c>C:\Windows\System32\curl.exe</c> is built without HTTP/2 (verified on windows-latest:
/// <c>libcurl/8.16.0 Schannel zlib WinIDN</c>, no nghttp2), so it advertises only http/1.1 in ALPN,
/// and EC's edge refuses HTTP/1.1 outright (verified: negotiated h2 gets 200, forced 1.1 gets 403,
/// regardless of User-Agent). Such a build therefore receives a 403 rather than a body. That is now
/// surfaced honestly as a blocked response instead of the silent argument-parse abort that forcing
/// <c>--http2</c> used to cause, but it is not a working route - see the wiki (<b>Data transport</b>).
/// </para>
/// <para>
/// Deliberately not <c>[ExcludeFromCodeCoverage]</c>: GoodGlam ships on both Windows and Linux, and
/// this path only activates once the in-process transport is blocked, which never happens on CI, so
/// the live integration suite can't reach it. <c>CurlTransportTests</c> drives it directly against a
/// loopback listener instead, which is the only automated coverage it gets.
/// </para>
/// </remarks>
internal sealed class CurlTransport : IEcTransport
{
    private static readonly string DefaultCurlPath = ResolveCurlPath();

    private readonly string userAgent;
    private readonly string curlPath;
    private readonly ITraceLogger<CurlTransport> log;

    public CurlTransport(
        string userAgent,
        ITraceLogger<CurlTransport>? log = null,
        string? curlPath = null)
    {
        this.userAgent = userAgent;
        this.log = log ?? new TraceLogger<CurlTransport>();
        this.curlPath = curlPath ?? DefaultCurlPath;
    }

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        var args = new List<string>
        {
            // No --http2 here, deliberately. EC's edge does 403 HTTP/1.1 (see
            // ManagedHttpTransport.SendAsync), but curl already negotiates HTTP/2 over TLS via ALPN
            // whenever its libcurl was built with it, so the flag buys nothing where it works - and
            // is fatal where it doesn't: Windows' bundled System32 curl.exe has no HTTP/2 support,
            // and curl rejects an unsupported protocol option while parsing arguments, exiting
            // non-zero without issuing any request at all. Since System32 is exactly the curl this
            // transport resolves on native Windows, forcing the flag stopped it making any request
            // whatsoever. Dropping the flag restores the request; whether EC answers it is a
            // separate matter (see the class remarks).
            "-s", "--compressed", "--max-time", "20",
            "-X", "POST", url,
            "-H", $"User-Agent: {this.userAgent}",
            "-H", "Accept: application/json, text/plain, */*",
            "-H", "Accept-Language: en-US,en;q=0.9",
            "-H", "Content-Type: application/json",
            "-H", "X-Requested-With: XMLHttpRequest",
            "-H", "Origin: https://ffxiv.eorzeacollection.com",
            "-H", "Referer: https://ffxiv.eorzeacollection.com/glamours",
            "--data", jsonBody,
        };

        return this.RunCurlAsync(args, url, ct);
    }

    public Task<string?> GetAsync(string url, CancellationToken ct)
    {
        var args = new List<string>
        {
            // No --http2 here, deliberately. EC's edge does 403 HTTP/1.1 (see
            // ManagedHttpTransport.SendAsync), but curl already negotiates HTTP/2 over TLS via ALPN
            // whenever its libcurl was built with it, so the flag buys nothing where it works - and
            // is fatal where it doesn't: Windows' bundled System32 curl.exe has no HTTP/2 support,
            // and curl rejects an unsupported protocol option while parsing arguments, exiting
            // non-zero without issuing any request at all. Since System32 is exactly the curl this
            // transport resolves on native Windows, forcing the flag stopped it making any request
            // whatsoever. Dropping the flag restores the request; whether EC answers it is a
            // separate matter (see the class remarks).
            "-s", "--compressed", "--max-time", "20",
            url,
            "-H", $"User-Agent: {this.userAgent}",
            "-H", "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "-H", "Accept-Language: en-US,en;q=0.9",
        };

        return this.RunCurlAsync(args, url, ct);
    }

    private static string ResolveCurlPath()
    {
        var system = Path.Combine(Environment.SystemDirectory, "curl.exe");
        return File.Exists(system) ? system : "curl.exe";
    }

    private async Task<string?> RunCurlAsync(IReadOnlyList<string> args, string url, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(this.curlPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        // Added here rather than by each verb so the request can never be issued without the status
        // marker the response parsing below depends on.
        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add(CurlStatusLine.WriteOutFormat);

        using var proc = new Process { StartInfo = psi };

        try
        {
            if (!proc.Start())
            {
                this.log.Debug("curl.exe failed to start (Process.Start returned false).");
                return null;
            }
        }
        catch (Exception ex)
        {
            this.log.Warning("unable to launch curl.exe; relying on the in-process transport.", ex);
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

            // We're bailing out before the normal await of the drain tasks below. Killing the
            // process (and disposing its streams on scope exit) can fault those reads, so observe
            // them here — otherwise a faulted read becomes an unobserved-task exception later. Their
            // output is irrelevant on the cancellation path.
            await ObserveAsync(stdoutTask).ConfigureAwait(false);
            await ObserveAsync(stderrTask).ConfigureAwait(false);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" {stderr.Trim()}";
            this.log.Debug($"curl.exe exited with code {proc.ExitCode} after {sw.ElapsedMilliseconds}ms.{detail}");
            return null;
        }

        if (!CurlStatusLine.TrySplit(stdout, out var status, out var body))
        {
            this.log.Warning(
                $"curl.exe returned output with no parseable status marker after {sw.ElapsedMilliseconds}ms; " +
                "treating it as a failure.");
            return null;
        }

        // A completed-but-refused transfer must not be handed back as a body. curl exits 0 for
        // these, so without the status check a Cloudflare challenge page would be scraped as a
        // listing containing no glamours - silently reporting a blocked item as having zero loves.
        if (!CurlStatusLine.IsSuccess(status))
        {
            this.log.Debug($"curl.exe {url} returned HTTP {status} in {sw.ElapsedMilliseconds}ms.");
            return null;
        }

        this.log.Verbose(
            $"curl.exe {url} -> HTTP {status}, {body.Length} chars in {sw.ElapsedMilliseconds}ms.");
        return body;
    }

    /// <summary>
    /// Awaits a drain task purely to observe its outcome so a fault on the cancellation path can't
    /// surface later as an unobserved-task exception. Both the result and any exception are discarded
    /// — the read was racing a kill/cancellation and its outcome is irrelevant here.
    /// </summary>
    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Best effort: intentionally swallowed.
        }
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
}
