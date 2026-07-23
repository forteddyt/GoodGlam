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
/// Fallback transport for native Windows: shells out to the system <c>curl.exe</c>
/// (libcurl/Schannel), whose TLS ClientHello Cloudflare accepts. Returns <c>null</c> when
/// curl is unavailable (e.g. under Wine), letting the in-process transport take over.
/// </summary>
/// <remarks>
/// Deliberately not <c>[ExcludeFromCodeCoverage]</c>: GoodGlam ships on both Windows and Linux, and
/// the <c>curl.exe</c> subprocess can't be driven deterministically from either CI runner, so the
/// report should honestly show this path as uncovered rather than hiding it.
/// </remarks>
internal sealed class CurlTransport : IEcTransport
{
    private static readonly string CurlPath = ResolveCurlPath();

    private readonly string userAgent;
    private readonly ITraceLogger<CurlTransport> log;

    public CurlTransport(string userAgent, ITraceLogger<CurlTransport>? log = null)
    {
        this.userAgent = userAgent;
        this.log = log ?? new TraceLogger<CurlTransport>();
    }

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        var args = new List<string>
        {
            // Force HTTP/2: EC's Cloudflare edge 403s HTTP/1.1 (see ManagedHttpTransport.SendAsync).
            "-s", "--http2", "--compressed", "--max-time", "20",
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

        return this.RunCurlAsync(args, ct);
    }

    public Task<string?> GetAsync(string url, CancellationToken ct)
    {
        var args = new List<string>
        {
            // Force HTTP/2: EC's Cloudflare edge 403s HTTP/1.1 (see ManagedHttpTransport.SendAsync).
            "-s", "--http2", "--compressed", "--max-time", "20",
            url,
            "-H", $"User-Agent: {this.userAgent}",
            "-H", "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "-H", "Accept-Language: en-US,en;q=0.9",
        };

        return this.RunCurlAsync(args, ct);
    }

    private static string ResolveCurlPath()
    {
        var system = Path.Combine(Environment.SystemDirectory, "curl.exe");
        return File.Exists(system) ? system : "curl.exe";
    }

    private async Task<string?> RunCurlAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
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

        this.log.Verbose($"curl.exe succeeded — {stdout.Length} chars in {sw.ElapsedMilliseconds}ms.");
        return stdout;
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
