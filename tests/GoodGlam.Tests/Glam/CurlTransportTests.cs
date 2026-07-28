using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

/// <summary>
/// Drives <see cref="CurlTransport"/> — the subprocess path used on native Windows, where
/// Cloudflare blocks managed HTTP — against a throwaway loopback <see cref="HttpListener"/> using a
/// real curl binary.
///
/// This suite exists because the curl path gets no other automated coverage: it only activates when
/// the in-process transport is blocked, which never happens on CI (the runner's managed HTTP reaches
/// Eorzea Collection fine), so the live integration suite never exercises it. Driving the real
/// binary is also the only way to pin the two things a fake could never catch: that curl reports a
/// refused request as a *successful* transfer, and that the argument list stays runnable by the
/// stock <c>System32\curl.exe</c> these tests resolve on Windows. The latter is not hypothetical -
/// that curl has no HTTP/2 support, so a forced <c>--http2</c> made it abort during argument
/// parsing without issuing a request, disabling the fallback on the one platform that needs it.
/// </summary>
public class CurlTransportTests
{
    public CurlTransportTests() => TestServices.EnsureLog();

    /// <summary>
    /// A real curl binary: <c>System32\curl.exe</c> on Windows (shipped since Windows 10 1803),
    /// otherwise the first <c>curl</c> on PATH. Deliberately the same binary the transport itself
    /// resolves in production, so these tests fail if the arguments ever stop being runnable by it.
    /// Both CI runners and the supported dev platforms have one, so a missing binary is a broken
    /// environment rather than a reason to skip - this is the only coverage the subprocess path has.
    /// </summary>
    private static readonly string? CurlBinary = ResolveCurl();

    private static string Curl => CurlBinary
        ?? throw new InvalidOperationException(
            "No curl binary found (looked for System32\\curl.exe and 'curl' on PATH). " +
            "CurlTransport's subprocess path cannot be verified without one.");

    private static string? ResolveCurl()
    {
        var system = Path.Combine(Environment.SystemDirectory, "curl.exe");
        if (File.Exists(system))
            return system;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .SelectMany(dir => new[] { Path.Combine(dir, "curl"), Path.Combine(dir, "curl.exe") })
            .FirstOrDefault(File.Exists);
    }

    private static int NextCandidatePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static (HttpListener Listener, string Url) StartServer(
        int status, string body, List<HttpListenerRequest>? seen = null)
    {
        // Same candidate-port retry as ManagedHttpTransportTests: HttpListener can't bind an
        // ephemeral port and report it back, so close the release-then-rebind race explicitly.
        for (var attempt = 0; ; attempt++)
        {
            var url = $"http://127.0.0.1:{NextCandidatePort()}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(url);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException) when (attempt < 25)
            {
                continue;
            }

            _ = Task.Run(async () =>
            {
                var ctx = await listener.GetContextAsync();
                seen?.Add(ctx.Request);
                ctx.Response.StatusCode = status;
                var bytes = Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            });

            return (listener, url);
        }
    }

    private static CurlTransport Transport() => new("GoodGlam-Test/1.0", curlPath: Curl);

    [Fact]
    public async Task GetAsync_returns_the_response_body_on_success()
    {
        var (listener, url) = StartServer(200, "<html>listing</html>");
        try
        {
            var result = await Transport().GetAsync(url, CancellationToken.None);

            result.Should().Be("<html>listing</html>",
                "the status marker curl appends must be stripped back off, leaving the body verbatim");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task PostJsonAsync_returns_the_response_body_on_success()
    {
        var seen = new List<HttpListenerRequest>();
        var (listener, url) = StartServer(200, """[{"ID":8912}]""", seen);
        try
        {
            var result = await Transport().PostJsonAsync(url, """{"search":"x"}""", CancellationToken.None);

            result.Should().Be("""[{"ID":8912}]""");
            seen.Should().ContainSingle().Which.HttpMethod.Should().Be("POST");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// The regression this suite exists for. curl exits 0 for any *completed* transfer, so a
    /// Cloudflare challenge is — to curl — a perfectly successful download of a block page. Exit
    /// code alone therefore cannot tell success from refusal, and returning that page as a body
    /// made <see cref="FallbackEcTransport"/> treat the block as a win (permanently switching to
    /// curl) and the listing scrape report the item as having zero loves.
    /// </summary>
    [Fact]
    public async Task Returns_null_for_a_cloudflare_challenge_despite_curl_exiting_zero()
    {
        const string challenge =
            "<!DOCTYPE html><html lang=\"en-US\"><head><title>Just a moment...</title></head></html>";

        var (listener, url) = StartServer(403, challenge);
        try
        {
            var result = await Transport().GetAsync(url, CancellationToken.None);

            result.Should().BeNull("a refused request is not a usable body, whatever curl's exit code says");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Returns_null_for_any_error_status(int status)
    {
        var (listener, url) = StartServer(status, "refused");
        try
        {
            var result = await Transport().PostJsonAsync(url, "{}", CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>A 3xx is not followed (no <c>-L</c>), so it is a non-answer rather than a body.</summary>
    [Fact]
    public async Task Returns_null_for_a_redirect()
    {
        var (listener, url) = StartServer(302, string.Empty);
        try
        {
            var result = await Transport().GetAsync(url, CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>An empty 200 is still a successful answer, and must not be confused with a block.</summary>
    [Fact]
    public async Task Returns_an_empty_body_for_a_successful_empty_response()
    {
        var (listener, url) = StartServer(200, string.Empty);
        try
        {
            var result = await Transport().GetAsync(url, CancellationToken.None);

            result.Should().Be(string.Empty);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>A body ending in digits must not have its tail mistaken for the appended status.</summary>
    [Fact]
    public async Task Preserves_a_body_that_ends_in_digits()
    {
        var (listener, url) = StartServer(200, "loves: 403");
        try
        {
            var result = await Transport().GetAsync(url, CancellationToken.None);

            result.Should().Be("loves: 403");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>A multi-line body keeps every one of its own newlines.</summary>
    [Fact]
    public async Task Preserves_a_multi_line_body()
    {
        var (listener, url) = StartServer(200, "line one\nline two\n");
        try
        {
            var result = await Transport().GetAsync(url, CancellationToken.None);

            result.Should().Be("line one\nline two\n");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Returns_null_when_the_connection_is_refused()
    {
        // Bind an ephemeral port without listening: the test owns it, but connections are refused,
        // so curl exits non-zero deterministically.
        using var refused = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        refused.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)refused.LocalEndPoint!).Port;

        var result = await Transport().GetAsync($"http://127.0.0.1:{port}/", CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>A missing binary must degrade to "no answer" so the in-process transport can take over.</summary>
    [Fact]
    public async Task Returns_null_when_the_curl_binary_is_missing()
    {
        var transport = new CurlTransport(
            "GoodGlam-Test/1.0",
            curlPath: Path.Combine(Path.GetTempPath(), "goodglam-no-such-curl.exe"));

        var result = await transport.GetAsync("http://127.0.0.1:1/", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        var (listener, url) = StartServer(200, "x");
        try
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var act = () => Transport().GetAsync(url, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            listener.Stop();
        }
    }
}
