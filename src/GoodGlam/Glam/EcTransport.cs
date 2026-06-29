using System.Diagnostics;
using System.Net;
using System.Text;

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
internal sealed class FallbackEcTransport(IEcTransport managed, IEcTransport curl) : IEcTransport
{
    private volatile bool preferCurl;

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
        => this.SendAsync((t, c) => t.PostJsonAsync(url, jsonBody, c), ct);

    public Task<string?> GetAsync(string url, CancellationToken ct)
        => this.SendAsync((t, c) => t.GetAsync(url, c), ct);

    private async Task<string?> SendAsync(Func<IEcTransport, CancellationToken, Task<string?>> call, CancellationToken ct)
    {
        var primary = this.preferCurl ? curl : managed;
        var secondary = this.preferCurl ? managed : curl;

        var result = await call(primary, ct).ConfigureAwait(false);
        if (result is not null)
            return result;

        result = await call(secondary, ct).ConfigureAwait(false);
        if (result is not null)
        {
            this.preferCurl = !this.preferCurl;
            Services.Log.Information(
                $"GoodGlam: switched Eorzea Collection transport to {(this.preferCurl ? "curl.exe" : "in-process HTTP")}.");
        }

        return result;
    }
}

/// <summary>In-process transport. Works under Wine and on most platforms; native Windows is blocked.</summary>
internal sealed class ManagedHttpTransport(string userAgent) : IEcTransport
{
    private static readonly HttpClient Http = CreateClient();

    public async Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("Origin", "https://ffxiv.eorzeacollection.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://ffxiv.eorzeacollection.com/glamours");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return await SendAsync(req, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        req.Headers.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        return await SendAsync(req, ct).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private static async Task<string?> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Services.Log.Debug($"GoodGlam: Eorzea Collection request to {req.RequestUri} returned HTTP {(int)resp.StatusCode}.");
                return null;
            }

            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, $"GoodGlam: Eorzea Collection request to {req.RequestUri} failed.");
            return null;
        }
    }
}

/// <summary>
/// Fallback transport for native Windows: shells out to the system <c>curl.exe</c>
/// (libcurl/Schannel), whose TLS ClientHello Cloudflare accepts. Returns <c>null</c> when
/// curl is unavailable (e.g. under Wine), letting the in-process transport take over.
/// </summary>
internal sealed class CurlTransport(string userAgent) : IEcTransport
{
    private static readonly string CurlPath = ResolveCurlPath();

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-s", "--compressed", "--max-time", "20",
            "-X", "POST", url,
            "-H", $"User-Agent: {userAgent}",
            "-H", "Accept: application/json, text/plain, */*",
            "-H", "Accept-Language: en-US,en;q=0.9",
            "-H", "Content-Type: application/json",
            "-H", "X-Requested-With: XMLHttpRequest",
            "-H", "Origin: https://ffxiv.eorzeacollection.com",
            "-H", "Referer: https://ffxiv.eorzeacollection.com/glamours",
            "--data", jsonBody,
        };

        return RunCurlAsync(args, ct);
    }

    public Task<string?> GetAsync(string url, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-s", "--compressed", "--max-time", "20",
            url,
            "-H", $"User-Agent: {userAgent}",
            "-H", "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "-H", "Accept-Language: en-US,en;q=0.9",
        };

        return RunCurlAsync(args, ct);
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
            Services.Log.Warning(ex, "GoodGlam: unable to launch curl.exe; relying on the in-process transport.");
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
}
