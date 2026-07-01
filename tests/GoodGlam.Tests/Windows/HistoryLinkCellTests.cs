using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the pure History-tab helpers: <see cref="HistoryLinkCell.Resolve"/> (link vs plain vs
/// disabled, plus which text to show) and <see cref="HistoryActions.OpenLink"/> (opens only a real
/// URL). Both are the logic pulled out of the excluded <c>HistoryTab</c> draw code.
/// </summary>
public class HistoryLinkCellTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void Resolve_disabled_when_no_url_and_no_label(string? label, string? url)
    {
        var cell = HistoryLinkCell.Resolve(label, url, "(n/a)");

        cell.Kind.Should().Be(HistoryLinkKind.Disabled);
        cell.Text.Should().Be("(n/a)");
        cell.Url.Should().BeNull();
    }

    [Fact]
    public void Resolve_plain_text_when_a_label_exists_without_a_url()
    {
        var cell = HistoryLinkCell.Resolve("My Glam", url: null, "(n/a)");

        cell.Kind.Should().Be(HistoryLinkKind.PlainText);
        cell.Text.Should().Be("My Glam");
        cell.Url.Should().BeNull();
    }

    [Fact]
    public void Resolve_link_using_the_label_when_both_label_and_url_exist()
    {
        var cell = HistoryLinkCell.Resolve("My Glam", "https://ec/glamour/1", "(n/a)");

        cell.Kind.Should().Be(HistoryLinkKind.Link);
        cell.Text.Should().Be("My Glam");
        cell.Url.Should().Be("https://ec/glamour/1");
    }

    [Fact]
    public void Resolve_link_falls_back_to_the_url_as_text_when_no_label()
    {
        var cell = HistoryLinkCell.Resolve(label: null, "https://ec/glamour/1", "(n/a)");

        cell.Kind.Should().Be(HistoryLinkKind.Link);
        cell.Text.Should().Be("https://ec/glamour/1");
        cell.Url.Should().Be("https://ec/glamour/1");
    }
}

public class HistoryActionsTests
{
    [Fact]
    public void OpenLink_opens_a_populated_url_once()
    {
        var opener = new FakeLinkOpener();

        new HistoryActions(opener).OpenLink("https://ec/glamour/1");

        opener.Opened.Should().ContainSingle().Which.Should().Be("https://ec/glamour/1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OpenLink_ignores_a_missing_url(string? url)
    {
        var opener = new FakeLinkOpener();

        new HistoryActions(opener).OpenLink(url);

        opener.Opened.Should().BeEmpty();
    }
}
