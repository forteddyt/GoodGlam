using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

/// <summary>
/// Drives <see cref="ManagedHttpTransport"/> - the in-process HTTP path, now the only way GoodGlam
/// reaches Eorzea Collection on every platform - against a throwaway loopback
/// <see cref="HttpListener"/>, covering the success, HTTP-error, connection-failure, and
/// cancellation branches without touching Eorzea Collection.
/// </summary>
public class ManagedHttpTransportTests
{
    public ManagedHttpTransportTests() => TestServices.EnsureLog();

    private static int NextCandidatePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static (HttpListener Listener, string Url) StartServer(
        int status,
        string body,
        List<HttpListenerRequest>? seen = null,
        IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        // HttpListener can't bind to an ephemeral port and report it back, so pick a candidate and
        // retry Start() if it was grabbed between selection and bind — closing the release-then-rebind
        // race that would otherwise flake on a busy runner.
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
                if (responseHeaders is not null)
                {
                    foreach (var (name, value) in responseHeaders)
                        ctx.Response.Headers[name] = value;
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            });

            return (listener, url);
        }
    }

    [Fact]
    public async Task PostJsonAsync_returns_the_response_body_on_success()
    {
        var seen = new List<HttpListenerRequest>();
        var (listener, url) = StartServer(200, "search-result", seen);
        try
        {
            var result = await new ManagedHttpTransport("GoodGlam-Test/1.0")
                .PostJsonAsync(url, """{"search":"x"}""", CancellationToken.None);

            result.Should().Be("search-result");
            seen.Should().ContainSingle().Which.HttpMethod.Should().Be("POST");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAsync_returns_the_response_body_on_success()
    {
        var (listener, url) = StartServer(200, "<html>listing</html>");
        try
        {
            var result = await new ManagedHttpTransport("GoodGlam-Test/1.0")
                .GetAsync(url, CancellationToken.None);

            result.Should().Be("<html>listing</html>");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Returns_null_on_non_success_status()
    {
        var (listener, url) = StartServer(403, "blocked");
        try
        {
            var result = await new ManagedHttpTransport("GoodGlam-Test/1.0")
                .GetAsync(url, CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Returns_null_when_the_request_fails()
    {
        // Bind a socket to an ephemeral port but never Listen(): the test owns the port (nothing else
        // can grab it) yet connections are refused, so the transport fails deterministically → null.
        using var refused = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        refused.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)refused.LocalEndPoint!).Port;
        var url = $"http://127.0.0.1:{port}/";

        var result = await new ManagedHttpTransport("GoodGlam-Test/1.0")
            .GetAsync(url, CancellationToken.None);

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

            var act = () => new ManagedHttpTransport("GoodGlam-Test/1.0").GetAsync(url, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// The factory hands back the in-process transport directly. GoodGlam no longer pairs it with a
    /// curl.exe subprocess: EC refuses by HTTP version rather than TLS fingerprint, so pinning
    /// HTTP/2 makes the managed client work on every platform - including native Windows, where the
    /// bundled curl.exe couldn't have helped anyway because it has no HTTP/2 support.
    /// </summary>
    [Fact]
    public void Factory_creates_the_in_process_transport()
        => EcTransportFactory.Create().Should().BeOfType<ManagedHttpTransport>();

    /// <summary>
    /// The transport and the integration tests' reachability probe must send the same request, or
    /// the probe can report success for something the plugin never sends. Both build their requests
    /// through <see cref="EcRequest"/>; pin the pieces that decide whether EC answers.
    /// </summary>
    [Fact]
    public void Ec_requests_pin_http2_and_the_browser_user_agent()
    {
        using var post = EcRequest.Post("https://example.invalid/gear/body/search", """{"search":"x"}""");
        using var get = EcRequest.Get("https://example.invalid/glamours");

        foreach (var req in new[] { post, get })
        {
            req.Version.Should().Be(HttpVersion.Version20, "EC's edge 403s HTTP/1.1");
            req.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionOrLower);

            // GetValues re-splits a user agent into its product tokens, so compare the rejoined value.
            string.Join(" ", req.Headers.GetValues("User-Agent")).Should().Be(EcRequest.UserAgent);
        }

        post.Headers.GetValues("X-Requested-With").Should().ContainSingle().Which.Should().Be("XMLHttpRequest");
        post.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    /// <summary>
    /// With the curl.exe fallback gone there is no second transport to try, so a refusal has to
    /// explain itself in the log or the user is left with a bare status. A Cloudflare challenge is
    /// aimed at the caller's IP and no amount of retrying or reconfiguring gets past it, so say so.
    /// </summary>
    [Fact]
    public async Task Names_a_cloudflare_challenge_in_the_log()
    {
        var log = new RecordingLogger();
        var (listener, url) = StartServer(
            403, "blocked", responseHeaders: new Dictionary<string, string> { ["cf-mitigated"] = "challenge" });
        try
        {
            var result = await new ManagedHttpTransport("GoodGlam-Test/1.0", log).GetAsync(url, CancellationToken.None);

            result.Should().BeNull();
            log.Debugs.Should().ContainSingle(m => m.Contains("Cloudflare challenge for this IP"));
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// The other refusal worth telling apart: EC answers HTTP/2 and 403s HTTP/1.1, so a request that
    /// failed to negotiate HTTP/2 is refused for a completely different (and fixable) reason than a
    /// bot challenge. The loopback listener is plaintext and therefore always HTTP/1.1, which is
    /// exactly the downgrade this branch describes.
    /// </summary>
    [Fact]
    public async Task Names_an_http1_downgrade_when_a_403_arrives_over_http1()
    {
        var log = new RecordingLogger();
        var (listener, url) = StartServer(403, "blocked");
        try
        {
            var result = await new ManagedHttpTransport("GoodGlam-Test/1.0", log).GetAsync(url, CancellationToken.None);

            result.Should().BeNull();
            log.Debugs.Should().ContainSingle(m => m.Contains("fell back to HTTP/1.1"));
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// A hand-written recorder. A FakeItEasy fake can't stand in here because
    /// <c>ITraceLogger&lt;ManagedHttpTransport&gt;</c> is parameterised with an internal type, which
    /// the dynamic-proxy backend can't proxy without exposing internals to DynamicProxyGenAssembly2.
    /// </summary>
    private sealed class RecordingLogger : ITraceLogger<ManagedHttpTransport>
    {
        public List<string> Debugs { get; } = new();

        public void Verbose(string message) { }

        public void Debug(string message) => this.Debugs.Add(message);

        public void Information(string message) { }

        public void Warning(string message) { }

        public void Warning(string message, Exception exception) { }

        public void Error(string message) { }

        public void Error(string message, Exception exception) { }
    }
}
