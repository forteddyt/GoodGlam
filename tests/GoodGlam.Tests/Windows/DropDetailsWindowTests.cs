using FluentAssertions;
using GoodGlam.Windows;
using System.Numerics;
using Xunit;

namespace GoodGlam.Tests.Windows;

public class DropDetailsWindowTests
{
    [Fact]
    public void Uses_Dalamuds_standard_Escape_close_behavior()
        => new DropDetailsWindow().RespectCloseHotkey.Should().BeTrue();

    [Fact]
    public void Click_away_waits_for_the_opening_click_to_be_released()
    {
        var dismissal = new ClickAwayDismissal();
        dismissal.SuppressUntilRelease();

        dismissal.Update(mouseDown: true, mouseClicked: true, windowHovered: false).Should().BeFalse();
        dismissal.Update(mouseDown: false, mouseClicked: false, windowHovered: false).Should().BeFalse();
        dismissal.Update(mouseDown: true, mouseClicked: true, windowHovered: false).Should().BeTrue();
    }

    [Fact]
    public void Click_inside_does_not_close()
    {
        var dismissal = new ClickAwayDismissal();

        dismissal.Update(mouseDown: true, mouseClicked: true, windowHovered: true).Should().BeFalse();
    }

    [Fact]
    public void Layout_centers_a_once_scaled_window_inside_the_host()
    {
        var layout = DropDetailsLayout.Compute(
            new Vector2(100f, 200f),
            new Vector2(1120f, 1040f),
            globalScale: 2f);

        layout.LogicalSize.Should().Be(new Vector2(360f, 170f));
        layout.Position.Should().Be(new Vector2(300f, 550f));
    }
}
