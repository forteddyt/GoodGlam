using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>Covers the one-shot destination requests behind the unified window's tab bar.</summary>
public class MainTabFocusTests
{
    [Fact]
    public void Fresh_window_requests_history()
    {
        var sut = new MainTabFocus();

        sut.Pending.Should().Be(MainTab.History);
        sut.Consume(MainTab.History).Should().BeTrue();
    }

    [Fact]
    public void Request_is_consumed_only_by_its_destination()
    {
        var sut = new MainTabFocus();
        sut.Request(MainTab.Settings);

        sut.Consume(MainTab.History).Should().BeFalse();
        sut.Pending.Should().Be(MainTab.Settings);
        sut.Consume(MainTab.Settings).Should().BeTrue();
        sut.Pending.Should().BeNull();
        sut.Consume(MainTab.Settings).Should().BeFalse();
    }

    [Fact]
    public void Latest_request_replaces_an_undrawn_request()
    {
        var sut = new MainTabFocus();

        sut.Request(MainTab.Settings);
        sut.Request(MainTab.History);

        sut.Pending.Should().Be(MainTab.History);
        sut.Consume(MainTab.History).Should().BeTrue();
    }
}
