using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers <see cref="HistoryTabFocus"/>, the pure logic that keeps the unified GoodGlam window
/// landing on its History tab. The contract: each open force-selects History for exactly one frame,
/// then yields control back to the user so their tab choice sticks. This guards the regression where
/// reopening after visiting Settings reopened on Settings (ImGui persists the active tab itself).
/// </summary>
public class HistoryTabFocusTests
{
    [Fact]
    public void Forces_history_on_the_first_frame_so_a_fresh_window_lands_on_history()
    {
        var sut = new HistoryTabFocus();

        sut.Pending.Should().BeTrue();
        sut.ConsumeForceSelect().Should().BeTrue();
    }

    [Fact]
    public void Force_select_is_consumed_after_one_frame()
    {
        var sut = new HistoryTabFocus();

        sut.ConsumeForceSelect().Should().BeTrue();   // first drawn frame after open
        sut.ConsumeForceSelect().Should().BeFalse();  // user is now free to switch tabs
        sut.ConsumeForceSelect().Should().BeFalse();
        sut.Pending.Should().BeFalse();
    }

    [Fact]
    public void Reopening_forces_history_again()
    {
        var sut = new HistoryTabFocus();
        sut.ConsumeForceSelect();                      // consume the initial open
        sut.ConsumeForceSelect().Should().BeFalse();   // settled (e.g. user on Settings)

        // The window is hidden and reopened: OnOpen re-arms the force-select for the next frame.
        sut.OnOpen();

        sut.Pending.Should().BeTrue();
        sut.ConsumeForceSelect().Should().BeTrue();
        sut.ConsumeForceSelect().Should().BeFalse();
    }

    [Fact]
    public void Repeated_opens_without_a_drawn_frame_collapse_to_a_single_force()
    {
        var sut = new HistoryTabFocus();
        sut.ConsumeForceSelect(); // consume initial

        // Toggling open several times before a draw still only needs one force-select.
        sut.OnOpen();
        sut.OnOpen();

        sut.ConsumeForceSelect().Should().BeTrue();
        sut.ConsumeForceSelect().Should().BeFalse();
    }
}
