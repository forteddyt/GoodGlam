using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers <see cref="LogoInteraction"/>, the pure pointer-interaction logic behind the floating
/// logo button: a plain click opens history, an unlocked drag moves the window and suppresses the
/// trailing click, a locked logo never drags, and the drag latch clears on mouse release.
/// </summary>
public class LogoInteractionTests
{
    private static LogoInteraction.Input Frame(
        bool held = false, bool mouseDragging = false, bool clicked = false,
        bool mouseReleased = false, bool locked = false)
        => new(held, mouseDragging, clicked, mouseReleased, locked);

    [Fact]
    public void Plain_click_opens_history_and_does_not_move()
    {
        var sut = new LogoInteraction();

        var outcome = sut.Process(Frame(clicked: true));

        outcome.OpenHistory.Should().BeTrue();
        outcome.MoveWindow.Should().BeFalse();
        outcome.AllowTooltip.Should().BeTrue();
        sut.Dragging.Should().BeFalse();
    }

    [Fact]
    public void Unlocked_drag_moves_window_and_latches_drag()
    {
        var sut = new LogoInteraction();

        var outcome = sut.Process(Frame(held: true, mouseDragging: true));

        outcome.MoveWindow.Should().BeTrue();
        sut.Dragging.Should().BeTrue();
    }

    [Fact]
    public void Drag_suppresses_click_and_tooltip_in_same_frame()
    {
        var sut = new LogoInteraction();

        // ImageButton can report a click on the release that ends a drag; it must not open history.
        var outcome = sut.Process(Frame(held: true, mouseDragging: true, clicked: true));

        outcome.MoveWindow.Should().BeTrue();
        outcome.OpenHistory.Should().BeFalse();
        outcome.AllowTooltip.Should().BeFalse();
    }

    [Fact]
    public void Click_stays_suppressed_on_subsequent_frame_while_drag_latched()
    {
        var sut = new LogoInteraction();
        sut.Process(Frame(held: true, mouseDragging: true)); // latch the drag

        // Next frame: still held, a click flag appears but the latch is not yet cleared.
        var outcome = sut.Process(Frame(held: true, clicked: true));

        outcome.OpenHistory.Should().BeFalse();
        sut.Dragging.Should().BeTrue();
    }

    [Fact]
    public void Locked_never_drags_and_click_still_opens()
    {
        var sut = new LogoInteraction();

        var outcome = sut.Process(Frame(held: true, mouseDragging: true, clicked: true, locked: true));

        outcome.MoveWindow.Should().BeFalse();
        outcome.OpenHistory.Should().BeTrue();
        outcome.AllowTooltip.Should().BeTrue();
        sut.Dragging.Should().BeFalse();
    }

    [Fact]
    public void Mouse_release_clears_drag_latch()
    {
        var sut = new LogoInteraction();
        sut.Process(Frame(held: true, mouseDragging: true)); // latch
        sut.Dragging.Should().BeTrue();

        sut.Process(Frame(mouseReleased: true));

        sut.Dragging.Should().BeFalse();
    }

    [Fact]
    public void Full_drag_then_release_then_click_opens_history()
    {
        var sut = new LogoInteraction();

        sut.Process(Frame(held: true, mouseDragging: true));            // drag starts
        sut.Process(Frame(held: true, mouseDragging: true));            // keeps dragging
        sut.Process(Frame(mouseReleased: true));                        // release clears latch
        var outcome = sut.Process(Frame(clicked: true));               // fresh click

        outcome.OpenHistory.Should().BeTrue();
        sut.Dragging.Should().BeFalse();
    }

    [Fact]
    public void Mouse_dragging_without_holding_logo_does_not_move()
    {
        var sut = new LogoInteraction();

        // Drag began elsewhere on screen; the logo isn't the active item.
        var outcome = sut.Process(Frame(held: false, mouseDragging: true));

        outcome.MoveWindow.Should().BeFalse();
        sut.Dragging.Should().BeFalse();
    }
}
