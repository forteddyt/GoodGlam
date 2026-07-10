namespace GoodGlam.Windows;

/// <summary>
/// Delays the logo until gameplay readiness has remained stable long enough for the native HUD's
/// final loading transition to complete.
/// </summary>
internal sealed class LogoVisibilityGate
{
    internal static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider timeProvider;
    private readonly TimeSpan settleDelay;
    private long? readySince;

    public LogoVisibilityGate()
        : this(TimeProvider.System, DefaultSettleDelay)
    {
    }

    internal LogoVisibilityGate(TimeProvider timeProvider, TimeSpan settleDelay)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(settleDelay, TimeSpan.Zero);

        this.timeProvider = timeProvider;
        this.settleDelay = settleDelay;
    }

    public bool ShouldDraw(bool gameplayReady)
    {
        if (!gameplayReady)
        {
            this.readySince = null;
            return false;
        }

        var now = this.timeProvider.GetTimestamp();
        this.readySince ??= now;
        return this.timeProvider.GetElapsedTime(this.readySince.Value, now) >= this.settleDelay;
    }
}
