using FluentAssertions;
using GoodGlam.History;
using Xunit;

namespace GoodGlam.Tests.History;

/// <summary>
/// Covers the tiny one-way latch shared by the notifier (writer) and the floating logo (reader):
/// it starts clear, <see cref="NotificationState.Raise"/> sets it, and <see cref="NotificationState.Clear"/>
/// resets it.
/// </summary>
public class NotificationStateTests
{
    [Fact]
    public void Starts_clear()
    {
        new NotificationState().HasUnseen.Should().BeFalse();
    }

    [Fact]
    public void Raise_sets_the_flag()
    {
        var state = new NotificationState();

        state.Raise();

        state.HasUnseen.Should().BeTrue();
    }

    [Fact]
    public void Clear_resets_the_flag()
    {
        var state = new NotificationState();
        state.Raise();

        state.Clear();

        state.HasUnseen.Should().BeFalse();
    }

    [Fact]
    public void Raise_is_idempotent_and_clear_wins_afterwards()
    {
        var state = new NotificationState();

        state.Raise();
        state.Raise();
        state.HasUnseen.Should().BeTrue();

        state.Clear();
        state.HasUnseen.Should().BeFalse();
    }
}
