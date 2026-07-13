using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using GoodGlam.Diagnostics;
using GoodGlam.Glam;
using GoodGlam.History;
using GoodGlam.Loot;
using GoodGlam.Windows;

namespace GoodGlam;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/goodglam";

    private readonly ITraceLogger<Plugin> log = new TraceLogger<Plugin>();
    private readonly Configuration config;
    private readonly WindowSystem windowSystem = new("GoodGlam");
    private readonly MainWindow mainWindow;
    private readonly DropDetailsWindow dropDetailsWindow;
    private readonly LogoWindow logoWindow;
    private readonly NotificationHistoryStore history;
    private readonly NotificationState notificationState = new();
    private readonly EorzeaCollectionClient ecClient;
    private readonly LootWatcher lootWatcher;
    private readonly CharacterDataManager characterData;

    public Plugin(IDalamudPluginInterface pluginInterface)
        : this(pluginInterface, new GameLootReader())
    {
    }

    internal Plugin(IDalamudPluginInterface pluginInterface, ILootReader lootReader)
    {
        pluginInterface.Create<Services>();

        // The live config + history start neutral (defaults, no backing file); CharacterDataManager
        // binds them to the logged-in character's own files on login. Every window/service below
        // captures these single instances, which are re-bound in place across character switches.
        this.config = new Configuration { Filters = new() };
        this.history = new NotificationHistoryStore(string.Empty);

        var charactersRoot = Path.Combine(Services.PluginInterface.ConfigDirectory.FullName, "characters");
        this.characterData = new CharacterDataManager(charactersRoot, this.config, this.history, this.notificationState);

        this.ecClient = new EorzeaCollectionClient();
        var notifier = new HistoryNotifier(this.history, this.notificationState);
        var popularity = new GlamPopularityService(this.config, this.ecClient, notifier);
        this.lootWatcher = new LootWatcher(new ItemResolver(), popularity, this.config, lootReader);

        this.dropDetailsWindow = new DropDetailsWindow();
        this.mainWindow = new MainWindow(
            this.config,
            EcFilterCatalog.LoadEmbedded(),
            this.history,
            this.SetLogoVisible,
            this.dropDetailsWindow);

        // No IsOpen initializer here: the config is neutral until a character logs in, so the logo's
        // per-character show/hide preference is applied in ActivateCurrentCharacter instead.
        this.logoWindow = new LogoWindow(this.config, this.ToggleMain, this.notificationState);
        this.windowSystem.AddWindow(this.mainWindow);
        this.windowSystem.AddWindow(this.dropDetailsWindow);
        this.windowSystem.AddWindow(this.logoWindow);

        Services.PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi += this.ToggleMain;
        Services.PluginInterface.UiBuilder.OpenMainUi += this.ToggleMain;

        Services.ClientState.Login += this.OnLogin;
        Services.ClientState.Logout += this.OnLogout;

        // If the plugin is (re)loaded while already in-game, the Login event won't fire, so adopt
        // the current character right away.
        if (Services.ClientState.IsLoggedIn)
            this.OnLogin();

        Services.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the GoodGlam window (History, Filters, Settings, About tabs). Debug: /goodglam dump, /goodglam check <itemId>, /goodglam glow, /goodglam reset.",
        });

        this.log.Information("GoodGlam loaded.");
    }

    /// <summary>
    /// Adopts the logged-in character's per-character config + history. The character's content id
    /// can lag the Login event by a frame, so when it isn't ready yet we retry on the next framework
    /// tick until it resolves.
    /// </summary>
    private void OnLogin()
    {
        if (Services.PlayerState.ContentId == 0)
        {
            // Dedupe in case login somehow fires again while we're still waiting.
            this.log.Debug("login received but ContentId not ready yet; awaiting it on the framework tick.");
            Services.Framework.Update -= this.AwaitContentId;
            Services.Framework.Update += this.AwaitContentId;
            return;
        }

        this.ActivateCurrentCharacter();
    }

    private void AwaitContentId(Dalamud.Plugin.Services.IFramework framework)
    {
        // Stop waiting if the player logged back out before the id resolved.
        if (!Services.ClientState.IsLoggedIn)
        {
            this.log.Debug("stopped awaiting ContentId — player logged out before it resolved.");
            Services.Framework.Update -= this.AwaitContentId;
            return;
        }

        if (Services.PlayerState.ContentId == 0)
            return;

        Services.Framework.Update -= this.AwaitContentId;
        this.ActivateCurrentCharacter();
    }

    private void ActivateCurrentCharacter()
    {
        var contentId = Services.PlayerState.ContentId;
        this.log.Information(
            $"activating character {Services.PlayerState.CharacterName ?? "(unknown)"} (contentId={contentId:x16}).");
        this.characterData.Activate(contentId, Services.PlayerState.CharacterName, ResolveHomeWorldName());

        // Each character has its own logo show/hide preference; reflect it now.
        this.logoWindow.IsOpen = this.config.ShowLogo;
    }

    private void OnLogout(int type, int code)
    {
        this.log.Information($"logout (type={type}, code={code}); deactivating character data.");
        Services.Framework.Update -= this.AwaitContentId;

        // Deactivate resets the live per-character state — including clearing the unseen-drop glow
        // latch — so a pending drop can't carry over to the next character.
        this.characterData.Deactivate();
        this.logoWindow.IsOpen = false;
    }

    private static string? ResolveHomeWorldName()
    {
        try
        {
            return Services.PlayerState.HomeWorld.ValueNullable?.Name.ExtractText();
        }
        catch
        {
            return null;
        }
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            this.ToggleMain();
            return;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        this.log.Debug($"command '/goodglam {trimmed}' -> subcommand '{parts[0].ToLowerInvariant()}'.");
        switch (parts[0].ToLowerInvariant())
        {
            case "config":
            case "settings":
            case "history":
                this.ToggleMain();
                break;

            case "dump":
                this.lootWatcher.DumpCurrentLoot();
                break;

            case "reset":
                // Debug: clear the seen-loot dedup set so the same open loot re-dispatches on the next
                // scan (re-open the roll window or use /goodglam check), for repeat testing.
                this.lootWatcher.ResetDispatchedDrops();
                break;

            case "check":
                if (parts.Length > 1 && uint.TryParse(parts[1].Trim(), out var itemId))
                    this.lootWatcher.SimulateDrop(itemId);
                else
                    this.log.Information("usage is /goodglam check <itemId> (a numeric game item ID).");
                break;

            case "glow":
                // Debug: light the logo glow directly, bypassing the EC lookup, so the
                // notification animation can be verified without a qualifying drop.
                this.notificationState.Raise();
                this.log.Information("glow: notification glow raised — open the window (or click the logo) to clear it.");
                break;

            default:
                this.ToggleMain();
                break;
        }
    }

    private void ToggleMain()
    {
        // Opening the window lands on the History tab, which acknowledges any pending popular drop,
        // so clear the logo glow.
        var opening = !this.mainWindow.IsOpen;
        this.log.Debug(opening
            ? "opening the GoodGlam window (History tab) and clearing the logo glow."
            : "closing the GoodGlam window.");
        this.notificationState.Clear();
        this.mainWindow.Toggle();
    }

    private void SetLogoVisible(bool visible)
    {
        this.log.Debug($"floating logo {(visible ? "shown" : "hidden")} via settings.");
        this.config.ShowLogo = visible;
        this.config.Save();
        this.logoWindow.IsOpen = visible;
    }

    public void Dispose()
    {
        Services.Commands.RemoveHandler(CommandName);

        Services.ClientState.Login -= this.OnLogin;
        Services.ClientState.Logout -= this.OnLogout;
        Services.Framework.Update -= this.AwaitContentId;

        Services.PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleMain;
        Services.PluginInterface.UiBuilder.OpenMainUi -= this.ToggleMain;
        this.windowSystem.RemoveAllWindows();

        this.mainWindow.Dispose();
        this.logoWindow.Dispose();
        this.lootWatcher.Dispose();
    }
}
