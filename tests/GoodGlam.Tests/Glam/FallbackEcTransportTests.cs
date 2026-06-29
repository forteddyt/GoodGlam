using FluentAssertions;
using GoodGlam.Glam;
using Xunit;

namespace GoodGlam.Tests.Glam;

public class FallbackEcTransportTests
{
    public FallbackEcTransportTests() => TestServices.EnsureLog();

    [Fact]
    public async Task Uses_primary_and_skips_fallback_when_primary_succeeds()
    {
        var managed = new CountingTransport("ok");
        var curl = new CountingTransport("curl");
        var fb = new FallbackEcTransport(managed, curl);

        var result = await fb.GetAsync("u", CancellationToken.None);

        result.Should().Be("ok");
        managed.Calls.Should().Be(1);
        curl.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Falls_back_to_curl_and_switches_preference()
    {
        var managed = new CountingTransport(null);
        var curl = new CountingTransport("curl");
        var fb = new FallbackEcTransport(managed, curl);

        var first = await fb.GetAsync("u", CancellationToken.None);
        first.Should().Be("curl");
        managed.Calls.Should().Be(1);
        curl.Calls.Should().Be(1);

        // Preference switched: curl is now primary, managed is no longer tried.
        var second = await fb.GetAsync("u", CancellationToken.None);
        second.Should().Be("curl");
        curl.Calls.Should().Be(2);
        managed.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Returns_null_and_keeps_preference_when_both_fail()
    {
        var managed = new CountingTransport(null);
        var curl = new CountingTransport(null);
        var fb = new FallbackEcTransport(managed, curl);

        var result = await fb.PostJsonAsync("u", "{}", CancellationToken.None);

        result.Should().BeNull();
        managed.Calls.Should().Be(1);
        curl.Calls.Should().Be(1);
    }
}
