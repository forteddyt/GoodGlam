using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FakeItEasy;
using FluentAssertions;
using Xunit;

namespace GoodGlam.Tests;

/// <summary>
/// Covers <see cref="Plugin"/>'s wiring and orchestration with the whole Dalamud service surface
/// faked into the static <c>Services</c> holder, plus an injected <see cref="StubLootReader"/> so the
/// debug commands don't touch the native game struct. Verifies construction/teardown, the slash
/// command router, and the login/logout lifecycle (including the content-id retry path).
/// </summary>
public class PluginTests : IDisposable
{
    private const ulong CharacterA = 0x0000_0000_0000_1111;
    private const ulong CharacterB = 0x0000_0000_0000_2222;

    private readonly string configDir;
    private readonly IDalamudPluginInterface pi = A.Fake<IDalamudPluginInterface>();
    private readonly IClientState clientState = A.Fake<IClientState>();
    private readonly ICondition condition = A.Fake<ICondition>();
    private readonly IGameGui gameGui = A.Fake<IGameGui>();
    private readonly IPlayerState playerState = A.Fake<IPlayerState>();
    private readonly ICommandManager commands = A.Fake<ICommandManager>();
    private readonly IAddonLifecycle addon = A.Fake<IAddonLifecycle>();
    private readonly IFramework framework = A.Fake<IFramework>();
    private readonly IDataManager data = A.Fake<IDataManager>();
    private readonly ITextureProvider textures = A.Fake<ITextureProvider>();
    private readonly INotificationManager notifications = A.Fake<INotificationManager>();
    private readonly IUiBuilder uiBuilder = A.Fake<IUiBuilder>();
    private readonly StubLootReader lootReader = new();

    private CommandInfo? command;

    public PluginTests()
    {
        this.configDir = Path.Combine(Path.GetTempPath(), $"goodglam-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.configDir);

        TestServices.EnsureLog();
        TestServices.Install("PluginInterface", this.pi);
        TestServices.Install("ClientState", this.clientState);
        TestServices.Install("Condition", this.condition);
        TestServices.Install("GameGui", this.gameGui);
        TestServices.Install("PlayerState", this.playerState);
        TestServices.Install("Commands", this.commands);
        TestServices.Install("AddonLifecycle", this.addon);
        TestServices.Install("Framework", this.framework);
        TestServices.Install("DataManager", this.data);
        TestServices.Install("TextureProvider", this.textures);
        TestServices.Install("Notifications", this.notifications);

        A.CallTo(() => this.pi.ConfigDirectory).Returns(new DirectoryInfo(this.configDir));
        A.CallTo(() => this.pi.UiBuilder).Returns(this.uiBuilder);
        A.CallTo(() => this.commands.AddHandler(A<string>._, A<CommandInfo>._))
            .Invokes(call => this.command = (CommandInfo)call.Arguments[1]!)
            .Returns(true);
    }

    private Plugin NewPlugin() => new(this.pi, this.lootReader);

    private string MetaPath(ulong contentId) =>
        Path.Combine(this.configDir, "characters", contentId.ToString("x16"), "meta.json");

    private static void InvokePrivate(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        m.Should().NotBeNull($"Plugin should declare a private {method} method");
        m!.Invoke(target, args);
    }

    [Fact]
    public void Ctor_registers_the_command_and_loot_listeners()
    {
        using var plugin = this.NewPlugin();

        this.command.Should().NotBeNull();
        A.CallTo(() => this.commands.AddHandler("/goodglam", A<CommandInfo>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => this.addon.RegisterListener(A<AddonEvent>._, "NeedGreed", A<IAddonLifecycle.AddonEventDelegate>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public void Public_ctor_wires_the_default_game_loot_reader()
    {
        // Exercises the production constructor, which supplies a real GameLootReader. Constructing it
        // is safe (native memory is only read when the dump command calls Read(), which we don't here).
        using var plugin = new Plugin(this.pi);

        this.command.Should().NotBeNull();
    }

    [Fact]
    public void Ctor_adopts_the_current_character_when_already_logged_in()
    {
        A.CallTo(() => this.clientState.IsLoggedIn).Returns(true);
        A.CallTo(() => this.playerState.ContentId).Returns(CharacterA);
        A.CallTo(() => this.playerState.CharacterName).Returns("Alpha");

        using var plugin = this.NewPlugin();

        File.Exists(this.MetaPath(CharacterA)).Should().BeTrue();
    }

    [Fact]
    public void Ctor_adopts_the_character_even_when_the_home_world_lookup_fails()
    {
        A.CallTo(() => this.clientState.IsLoggedIn).Returns(true);
        A.CallTo(() => this.playerState.ContentId).Returns(CharacterA);
        A.CallTo(() => this.playerState.HomeWorld).Throws(new InvalidOperationException("home world not ready"));

        using var plugin = this.NewPlugin();

        // The home-world lookup is best-effort: a failure resolves to a null world but must not block
        // adoption of the character (meta.json is still written).
        File.Exists(this.MetaPath(CharacterA)).Should().BeTrue();
    }

    [Fact]
    public void Ctor_stays_neutral_when_not_logged_in()
    {
        A.CallTo(() => this.clientState.IsLoggedIn).Returns(false);

        using var plugin = this.NewPlugin();

        Directory.Exists(Path.Combine(this.configDir, "characters")).Should().BeFalse();
    }

    [Fact]
    public void Login_waits_for_the_content_id_then_activates()
    {
        // Logged in but the content id hasn't resolved yet: OnLogin (fired from the ctor) defers.
        A.CallTo(() => this.clientState.IsLoggedIn).Returns(true);
        A.CallTo(() => this.playerState.ContentId).Returns(0UL);
        using var plugin = this.NewPlugin();

        File.Exists(this.MetaPath(CharacterB)).Should().BeFalse();

        // Still not ready on the next tick.
        InvokePrivate(plugin, "AwaitContentId", this.framework);
        File.Exists(this.MetaPath(CharacterB)).Should().BeFalse();

        // Content id resolves: the next tick adopts the character.
        A.CallTo(() => this.playerState.ContentId).Returns(CharacterB);
        InvokePrivate(plugin, "AwaitContentId", this.framework);
        File.Exists(this.MetaPath(CharacterB)).Should().BeTrue();
    }

    [Fact]
    public void AwaitContentId_stops_waiting_if_the_player_logs_back_out()
    {
        A.CallTo(() => this.clientState.IsLoggedIn).Returns(true);
        A.CallTo(() => this.playerState.ContentId).Returns(0UL);
        using var plugin = this.NewPlugin();

        A.CallTo(() => this.clientState.IsLoggedIn).Returns(false);

        plugin.Invoking(p => InvokePrivate(p, "AwaitContentId", this.framework)).Should().NotThrow();
        Directory.Exists(Path.Combine(this.configDir, "characters")).Should().BeFalse();
    }

    [Fact]
    public void Logout_deactivates_without_throwing()
    {
        A.CallTo(() => this.clientState.IsLoggedIn).Returns(true);
        A.CallTo(() => this.playerState.ContentId).Returns(CharacterA);
        using var plugin = this.NewPlugin();

        plugin.Invoking(p => InvokePrivate(p, "OnLogout", 0, 0)).Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("settings")]
    [InlineData("config")]
    [InlineData("history")]
    [InlineData("glow")]
    [InlineData("dump")]
    [InlineData("reset")]
    [InlineData("check")]
    [InlineData("check not-a-number")]
    [InlineData("check 3610")]
    [InlineData("something-else")]
    public void OnCommand_routes_every_argument_shape_without_throwing(string args)
    {
        using var plugin = this.NewPlugin();
        this.command.Should().NotBeNull();

        this.command!.Handler.Invoking(h => h.Invoke("/goodglam", args)).Should().NotThrow();
    }

    [Fact]
    public void SetLogoVisible_persists_without_throwing()
    {
        using var plugin = this.NewPlugin();

        plugin.Invoking(p =>
        {
            InvokePrivate(p, "SetLogoVisible", true);
            InvokePrivate(p, "SetLogoVisible", false);
        }).Should().NotThrow();
    }

    [Fact]
    public void Dispose_removes_the_command_and_loot_listeners()
    {
        var plugin = this.NewPlugin();

        plugin.Dispose();

        A.CallTo(() => this.commands.RemoveHandler("/goodglam")).MustHaveHappenedOnceExactly();
        A.CallTo(() => this.addon.UnregisterListener(A<IAddonLifecycle.AddonEventDelegate[]>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    public void Dispose()
    {
        // The static Services holder is process-global; clear this class's broad set of fakes so they
        // can't leak into later-running test classes (see TestServices.ResetServices).
        TestServices.ResetServices();

        if (Directory.Exists(this.configDir))
            Directory.Delete(this.configDir, recursive: true);
    }
}
