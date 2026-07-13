using FluentAssertions;
using GoodGlam.History;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

public class HistoryDetailsStateTests
{
    private static PopularDropRecord Record() => new(
        3610,
        "Cavalry Gauntlets",
        "hands",
        250,
        "Nirvana",
        "https://ec/glamour/200",
        new DateTimeOffset(2026, 7, 12, 21, 19, 32, TimeSpan.Zero),
        "The Aurum Vale");

    [Fact]
    public void Close_clears_the_selected_details()
    {
        var state = new HistoryDetailsState();
        state.Open(Record());

        state.Close();

        state.Selected.Should().BeNull();
    }

    [Fact]
    public void Open_selects_the_record()
    {
        var record = Record();
        var state = new HistoryDetailsState();
        state.Open(record);

        state.Selected.Should().BeSameAs(record);
    }
}
