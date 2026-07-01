using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

/// <summary>
/// Drives <see cref="ManagedHttpTransport"/> (the in-process HTTP path used everywhere except native
/// Windows) against a throwaway loopback <see cref="HttpListener"/>, covering the success, HTTP-error,
/// connection-failure, and cancellation branches without touching Eorzea Collection.
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
        int status, string body, List<HttpListenerRequest>? seen = null)
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

    [Fact]
    public void Factory_creates_the_fallback_transport_stack()
        => EcTransportFactory.Create().Should().BeOfType<FallbackEcTransport>();
}
