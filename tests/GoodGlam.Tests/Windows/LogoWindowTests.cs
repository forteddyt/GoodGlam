using Dalamud.Plugin;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the config-mutating actions on <see cref="LogoWindow"/> (the right-click menu's lock
/// toggle and hide). A faked <see cref="IDalamudPluginInterface"/> is installed into the static
/// <c>Services</c> holder so <see cref="Configuration.Save"/> is observable without a framework.
/// </summary>
public class LogoWindowTests
{
    private readonly IDalamudPluginInterface pluginInterface = A.Fake<IDalamudPluginInterface>();

    public LogoWindowTests()
    {
        TestServices.EnsureLog();
        TestServices.Install("PluginInterface", this.pluginInterface);
    }

    private LogoWindow NewWindow(Configuration config)
        => new(config, openHistory: () => { }, openConfig: () => { });

    [Fact]
    public void ToggleLock_locks_then_unlocks_and_persists_each_time()
    {
        var config = new Configuration { LockLogo = false };
        var window = this.NewWindow(config);

        window.ToggleLock();
        config.LockLogo.Should().BeTrue();

        window.ToggleLock();
        config.LockLogo.Should().BeFalse();

        A.CallTo(() => this.pluginInterface.SavePluginConfig(config)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public void Hide_clears_show_flag_closes_window_and_persists()
    {
        var config = new Configuration { ShowLogo = true };
        var window = this.NewWindow(config);
        window.IsOpen = true;

        window.Hide();

        config.ShowLogo.Should().BeFalse();
        window.IsOpen.Should().BeFalse();
        A.CallTo(() => this.pluginInterface.SavePluginConfig(config)).MustHaveHappenedOnceExactly();
    }
}
