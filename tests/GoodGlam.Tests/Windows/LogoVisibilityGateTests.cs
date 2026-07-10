using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

public class LogoVisibilityGateTests
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly ManualTimeProvider time = new();

    [Fact]
    public void Ready_state_must_remain_stable_for_the_settle_delay()
    {
        var gate = new LogoVisibilityGate(this.time, SettleDelay);

        gate.ShouldDraw(gameplayReady: true).Should().BeFalse();

        this.time.Advance(SettleDelay - TimeSpan.FromMilliseconds(1));
        gate.ShouldDraw(gameplayReady: true).Should().BeFalse();

        this.time.Advance(TimeSpan.FromMilliseconds(1));
        gate.ShouldDraw(gameplayReady: true).Should().BeTrue();
    }

    [Fact]
    public void Losing_readiness_hides_immediately_and_restarts_the_settle_delay()
    {
        var gate = new LogoVisibilityGate(this.time, SettleDelay);

        gate.ShouldDraw(gameplayReady: true).Should().BeFalse();
        this.time.Advance(SettleDelay);
        gate.ShouldDraw(gameplayReady: true).Should().BeTrue();

        gate.ShouldDraw(gameplayReady: false).Should().BeFalse();
        gate.ShouldDraw(gameplayReady: true).Should().BeFalse();

        this.time.Advance(SettleDelay);
        gate.ShouldDraw(gameplayReady: true).Should().BeTrue();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => this.timestamp;

        public void Advance(TimeSpan elapsed) => this.timestamp += elapsed.Ticks;
    }
}
