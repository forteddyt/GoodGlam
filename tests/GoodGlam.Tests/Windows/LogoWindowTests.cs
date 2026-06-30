using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FakeItEasy;
using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the config-mutating actions on <see cref="LogoWindow"/> (the right-click menu's lock
/// toggle and hide) and its login-gated visibility. A faked <see cref="IDalamudPluginInterface"/>
/// is installed into the static <c>Services</c> holder so <see cref="Configuration.Save"/> is
/// observable without a framework; a faked <see cref="IClientState"/> drives the login gate.
/// </summary>
public class LogoWindowTests
{
    private readonly IDalamudPluginInterface pluginInterface = A.Fake<IDalamudPluginInterface>();
    private readonly IClientState clientState = A.Fake<IClientState>();

    public LogoWindowTests()
    {
        TestServices.EnsureLog();
        TestServices.Install("PluginInterface", this.pluginInterface);
        TestServices.Install("ClientState", this.clientState);
    }

    private LogoWindow NewWindow(Configuration config)
        => new(config, openMain: () => { }, new GoodGlam.History.NotificationState());

    [Fact]
    public void ToggleLock_locks_then_unlocks_and_persists_each_time()
    {
        var saves = 0;
        var config = new Configuration { LockLogo = false, SaveSink = _ => saves++ };
        var window = this.NewWindow(config);

        window.ToggleLock();
        config.LockLogo.Should().BeTrue();

        window.ToggleLock();
        config.LockLogo.Should().BeFalse();

        saves.Should().Be(2);
    }

    [Fact]
    public void Hide_clears_show_flag_closes_window_and_persists()
    {
        var saves = 0;
        var config = new Configuration { ShowLogo = true, SaveSink = _ => saves++ };
        var window = this.NewWindow(config);
        window.IsOpen = true;

        window.Hide();

        config.ShowLogo.Should().BeFalse();
        window.IsOpen.Should().BeFalse();
        saves.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DrawConditions_follows_login_state(bool loggedIn)
    {
        A.CallTo(() => this.clientState.IsLoggedIn).Returns(loggedIn);
        var window = this.NewWindow(new Configuration());

        window.DrawConditions().Should().Be(loggedIn);
    }

    [Fact]
    public void DrawConditions_tracks_login_logout_transitions_on_one_instance()
    {
        var loggedIn = false;
        A.CallTo(() => this.clientState.IsLoggedIn).ReturnsLazily(() => loggedIn);
        var window = this.NewWindow(new Configuration());

        // Character select (logged out): hidden.
        window.DrawConditions().Should().BeFalse();

        // Log in: appears.
        loggedIn = true;
        window.DrawConditions().Should().BeTrue();

        // Log out: disappears again — no latched state keeps it visible.
        loggedIn = false;
        window.DrawConditions().Should().BeFalse();
    }
}
