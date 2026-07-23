using System.Net;
using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Guards <see cref="GlamImageHttpClientFactory"/> — the cover-image download client behind the History
/// tab's hover preview. The HTTP/2 configuration here is the fix for Eorzea Collection's Cloudflare edge
/// 403ing the .NET client's default HTTP/1.1, which surfaced in-game as "No preview available". It lives
/// in a factory (rather than inline in the coverage-excluded <see cref="GoodGlam.Windows.HistoryTab"/>)
/// precisely so this regression can't be silently reverted: <see cref="GlamImageLoader"/>'s own tests
/// inject a fake byte fetch and never observe the real client, so this is the seam that asserts it.
/// </summary>
public class GlamImageHttpClientFactoryTests
{
    [Fact]
    public void Create_requests_http2_so_cloudflare_does_not_403_the_download()
    {
        using var client = GlamImageHttpClientFactory.Create();

        client.DefaultRequestVersion.Should().Be(HttpVersion.Version20);
    }

    [Fact]
    public void Create_degrades_gracefully_where_http2_cannot_be_negotiated()
    {
        using var client = GlamImageHttpClientFactory.Create();

        // OrLower (not Exact/OrHigher): negotiate HTTP/2 on the real HTTPS edge but fall back to HTTP/1.1
        // rather than throwing where HTTP/2 can't be negotiated. Exact/OrHigher would reintroduce the bug.
        client.DefaultVersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionOrLower);
    }

    [Fact]
    public void Create_sends_a_browser_shaped_user_agent()
    {
        using var client = GlamImageHttpClientFactory.Create();

        client.DefaultRequestHeaders.UserAgent.ToString()
            .Should().Be(GlamImageHttpClientFactory.UserAgent);
    }

    [Fact]
    public void Create_caps_the_download_with_a_finite_timeout()
    {
        using var client = GlamImageHttpClientFactory.Create();

        client.Timeout.Should().Be(GlamImageHttpClientFactory.Timeout);
    }
}
